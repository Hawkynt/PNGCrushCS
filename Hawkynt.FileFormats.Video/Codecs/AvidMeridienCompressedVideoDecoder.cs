using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Decodes Avid Meridien Compressed (<c>AVDJ</c>) video.</summary>
/// <remarks>
/// Meridien Compressed is intra-only Avid JFIF. A progressive packet is one complete JPEG.
/// An interlaced standard-definition packet carries two complete JPEG field pictures back to back,
/// each half the frame's height, and is woven into one frame here. There are therefore no P or B
/// pictures and no reference-picture state: every packet is independently decodable and is a key
/// frame.
/// <para/>
/// <b>Two things the JPEG itself does not say.</b> The first is that Avid permits the coded picture
/// to be taller than the container's display geometry — an encoder padding to a whole macroblock row
/// and never trimming the frame header back — and that the padding sits above the picture, so the
/// bottom rows are the ones kept. The second is which output row parity the first coded field
/// belongs on. Both are handled through <see cref="AvidMotionJpegLayout"/>, which AVRn shares.
/// <para/>
/// <b>Where the field placement comes from</b>, in order: QuickTime's <c>fiel</c> image-description
/// extension, then the Video-for-Windows Avid discriminator (<c>2C 00 00 00 18 00 00 00</c> followed
/// by 1 for NTSC or 2 for PAL at byte twelve) either alone or behind a complete
/// <c>BITMAPINFOHEADER</c>, then the two D1 Meridien rasters whose polarity Avid fixes — 486-line
/// NTSC stores its lower field first, 576-line PAL its upper. A two-field packet at a geometry none
/// of those covers is refused rather than woven one way and hoped for.
/// <para/>
/// Avid documentation also describes an alpha-bearing Meridien Compressed variant, but no public
/// bitstream description found during implementation says how that alpha is represented. This
/// decoder deliberately does not reinterpret a second JPEG or a four-component JPEG as alpha; doing
/// so would silently turn ordinary CMYK/YCCK JPEG syntax into a different colour model.
/// <para/>
/// Derived from Apple's published QuickTime image-description documentation, ITU-T T.81, Avid's own
/// statements of the D1 field order, and measurement of the observable behaviour of FFmpeg's
/// LGPL-2.1-or-later MJPEG decoder. No implementation code is reproduced here; what was measured and
/// how is in <c>codec-notes.md</c>.
/// </remarks>
public sealed class AvidMeridienCompressedVideoDecoder : IVideoCodecDecoder<AvidMeridienCompressedVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVDJ");

  /// <summary>An ISO base media box header plus the fixed part of a <c>VisualSampleEntry</c>.</summary>
  private const int _VISUAL_SAMPLE_ENTRY_HEADER = 8 + 78;

  private const int _BITMAP_INFO_HEADER_SIZE = 40;

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

  /// <summary>What a person calls this codec, and what a refusal message names it by.</summary>
  public static string CodecName => "Avid Meridien Compressed";

  /// <summary>Takes a video stream tagged <c>AVDJ</c> in either case.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Builds a decoder for one stream, reading its field placement once.</summary>
  public static AvidMeridienCompressedVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(stream);
  }

  /// <summary>Decodes one packet: one JPEG picture, or two field pictures woven into one frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var firstLength = JpegChunkLayout.FirstImageLength(data);
    if (firstLength <= 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an AVDJ packet whose first field is not a complete JPEG picture.");

    var first = JpegFile.ToRawImage(JpegReader.FromSpan(data[..firstLength]));
    frame = AvidMotionJpegLayout.IsCodedField(first.Height, this._height)
      ? this._Weave(first, data[firstLength..])
      : first;

    frame = AvidMotionJpegLayout.CropToDisplay(
      frame,
      this._width > 0 ? this._width : frame.Width,
      this._height > 0 ? this._height : frame.Height,
      this._streamIndex,
      "AVDJ packet");
    return true;
  }

  /// <summary>Reads the second coded field out of what follows the first and interleaves the two.</summary>
  /// <remarks>
  /// Zero padding between the two pictures is skipped: Avid pads a field out to a boundary rather
  /// than starting the next <c>FF D8</c> immediately.
  /// </remarks>
  private RawImage _Weave(RawImage first, ReadOnlySpan<byte> rest) {
    var start = 0;
    while (start < rest.Length && rest[start] == 0)
      ++start;

    if (rest.Length - start < 2 || rest[start] != 0xFF || rest[start + 1] != 0xD8)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an interlaced AVDJ packet whose second JPEG field is missing.");

    var secondLength = JpegChunkLayout.FirstImageLength(rest[start..]);
    if (secondLength <= 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an interlaced AVDJ packet whose second field is not a complete JPEG picture.");

    var second = JpegFile.ToRawImage(JpegReader.FromSpan(rest.Slice(start, secondLength)));
    if (first.Width != second.Width || first.Height != second.Height || first.Format != second.Format)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries AVDJ fields with incompatible decoded layouts: "
        + $"{first.Width}x{first.Height} {first.Format} and {second.Width}x{second.Height} {second.Format}.");

    if (this._firstCodedFieldOnOddRows is not { } firstCodedFieldOnOddRows)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries two-field AVDJ at {this._width}x{this._height}, but neither its "
        + "codec data nor a standard Meridien D1 geometry states the spatial placement of the first coded field.");

    return AvidMotionJpegLayout.Weave(first, second, firstCodedFieldOnOddRows);
  }

  /// <summary>
  /// Returns whether the first coded field occupies odd zero-based output rows, or null when nothing
  /// the stream carries says.
  /// </summary>
  private static bool? _FieldPlacement(ReadOnlySpan<byte> privateData, int height) {
    if (_QuickTimeFieldPlacement(privateData) is { } quickTime)
      return quickTime;

    if (_AvidExtraFieldPlacement(privateData) is { } avid)
      return avid;

    // AVI and VfW Matroska hand this package the complete BITMAPINFOHEADER before the codec bytes.
    if (privateData.Length >= _BITMAP_INFO_HEADER_SIZE) {
      var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(privateData);
      if (headerSize >= _BITMAP_INFO_HEADER_SIZE && headerSize <= privateData.Length
          && _AvidExtraFieldPlacement(privateData[(int)headerSize..]) is { } afterHeader)
        return afterHeader;
    }

    return height switch {
      486 => true,  // 525-line / NTSC D1: the lower field is stored first.
      576 => false, // 625-line / PAL D1: the upper field is stored first.
      _ => null,
    };
  }

  /// <summary>
  /// Reads the Video-for-Windows Avid Motion JPEG discriminator: a fixed <c>0x2C</c>/<c>0x18</c> pair
  /// and a television standard at byte twelve.
  /// </summary>
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

  /// <summary>
  /// Reads QuickTime's <c>fiel</c> image-description extension, whose second byte says which field
  /// is stored first and which is displayed first.
  /// </summary>
  /// <remarks>
  /// Weaving needs only the stored order, which is the half of Apple's four values this reads: 1
  /// (T stored first, T displayed first) and 9 (T stored first, B displayed first) put the first
  /// coded field on the top row; 6 (B/B) and 14 (B stored first, T displayed first) put it on the
  /// second. A field count that is not two describes a stream that is not two-field and says nothing
  /// about placement.
  /// </remarks>
  private static bool? _QuickTimeFieldPlacement(ReadOnlySpan<byte> data) {
    if (data.Length < _VISUAL_SAMPLE_ENTRY_HEADER)
      return null;

    for (var position = _VISUAL_SAMPLE_ENTRY_HEADER; position + 8 <= data.Length;) {
      var size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
      if (size < 8 || size > int.MaxValue || position > data.Length - (int)size)
        return null;

      if (!data.Slice(position + 4, 4).SequenceEqual("fiel"u8)) {
        position += (int)size;
        continue;
      }

      if (size < 10 || data[position + 8] != 2)
        return null;

      return data[position + 9] switch {
        1 or 9 => false,
        6 or 14 => true,
        _ => null,
      };
    }

    return null;
  }
}
