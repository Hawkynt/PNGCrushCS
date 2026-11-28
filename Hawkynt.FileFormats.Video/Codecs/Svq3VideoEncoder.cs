using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Svq3;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Sorenson Video 3 (<c>SVQ3</c>): an I picture every thirtieth, and P pictures between them
/// whose macroblocks are skipped, coded as a residual over the reference, or coded intra.
/// </summary>
/// <remarks>
/// The package's own encoder — FFmpeg has none — writing the syntax of the SVQ3 decoder beside it:
/// whole-sample vectors only, all of them nought, no B pictures, and the stream marked low delay in
/// the <c>SEQH</c> atom that follows the BITMAPINFOHEADER, so every picture comes straight back out.
/// Each macroblock takes whichever of the three costs least in squared error plus weighted bits.
/// Lossy by construction, at quantiser 18. FFmpeg's decoder is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Svq3VideoEncoder : IVideoCodecEncoder<Svq3VideoEncoder> {

  private const int _KEY_FRAME_INTERVAL = 30;
  private const int _QSCALE = 18;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("SVQ3");

  private readonly MediaStreamInfo _requested;
  private readonly Svq3Encoder _encoder;
  private readonly int _width;
  private readonly int _height;
  private int _sinceKeyFrame;
  private MediaStreamInfo? _stream;

  private Svq3VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._encoder = new(stream.Width, stream.Height, _QSCALE);
  }

  public static string CodecName => Svq3VideoDecoder.CodecName;

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static Svq3VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Sorenson Video 3 can only encode a video stream.");
    if (stream.Width < 16 || stream.Height < 16 || stream.Width > 4095 || stream.Height > 4095)
      throw new NotSupportedException($"SVQ3 pictures are 16 to 4095 samples a side; {stream.Width}x{stream.Height} was asked for.");
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"This SVQ3 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var yuv = frame.Format == PixelFormat.Yuv420P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8);
    var key = this._sinceKeyFrame == 0;
    var bytes = this._encoder.Encode(yuv.PixelData, key);
    this._sinceKeyFrame = (this._sinceKeyFrame + 1) % _KEY_FRAME_INTERVAL;
    packet = new(this._requested.Index, bytes, PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, IsKeyFrame: key);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    // The SEQH atom follows the BITMAPINFOHEADER, whose size counts it as FFmpeg's muxers count it;
    // FFmpeg finds the atom by searching the codec data for it.
    var sequence = Svq3Encoder.SequenceHeader(this._width, this._height);
    var format = new byte[BitmapInfoHeader.StructSize + sequence.Length];
    new BitmapInfoHeader(
      HeaderSize: format.Length, Width: this._width, Height: this._height, Planes: 1, BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value), ImageSize: 0, XPixelsPerMeter: 0, YPixelsPerMeter: 0, ColorsUsed: 0, ImportantColors: 0).WriteTo(format);
    sequence.CopyTo(format, BitmapInfoHeader.StructSize);
    return this._stream = new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = _Tag, Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC", TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate,
      Width = this._width, Height = this._height, BitsPerPixel = 24, CodecPrivateData = format,
    };
  }
}
