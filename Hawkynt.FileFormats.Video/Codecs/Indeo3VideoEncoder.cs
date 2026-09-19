using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Intel Indeo 3 <c>IV32</c> intra and forward-predicted pictures.</summary>
/// <remarks>
/// Indeo 3 uses two internal reference buffers rather than MPEG-style past/future picture lists. This
/// writer alternates those buffers: one frame in every twelve is a key/intra picture and the eleven
/// between are inter pictures predicted from the immediately preceding reconstruction. The periodic
/// key pictures and the preceding picture carry the format's periodic/next-key signalling as well.
/// There is no display-order reordering and therefore no B-picture analogue to emit.
/// <para/>
/// Inter planes use the format's ordinary motion-compensated cell grammar with vector <c>0,0</c> and
/// mode 0 residuals. Unchanged blocks are copied with the codec's run escapes, while a wholly unchanged
/// plane becomes one VQ-tree copy leaf. The encoder deliberately predicts from its own reconstruction,
/// not the source, because the seven-bit YUV 4:1:0/VQ path is lossy and source-side prediction would
/// accumulate drift.
/// <para/>
/// Eight-bit sample mode, half-sample motion vectors and the obscure VQ-tree SkipCell procedure remain
/// outside the writer: no known reference encoder sample uses the first two, and even FFmpeg's Indeo 3
/// decoder marks SkipCell as unimplemented. The normal key/inter/reference path is interoperable and
/// is checked by the decoder beside it plus FFmpeg as an independent oracle.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo3VideoEncoder : IVideoCodecEncoder<Indeo3VideoEncoder> {

  private static readonly CodecTag _IV32 = CodecTag.FromCharacters("IV32");

  private const int _MIN_DIMENSION = 16;
  private const int _MAX_WIDTH = 640;
  private const int _MAX_HEIGHT = 480;
  private const int _GROUP_SIZE = 12;

  private readonly MediaStreamInfo _stream;
  private readonly Indeo3FrameEncoder _frameEncoder;
  private uint _frameNumber;
  private int _groupPosition;

  private Indeo3VideoEncoder(MediaStreamInfo stream) {
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_IV32.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _IV32,
      Handler = _IV32,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
    this._frameEncoder = new(stream.Width, stream.Height);
  }

  public static string CodecName => "Intel Indeo 3";

  public static CodecTag Codec => _IV32;

  /// <summary>Creates an IV32 writer for a picture size the format can divide into 4x4 cells.</summary>
  public static Indeo3VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo 3 can only encode a video stream.");
    if (stream.Width < _MIN_DIMENSION || stream.Width > _MAX_WIDTH ||
        stream.Height < _MIN_DIMENSION || stream.Height > _MAX_HEIGHT ||
        (stream.Width & 3) != 0 || (stream.Height & 3) != 0)
      throw new NotSupportedException(
        $"Intel Indeo 3 writes pictures from 16x16 through 640x480 in whole 4x4 cells; "
        + $"{stream.Width}x{stream.Height} was requested.");

    return new(stream);
  }

  /// <summary>Encodes one picture immediately; no display-order reordering is required by Indeo 3.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The Intel Indeo 3 encoder was created for {this._stream.Width}x{this._stream.Height} pictures, "
        + $"but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes "
        + $"and carries {frame.PixelData.Length}.");

    var rgb = frame.Format == PixelFormat.Rgb24 ? frame.PixelData : frame.ToRgb24();
    var encoded = this._frameEncoder.Encode(
      rgb,
      this._frameNumber,
      periodicKeyFrame: this._groupPosition == 0,
      nextFrameIsKeyFrame: this._groupPosition == _GROUP_SIZE - 1);
    this._frameNumber = unchecked(this._frameNumber + 1);
    this._groupPosition = (this._groupPosition + 1) % _GROUP_SIZE;

    packet = new(
      StreamIndex: this._stream.Index,
      Data: encoded.Data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: encoded.IsKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
