using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Intel Indeo 3 as independently decodable <c>IV32</c> intra frames.</summary>
/// <remarks>
/// Indeo 3 itself has inter prediction, a recursive cell tree, several coding modes and twenty-four
/// quantisation tables. The writer starts with the conservative subset: one intra cell per plane,
/// mode 0 and the finest table. Every packet can therefore be decoded without any packet before it.
/// That costs bytes, but it is the useful direction to be incomplete in: the packet is ordinary
/// Indeo 3 rather than a private dialect that only this package understands.
/// <para/>
/// Input is converted to the codec's YUV 4:1:0 sampling. The seven-bit plane samples are selected by
/// solving against the decoder's actual packed-delta arithmetic, so the output is deterministic and
/// the quantisation error is the format's error, not a disagreement between encoder and decoder math.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo3VideoEncoder : IVideoCodecEncoder<Indeo3VideoEncoder> {

  private static readonly CodecTag _IV32 = CodecTag.FromCharacters("IV32");

  private const int _MIN_DIMENSION = 16;
  private const int _MAX_WIDTH = 640;
  private const int _MAX_HEIGHT = 480;

  private readonly MediaStreamInfo _stream;
  private uint _frameNumber;

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

  /// <summary>Encodes one picture immediately; the encoder buffers no frames.</summary>
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
    var data = Indeo3FrameEncoder.Encode(rgb, frame.Width, frame.Height, this._frameNumber);
    this._frameNumber = unchecked(this._frameNumber + 1);

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
