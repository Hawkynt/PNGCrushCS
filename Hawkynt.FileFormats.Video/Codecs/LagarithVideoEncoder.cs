using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Lagarith;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Lagarith (<c>LAGS</c>) losslessly: arithmetic-coded RGB24 by default, RGBA for a stream of
/// 32 bits a pixel, YUY2 for 16 and YV12 for 12; a picture of one colour as a solid frame.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no Lagarith encoder: each plane is predicted with the decoder's
/// own predictors run forwards and range-coded with a probability table of its own residues, through
/// the inverse of the decoder's range coder; a plane of one value is coded as solid, and one the coder
/// cannot shrink has its residues stored. The zero-run
/// escape is not used. Every picture comes back exactly, and FFmpeg's decoder is the oracle that reads
/// what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class LagarithVideoEncoder : IVideoCodecEncoder<LagarithVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("LAGS");

  private readonly MediaStreamInfo _stream;
  private readonly LagarithFrameType _type;

  private LagarithVideoEncoder(MediaStreamInfo requested) {
    this._type = requested.BitsPerPixel switch {
      32 => LagarithFrameType.ArithmeticRgba,
      16 => LagarithFrameType.ArithmeticYuy2,
      12 => LagarithFrameType.ArithmeticYv12,
      _ => LagarithFrameType.ArithmeticRgb24,
    };
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = this._type switch {
        LagarithFrameType.ArithmeticRgba => 32,
        LagarithFrameType.ArithmeticYuy2 => 16,
        LagarithFrameType.ArithmeticYv12 => 12,
        _ => 24,
      },
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => LagarithVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static LagarithVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Lagarith can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Lagarith needs a positive picture size; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"Lagarith geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    var data = this._type switch {
      LagarithFrameType.ArithmeticYv12 or LagarithFrameType.ArithmeticYuy2 => this._EncodeYuv(frame),
      _ => this._EncodeRgb(frame),
    };
    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }

  private byte[] _EncodeRgb(RawImage frame) {
    var alpha = this._type == LagarithFrameType.ArithmeticRgba;
    var channels = alpha ? 4 : 3;
    var pixels = alpha ? frame.ToRgba32() : frame.ToRgb24();
    int width = frame.Width, height = frame.Height, size = width * height;

    var solid = true;
    for (var i = 1; i < size && solid; ++i)
      for (var c = 0; c < channels; ++c)
        solid &= pixels[i * channels + c] == pixels[c];
    if (solid)
      return alpha
        ? [(byte)LagarithFrameType.SolidRgba, pixels[2], pixels[1], pixels[0], pixels[3]]
        : [(byte)LagarithFrameType.SolidColor, pixels[2], pixels[1], pixels[0]];

    // Bottom-up rows; red and blue as differences from green.
    var red = new byte[size];
    var green = new byte[size];
    var blue = new byte[size];
    var opacity = alpha ? new byte[size] : null;
    for (var row = 0; row < height; ++row) {
      var source = (height - 1 - row) * width * channels;
      for (var x = 0; x < width; ++x) {
        var at = source + x * channels;
        var g = pixels[at + 1];
        var i = row * width + x;
        green[i] = g;
        red[i] = (byte)(pixels[at] - g);
        blue[i] = (byte)(pixels[at + 2] - g);
        if (opacity != null)
          opacity[i] = pixels[at + 3];
      }
    }

    var planeBlue = LagarithPlaneCodec.Encode(blue, width, height, LagarithPrediction.Rgb);
    var planeGreen = LagarithPlaneCodec.Encode(green, width, height, LagarithPrediction.Rgb);
    var planeRed = LagarithPlaneCodec.Encode(red, width, height, LagarithPrediction.Rgb);
    var planeAlpha = opacity != null ? LagarithPlaneCodec.Encode(opacity, width, height, LagarithPrediction.Rgb) : [];

    var header = alpha ? 13 : 9;
    var output = new List<byte>(header + planeBlue.Length + planeGreen.Length + planeRed.Length + planeAlpha.Length);
    var offsetGu = header + planeBlue.Length;
    var offsetBv = offsetGu + planeGreen.Length;
    var offsetA = offsetBv + planeRed.Length;
    Span<byte> fields = stackalloc byte[13];
    fields[0] = (byte)this._type;
    BinaryPrimitives.WriteUInt32LittleEndian(fields[1..], (uint)offsetGu);
    BinaryPrimitives.WriteUInt32LittleEndian(fields[5..], (uint)offsetBv);
    if (alpha)
      BinaryPrimitives.WriteUInt32LittleEndian(fields[9..], (uint)offsetA);
    output.AddRange(fields[..header].ToArray());
    output.AddRange(planeBlue);
    output.AddRange(planeGreen);
    output.AddRange(planeRed);
    output.AddRange(planeAlpha);
    return output.ToArray();
  }

  private byte[] _EncodeYuv(RawImage frame) {
    var yv12 = this._type == LagarithFrameType.ArithmeticYv12;
    var format = yv12 ? PixelFormat.Yuv420P8 : PixelFormat.Yuv422P8;
    var yuv = frame.Format == format ? frame : FastRawImageConverter.Convert(frame, format);
    int width = frame.Width, height = frame.Height;
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = yv12 ? (height + 1) / 2 : height;

    var planeY = LagarithPlaneCodec.Encode(yuv.GetPlaneData(0), width, height,
      yv12 ? LagarithPrediction.Yv12 : LagarithPrediction.Yuy2Luma);
    var chroma = yv12 ? LagarithPrediction.Yv12 : LagarithPrediction.Yuy2Chroma;
    var planeU = LagarithPlaneCodec.Encode(yuv.GetPlaneData(1), chromaWidth, chromaHeight, chroma);
    var planeV = LagarithPlaneCodec.Encode(yuv.GetPlaneData(2), chromaWidth, chromaHeight, chroma);

    // YV12 puts V at the first offset and U at the second; YUY2 the other way round.
    var (first, second) = yv12 ? (planeV, planeU) : (planeU, planeV);
    var output = new List<byte>(9 + planeY.Length + first.Length + second.Length);
    Span<byte> fields = stackalloc byte[9];
    fields[0] = (byte)this._type;
    BinaryPrimitives.WriteUInt32LittleEndian(fields[1..], (uint)(9 + planeY.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(fields[5..], (uint)(9 + planeY.Length + first.Length));
    output.AddRange(fields.ToArray());
    output.AddRange(planeY);
    output.AddRange(first);
    output.AddRange(second);
    return output.ToArray();
  }
}
