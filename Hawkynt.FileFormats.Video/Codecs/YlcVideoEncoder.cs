using System;
using System.IO;
using FileFormat.Codecs.Ylc;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes the YUY2 Lossless Codec (<c>YLC0</c>) losslessly from 4:2:2 YUV: every group of four
/// samples gradient-predicted, a run of unchanged groups skipped, small residues through the first
/// table and the rest as literal codes, with tables built from the picture's own counts.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no YLC encoder; the tables are built by the decoder's own tree
/// construction, so the codes are the ones it will derive. Every picture of 4:2:2 YUV comes back
/// exactly; a picture in another format is converted to 4:2:2 first. FFmpeg's decoder is the oracle
/// that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class YlcVideoEncoder : IVideoCodecEncoder<YlcVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("YLC0");

  private readonly MediaStreamInfo _stream;

  private YlcVideoEncoder(MediaStreamInfo requested) => this._stream = new() {
    Index = requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _Tag,
    Handler = _Tag,
    TimeBase = requested.TimeBase,
    FrameRate = requested.FrameRate,
    DeclaredFrameCount = requested.DeclaredFrameCount,
    Width = requested.Width,
    Height = requested.Height,
    BitsPerPixel = 16,
    Language = requested.Language,
    Name = requested.Name,
  };

  public static string CodecName => YlcVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static YlcVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("YLC can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0 || (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"YLC codes YUYV pairs, so its picture is a positive even number of samples wide; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"YLC geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    var yuv = frame.Format == PixelFormat.Yuv422P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P8);
    int width = frame.Width, height = frame.Height;
    var y = yuv.GetPlaneData(0);
    var u = yuv.GetPlaneData(1);
    var v = yuv.GetPlaneData(2);
    var packed = new byte[width * height * 2];
    for (var i = 0; i < width * height / 2; ++i) {
      packed[4 * i] = y[2 * i];
      packed[4 * i + 1] = u[i];
      packed[4 * i + 2] = y[2 * i + 1];
      packed[4 * i + 3] = v[i];
    }

    packet = new(this._stream.Index, YlcCodec.Encode(packed, width, height), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }
}
