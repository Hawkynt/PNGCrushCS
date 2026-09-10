using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.DnxHd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Avid DNxHR, the resolution-independent profile of SMPTE VC-3, as progressive 8-bit 4:2:2
/// DNxHR SQ frames.
/// </summary>
/// <remarks>
/// Written from SMPTE ST 2019-1:2016 alongside <see cref="DnxHdVideoDecoder"/>. The encoder uses
/// compression ID 1273 (DNxHR SQ): header version 3, 4:2:2 Y′CbCr, eight bits per component, no alpha
/// and constant bitrate. Every packet is one independently decodable key frame.
/// <para/>
/// <b>Why DNxHR rather than inventing a DNxHD raster.</b> The HD compression IDs in Annex C fix
/// raster and compressed frame size together. The resolution-independent IDs are the standard's
/// mechanism for arbitrary dimensions, and <c>AVdh</c> is their container tag. This encoder therefore
/// writes a real RI profile instead of putting a DNxHD identifier around a raster that identifier
/// cannot describe.
/// <para/>
/// <b>Rate control.</b> Section 7.1 fixes the CBR frame size from the number of macroblocks. The
/// coding unit starts at qscale 4 and raises that scale by powers of two only when the encoded
/// macroblock data does not fit; the remainder of the compressed payload is zero padding and the
/// standard's non-CRC EOF signature closes the frame.
/// <para/>
/// <b>Colour.</b> Coding Control B states BT.709 and the input is converted to limited-range
/// <see cref="PixelFormat.Yuv422P8"/> with that matrix before the DCT. A caller already supplying
/// YUV422P8 is taken literally, as elsewhere in the video encoders: its samples are the samples to
/// encode.
/// <para/>
/// <b>What it does not write.</b> DNxHD's fixed HD compression IDs, 10/12-bit HQX, 4:4:4/RGB, 4:2:0,
/// alpha and interlaced coding are not approximated under the wrong flags; this encoder has one
/// deliberately narrow, interoperable output profile.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DnxHdVideoEncoder : IVideoCodecEncoder<DnxHdVideoEncoder> {

  private const int _MAX_RASTER = 16384;
  private static readonly CodecTag _Codec = CodecTag.FromCharacters("AVdh");

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _paddedWidth;
  private readonly int _paddedHeight;
  private MediaStreamInfo? _stream;

  private DnxHdVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._paddedWidth = this._macroblockWidth * 16;
    this._paddedHeight = this._macroblockHeight * 16;
  }

  public static string CodecName => "Avid DNxHD / DNxHR (SMPTE VC-3)";

  /// <summary>The resolution-independent DNxHR four-character code.</summary>
  public static CodecTag Codec => _Codec;

  public static DnxHdVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Avid DNx can only encode a video stream.");

    if (stream.Width is < 1 or > _MAX_RASTER || stream.Height is < 1 or > _MAX_RASTER)
      throw new NotSupportedException(
        $"SMPTE ST 2019-1 bounds a resolution-independent raster to 1…{_MAX_RASTER} samples and lines; "
        + $"{stream.Width}x{stream.Height} was supplied.");

    if (stream.Codec != CodecTag.None && !stream.Codec.EqualsIgnoringCase(_Codec))
      throw new NotSupportedException(
        $"This encoder writes the resolution-independent DNxHR profile tagged 'AVdh'; '{stream.Codec}' was requested.");

    return new(stream);
  }

  /// <summary>Codes one whole intra picture.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This DNxHR stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._requested.Index,
      this.EncodeFrame(frame),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  /// <summary>Nothing is buffered: VC-3 frames are independent coding units.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Codec,
    Handler = _Codec,
    CodecId = "V_DNXHD",
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 16,
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  /// <summary>Encodes one picture into one DNxHR SQ coding unit.</summary>
  internal byte[] EncodeFrame(RawImage frame) {
    var source = frame.Format == PixelFormat.Yuv422P8
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P8, RawImageColorInfo.Bt709Limited);

    if (!source.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {PixelFormat.Yuv422P8} produced too few bytes for {this._width}x{this._height}.");

    return DnxHdCodingUnitEncoder.Encode(this._ToPlanes(source), this._width, this._height);
  }

  /// <summary>Pads the three source planes to whole 16x16 luma macroblocks by extending their edges.</summary>
  private DnxHdPlanes _ToPlanes(RawImage source) {
    var planes = DnxHdPlanes.Allocate(
      this._paddedWidth,
      this._paddedHeight,
      chromaShift: 1,
      bitDepth: 8);

    var chromaWidth = (this._width + 1) / 2;
    _Fill(source.GetPlaneData(0), this._width, this._height, planes.Luma, this._paddedWidth, this._paddedHeight);
    _Fill(source.GetPlaneData(1), chromaWidth, this._height, planes.Cb, planes.ChromaWidth, this._paddedHeight);
    _Fill(source.GetPlaneData(2), chromaWidth, this._height, planes.Cr, planes.ChromaWidth, this._paddedHeight);

    return planes;
  }

  private static void _Fill(
    ReadOnlySpan<byte> source, int width, int height, ushort[] target, int paddedWidth, int paddedHeight) {

    if (source.Length < checked(width * height))
      throw new InvalidDataException(
        $"A planar eight-bit DNxHR source plane needs {width * height} bytes and carries only {source.Length}.");

    for (var y = 0; y < height; ++y) {
      var from = y * width;
      var into = y * paddedWidth;
      var last = (ushort)source[from + width - 1];

      for (var x = 0; x < width; ++x)
        target[into + x] = source[from + x];

      for (var x = width; x < paddedWidth; ++x)
        target[into + x] = last;
    }

    for (var y = height; y < paddedHeight; ++y)
      Array.Copy(target, (height - 1) * paddedWidth, target, y * paddedWidth, paddedWidth);
  }
}
