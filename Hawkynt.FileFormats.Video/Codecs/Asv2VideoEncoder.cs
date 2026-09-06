using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Asv;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes ASUS V2, ASUSTeK's successor to ASV1 for the same TV tuner cards, from the same document
/// <see cref="Asv2VideoDecoder"/> reads: Michael Niedermayer's "ASUS V1/V2 Codecs" (asv1.txt,
/// 2003-2016, GNU FDL/GPL).
/// </summary>
/// <remarks>
/// <b>What it writes.</b> Every picture whole and on its own — the format has no prediction between
/// pictures of any kind, so every packet is a key frame and there is nothing to hold back. One
/// quantisation parameter for the whole stream, carried once in the codec-private data behind the
/// <c>BITMAPINFOHEADER</c>, followed by the four bytes <c>ASUS</c> the reference encoder writes
/// there. A macroblock is four luma blocks and one block each of Cb and Cr, and a picture that is not
/// a whole number of macroblocks is coded as clause 3.1 orders it.
/// <para/>
/// <b>What it reaches that ASV1 cannot.</b> All sixty-four positions of a block. ASV2 states up front
/// how many coefficient groups a block holds instead of ending on an End Of Block code, so it can
/// carry the six highest-frequency groups ASV1's coding cannot state at all. That is the whole of the
/// difference in what the two encoders can express; everything else between them is storage order and
/// which tables the coefficients are coded with.
/// <para/>
/// <b>Lossy, and where the loss comes from.</b> The transform and the quantiser, and nothing else —
/// unlike ASV1 there are no positions the coding cannot reach. There is no rate control: the format
/// has nowhere to put a second quantiser, so a fixed one is the whole of the decision and is what
/// makes the same input produce the same bytes.
/// <para/>
/// <b>Measured.</b> See this codec's section of <c>README.md</c>: streams written here, decoded by
/// ffmpeg's own ASV2 decoder and compared against this package's decode of the same bytes plane by
/// plane, on the 4:2:0 samples rather than in RGB.
/// </remarks>
public sealed class Asv2VideoEncoder : IVideoCodecEncoder<Asv2VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ASV2");

  /// <summary>How many bytes of codec-private data ASV2's own global header takes, asv1.txt 4.2.</summary>
  private const int _GLOBAL_HEADER_SIZE = 8;

  /// <summary>The factor clause 3.6 scales the intra matrix by before dividing by the quantiser.</summary>
  private const int _DEQUANTISATION_SCALE = 128;

  /// <summary>
  /// The quantisation parameter every stream is written at.
  /// </summary>
  /// <remarks>
  /// Sixteen, which puts the step at <c>128 * q[i] / 16</c> — the same effective step ASV1's default
  /// of eight gives against its own scale of sixty-four, so the two encoders code a picture at the
  /// same fineness and the difference between their outputs is the coding rather than the quality.
  /// </remarks>
  private const int _QUANTISER = 16;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int[] _dequantFactors;

  private MediaStreamInfo? _stream;

  private Asv2VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;
    this._dequantFactors = AsvQuantiser.DequantFactors(_DEQUANTISATION_SCALE, _QUANTISER);
  }

  public static string CodecName => "ASUS V2";

  public static CodecTag Codec => _Tag;

  public static Asv2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("ASUS V2 can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An ASUS V2 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied. The "
        + "bitstream states it nowhere, so the container's is the only one there is.");

    return new(stream);
  }

  /// <summary>Codes one picture, whole and on its own.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This ASUS V2 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived. "
        + "The bitstream states no picture size, so a stream cannot change one.");

    var source = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    AsvColorConversion.FromRgb24(frame.ToRgb24(), source, this._width, this._height);

    var bytes = AsvPictureEncoder.Encode(AsvVersion.Version2, source, this._width, this._height, this._dequantFactors);

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);

    return true;
  }

  /// <summary>Nothing is ever held back, so there is nothing left at the end.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> naming ASV2, and behind it the
  /// eight-byte global header carrying the one quantisation parameter the whole file is coded at.
  /// </summary>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize + _GLOBAL_HEADER_SIZE];
    header.WriteTo(format);
    format[BitmapInfoHeader.StructSize] = _QUANTISER;
    "ASUS"u8.CopyTo(format.AsSpan(BitmapInfoHeader.StructSize + 4));

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
    };
  }
}
