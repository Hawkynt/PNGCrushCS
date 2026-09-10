using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Avid AVRn, whose <c>AVRn</c> four-character code names two historical payloads: Motion
/// JPEG and Avid's lossless Resolution 1:1 UYVY packing.
/// </summary>
/// <remarks>
/// The distinction is carried by the Video-for-Windows codec data, not by the four-character code.
/// FFmpeg's AVI demuxer makes the same split: an <c>AVRn</c> stream whose codec extra data has
/// <c>1:1</c> at byte 28 remains AVRn, while every other <c>AVRn</c> stream is handed to its Motion
/// JPEG decoder. AVI and Matroska/VFW hand this package the complete <c>BITMAPINFOHEADER</c> plus those
/// extra bytes, so this decoder peels off the forty-byte base header before applying that test.
/// <para/>
/// <b>Resolution 1:1.</b> The coded picture is uncompressed UYVY 4:2:2 — Cb, Y0, Cr, Y1 — and is
/// therefore lossless. A progressive packet may carry whole unused rows ahead of the picture; its
/// byte length divided by the two-byte pixel stride states the coded height, and the final
/// container-height rows are the picture. Interlaced packets store the two fields consecutively with
/// a four-byte separator and use the codec data to say which output row parity receives the first
/// field. Those are the rules used by FFmpeg's <c>avrndec.c</c>, expressed here through the package's
/// shared <see cref="PackedYuv422Packing"/> rather than through a second UYVY implementation.
/// <para/>
/// <b>The older subtype is ordinary Motion JPEG.</b> It is delegated to
/// <see cref="MotionJpegDecoder"/> unchanged. In particular, the JPEG's own dimensions win there;
/// cropping it to the AVI header is wrong for this subtype and was the defect in the previous AVRn
/// implementation.
/// <para/>
/// The implementation was derived clean-room from the observable stream rules in FFmpeg's
/// LGPL-2.1-or-later AVRn decoder and AVI demuxer; no implementation code is reproduced here.
/// </remarks>
public sealed class AvrnVideoDecoder : IVideoCodecDecoder<AvrnVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVRn");

  private const int _BITMAP_INFO_HEADER_SIZE = 40;
  private const int _ONE_TO_ONE_MARKER_OFFSET = 28;
  private const int _INTERLACE_OFFSET_BIAS = 4;
  private const int _FIELD_ORDER_OFFSET = 24;

  private readonly MotionJpegDecoder? _motionJpeg;
  private readonly PackedYuv422Packing? _packing;
  private readonly int _streamIndex;
  private readonly int _width;
  private readonly int _height;
  private readonly int _rowStride;
  private readonly int _frameBytes;
  private readonly bool _interlaced;
  private readonly bool _firstFieldOnOddRows;

  private AvrnVideoDecoder(MotionJpegDecoder motionJpeg) => this._motionJpeg = motionJpeg;

  private AvrnVideoDecoder(MediaStreamInfo stream, ReadOnlySpan<byte> extraData) {
    this._streamIndex = stream.Index;
    this._width = stream.Width;
    this._height = stream.Height;

    try {
      this._rowStride = checked(stream.Width * 2);
      this._frameBytes = checked(this._rowStride * stream.Height);
    } catch (OverflowException exception) {
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, whose AVRn frame size does not fit in memory.",
        exception);
    }

    this._packing = PackedYuv422Packing.For(stream, PackedYuv422Order.CbLumaCrLuma, "AVRn Resolution 1:1");

    if (extraData.Length < 9)
      return;

    var descriptorOffset = extraData[4] + _INTERLACE_OFFSET_BIAS;
    if (descriptorOffset + _FIELD_ORDER_OFFSET >= extraData.Length
        || !extraData.Slice(descriptorOffset, 4).SequenceEqual("1:1("u8))
      return;

    this._interlaced = true;
    this._firstFieldOnOddRows = extraData[descriptorOffset + _FIELD_ORDER_OFFSET] == 1;

    if ((stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} is an interlaced AVRn Resolution 1:1 stream with odd height {stream.Height}; "
        + "its two fields cannot contribute the same number of rows.");
  }

  public static string CodecName => "Avid AVRn";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AvrnVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var extraData = _AvidExtraData(stream.CodecPrivateData.Span);
    return _IsOneToOne(extraData)
      ? new(stream, extraData)
      : new(MotionJpegDecoder.Create(stream));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (this._motionJpeg != null)
      return this._motionJpeg.TryDecode(packet, out frame);

    var data = packet.Data.Span;
    this._ValidateRawPacket(data);

    var packed = this._interlaced
      ? this._Deinterlace(data)
      : this._ProgressivePicture(data).ToArray();

    frame = this._packing!.ToImage(this._packing.Unpack(packed));
    return true;
  }

  /// <summary>
  /// Returns FFmpeg-style AVRn codec extra data whether the caller supplied those bytes alone or the
  /// complete Video-for-Windows <c>BITMAPINFOHEADER</c> that AVI and Matroska carry.
  /// </summary>
  private static ReadOnlySpan<byte> _AvidExtraData(ReadOnlySpan<byte> privateData) {
    if (privateData.Length < _BITMAP_INFO_HEADER_SIZE)
      return privateData;

    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(privateData);
    return headerSize >= _BITMAP_INFO_HEADER_SIZE && headerSize <= privateData.Length
      ? privateData[_BITMAP_INFO_HEADER_SIZE..]
      : privateData;
  }

  private static bool _IsOneToOne(ReadOnlySpan<byte> extraData)
    => extraData.Length >= _ONE_TO_ONE_MARKER_OFFSET + 3
       && extraData.Slice(_ONE_TO_ONE_MARKER_OFFSET, 3).SequenceEqual("1:1"u8);

  private void _ValidateRawPacket(ReadOnlySpan<byte> data) {
    if (data.Length >= this._frameBytes)
      return;

    throw new InvalidDataException(
      $"Video stream {this._streamIndex} carries an AVRn Resolution 1:1 packet of {data.Length} byte(s), where a "
      + $"{this._width}x{this._height} UYVY frame needs at least {this._frameBytes}.");
  }

  /// <summary>
  /// A progressive packet may prefix whole coded rows. Integer division deliberately ignores a
  /// trailing partial row, matching the way the reference decoder derives its coded height.
  /// </summary>
  private ReadOnlySpan<byte> _ProgressivePicture(ReadOnlySpan<byte> data) {
    var codedHeight = data.Length / this._rowStride;
    var skippedRows = codedHeight - this._height;
    var start = checked(skippedRows * this._rowStride);
    return data.Slice(start, this._frameBytes);
  }

  /// <summary>
  /// Interlaced Resolution 1:1 stores the first field, four separator bytes, then the second field.
  /// The field-order byte controls output row parity exactly as the reference decoder interprets it.
  /// </summary>
  private byte[] _Deinterlace(ReadOnlySpan<byte> data) {
    var codedHeight = data.Length / this._rowStride;
    var firstFieldStart = (long)(codedHeight - this._height) * this._width;
    var secondFieldDelta = (long)this._width * codedHeight + 4;
    var fieldRows = this._height / 2;
    var lastFirstFieldRow = firstFieldStart + (long)(fieldRows - 1) * this._rowStride;
    var required = lastFirstFieldRow + secondFieldDelta + this._rowStride;

    if (required > data.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an interlaced AVRn Resolution 1:1 packet of {data.Length} byte(s), "
        + $"but its two {this._width}x{this._height / 2} fields and four-byte separator require at least {required}.");

    var packed = new byte[this._frameBytes];
    var firstParity = this._firstFieldOnOddRows ? 1 : 0;
    var secondParity = 1 - firstParity;

    for (var fieldRow = 0; fieldRow < fieldRows; ++fieldRow) {
      var firstSource = checked((int)(firstFieldStart + (long)fieldRow * this._rowStride));
      var secondSource = checked((int)(firstSource + secondFieldDelta));
      var outputPair = fieldRow * 2;

      data.Slice(firstSource, this._rowStride)
        .CopyTo(packed.AsSpan((outputPair + firstParity) * this._rowStride, this._rowStride));
      data.Slice(secondSource, this._rowStride)
        .CopyTo(packed.AsSpan((outputPair + secondParity) * this._rowStride, this._rowStride));
    }

    return packed;
  }
}
