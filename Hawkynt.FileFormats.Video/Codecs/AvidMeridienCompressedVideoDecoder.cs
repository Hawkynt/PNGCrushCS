using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Decodes Avid Meridien Compressed (<c>AVDJ</c>) video.</summary>
/// <remarks>
/// Meridien Compressed is intra-only Avid JFIF/Motion JPEG. A progressive packet is one complete
/// JPEG. Interlaced standard-definition packets carry two complete JPEG field pictures back to back;
/// NTSC is lower-field-first and PAL is upper-field-first. There are therefore no P/B pictures and
/// no reference-picture state: every packet is independently decodable and is a key frame.
/// <para/>
/// Avid permits the JPEG coded picture to be taller than the container's display geometry. As with
/// FFmpeg's AVDJ compatibility path, the excess is discarded from the top and the displayed image
/// keeps the bottom-left rectangle. Field placement is taken first from QuickTime's <c>fiel</c> sample
/// description, then from the old Avid MJPEG extradata discriminator, and finally from the two D1
/// Meridien geometries whose polarity is defined by Avid (486-line NTSC and 576-line PAL).
/// <para/>
/// Avid documentation also describes an alpha-capable Meridien Compressed variant, but no public
/// bitstream description found during implementation defines how that alpha is represented. This
/// decoder deliberately does not reinterpret an additional JPEG or a four-component JPEG as alpha;
/// doing so would silently turn ordinary CMYK/YCCK JPEG syntax into a different colour model.
/// <para/>
/// The implementation was derived clean-room from Avid's published field-order/layout documentation,
/// Apple's public QuickTime <c>fiel</c> description, ITU-T T.81 JPEG, and the observable behavior of
/// FFmpeg's LGPL-2.1-or-later MJPEG decoder. No implementation code is reproduced here.
/// </remarks>
public sealed class AvidMeridienCompressedVideoDecoder : IVideoCodecDecoder<AvidMeridienCompressedVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVDJ");
  private const int _VISUAL_SAMPLE_ENTRY_HEADER = 8 + 78;

  private readonly int _streamIndex;
  private readonly int _width;
  private readonly int _height;
  private readonly bool? _firstCodedFieldOnOddRows;

  private AvidMeridienCompressedVideoDecoder(MediaStreamInfo stream) {
    this._streamIndex = stream.Index;
    this._width = stream.Width;
    this._height = stream.Height;
    this._firstCodedFieldOnOddRows = _FieldPlacement(stream.CodecPrivateData.Span, stream.Height);
  }

  public static string CodecName => "Avid Meridien Compressed";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AvidMeridienCompressedVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var firstLength = JpegChunkLayout.FirstImageLength(data);
    if (firstLength == 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an AVDJ packet whose first field is not a complete JPEG picture.");

    var first = JpegFile.ToRawImage(JpegReader.FromSpan(data[..firstLength]));
    if (!this._LooksInterlaced(first.Height)) {
      frame = this._CropToDisplay(first);
      return true;
    }

    var secondOffset = firstLength;
    while (secondOffset < data.Length && data[secondOffset] == 0)
      ++secondOffset;

    if (secondOffset >= data.Length || data.Length - secondOffset < 2
        || data[secondOffset] != 0xFF || data[secondOffset + 1] != 0xD8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an interlaced AVDJ packet whose second JPEG field is missing.");

    var secondLength = JpegChunkLayout.FirstImageLength(data[secondOffset..]);
    if (secondLength == 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an interlaced AVDJ packet whose second field is not a complete JPEG picture.");

    var second = JpegFile.ToRawImage(JpegReader.FromSpan(data.Slice(secondOffset, secondLength)));
    if (first.Width != second.Width || first.Height != second.Height || first.Format != second.Format)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries AVDJ fields with incompatible decoded layouts: "
        + $"{first.Width}x{first.Height} {first.Format} and {second.Width}x{second.Height} {second.Format}.");

    if (this._firstCodedFieldOnOddRows is not { } firstCodedFieldOnOddRows)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries two-field AVDJ at {this._width}x{this._height}, but neither its "
        + "codec data nor a standard Meridien D1 geometry states the spatial placement of the first coded field.");

    frame = this._CropToDisplay(_Weave(first, second, firstCodedFieldOnOddRows));
    return true;
  }

  private bool _LooksInterlaced(int codedFieldHeight)
    => this._height > 0 && (long)codedFieldHeight * 4 < (long)this._height * 3;

  private RawImage _CropToDisplay(RawImage decoded) {
    var width = this._width > 0 ? this._width : decoded.Width;
    var height = this._height > 0 ? this._height : decoded.Height;
    if (width > decoded.Width || height > decoded.Height)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a picture size of {width}x{height}, larger than the "
        + $"{decoded.Width}x{decoded.Height} its AVDJ packet codes.");

    if (width == decoded.Width && height == decoded.Height)
      return decoded;

    var bytesPerPixel = RawImage.BytesPerPixel(decoded.Format);
    if (bytesPerPixel <= 0)
      throw new NotSupportedException($"Avid Meridien display cropping does not support decoded pixel format {decoded.Format}.");

    var sourceStride = checked(decoded.Width * bytesPerPixel);
    var targetStride = checked(width * bytesPerPixel);
    var pixels = new byte[checked(targetStride * height)];
    var firstRow = decoded.Height - height;

    for (var row = 0; row < height; ++row)
      decoded.PixelData.AsSpan((firstRow + row) * sourceStride, targetStride)
        .CopyTo(pixels.AsSpan(row * targetStride, targetStride));

    return new() {
      Width = width,
      Height = height,
      Format = decoded.Format,
      PixelData = pixels,
      ColorInfo = decoded.ColorInfo,
      Palette = decoded.Palette,
      PaletteCount = decoded.PaletteCount,
      AlphaTable = decoded.AlphaTable,
      Metadata = decoded.Metadata,
    };
  }

  private static RawImage _Weave(RawImage first, RawImage second, bool firstFieldOnOddRows) {
    var bytesPerPixel = RawImage.BytesPerPixel(first.Format);
    if (bytesPerPixel <= 0)
      throw new NotSupportedException($"Avid Meridien field weaving does not support decoded pixel format {first.Format}.");

    var stride = checked(first.Width * bytesPerPixel);
    var height = checked(first.Height * 2);
    var pixels = new byte[checked(stride * height)];
    var firstParity = firstFieldOnOddRows ? 1 : 0;
    var secondParity = 1 - firstParity;

    for (var fieldRow = 0; fieldRow < first.Height; ++fieldRow) {
      first.PixelData.AsSpan(fieldRow * stride, stride)
        .CopyTo(pixels.AsSpan((fieldRow * 2 + firstParity) * stride, stride));
      second.PixelData.AsSpan(fieldRow * stride, stride)
        .CopyTo(pixels.AsSpan((fieldRow * 2 + secondParity) * stride, stride));
    }

    return new() {
      Width = first.Width,
      Height = height,
      Format = first.Format,
      PixelData = pixels,
      ColorInfo = first.ColorInfo,
      Metadata = first.Metadata,
    };
  }

  /// <summary>Returns whether the first coded field occupies odd zero-based output rows.</summary>
  private static bool? _FieldPlacement(ReadOnlySpan<byte> privateData, int height) {
    if (_QuickTimeFieldPlacement(privateData) is { } quickTime)
      return quickTime;

    if (_AvidExtraFieldPlacement(privateData) is { } avid)
      return avid;

    // AVI readers in this package retain the complete BITMAPINFOHEADER before codec-private bytes.
    if (privateData.Length >= 40) {
      var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(privateData);
      if (headerSize >= 40 && headerSize <= privateData.Length
          && _AvidExtraFieldPlacement(privateData[(int)headerSize..]) is { } afterHeader)
        return afterHeader;
    }

    return height switch {
      486 => true,  // 525-line / NTSC: lower field first.
      576 => false, // 625-line / PAL: upper field first.
      _ => null,
    };
  }

  private static bool? _AvidExtraFieldPlacement(ReadOnlySpan<byte> data) {
    if (data.Length <= 12
        || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x2C
        || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0x18)
      return null;

    return data[12] switch {
      1 => true,  // NTSC
      2 => false, // PAL
      _ => null,
    };
  }

  private static bool? _QuickTimeFieldPlacement(ReadOnlySpan<byte> data) {
    if (data.Length < _VISUAL_SAMPLE_ENTRY_HEADER)
      return null;

    for (var position = _VISUAL_SAMPLE_ENTRY_HEADER; position + 8 <= data.Length;) {
      var size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
      if (size < 8 || size > int.MaxValue || position > data.Length - (int)size)
        return null;

      if (data.Slice(position + 4, 4).SequenceEqual("fiel"u8) && size >= 10) {
        var fields = data[position + 8];
        var detail = data[position + 9];
        if (fields != 2)
          return null;

        // QuickTime/FFmpeg distinguish coded order from display order. For spatial weaving we only
        // need the coded field's parity: TT/TB code top first, BB/BT code bottom first.
        return detail switch {
          1 or 9 => false,
          6 or 14 => true,
          _ => null,
        };
      }

      position += (int)size;
    }

    return null;
  }
}
