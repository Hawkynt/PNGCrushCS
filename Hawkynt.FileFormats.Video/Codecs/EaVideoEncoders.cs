using System;
using System.IO;
using FileFormat.Codecs.Ea;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>What the four Electronic Arts encoders share: geometry, the stream they describe, and their pictures as 4:2:0.</summary>
public abstract class EaVideoEncoderBase {

  private protected const int KeyFrameInterval = 12;

  private protected readonly MediaStreamInfo Stream;
  private protected int PictureNumber;

  private protected EaVideoEncoderBase(MediaStreamInfo requested, CodecTag tag, int bitsPerPixel) => this.Stream = new() {
    Index = requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = tag,
    Handler = tag,
    TimeBase = requested.TimeBase.IsKnown ? requested.TimeBase : new Rational(1, 15),
    FrameRate = requested.FrameRate.IsKnown ? requested.FrameRate : new Rational(15, 1),
    DeclaredFrameCount = requested.DeclaredFrameCount,
    Width = requested.Width,
    Height = requested.Height,
    BitsPerPixel = bitsPerPixel,
    Language = requested.Language,
    Name = requested.Name,
  };

  public MediaStreamInfo DescribeStream() => this.Stream;

  private protected void CheckGeometry(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this.Stream.Width || frame.Height != this.Stream.Height)
      throw new InvalidDataException($"The geometry is fixed at {this.Stream.Width}x{this.Stream.Height}; received {frame.Width}x{frame.Height}.");
  }

  private protected EaSource Planes(RawImage frame) {
    var yuv = frame.Format == PixelFormat.Yuv420P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv420P8);
    return EaSource.From(yuv.GetPlaneData(0), yuv.GetPlaneData(1), yuv.GetPlaneData(2), frame.Width, frame.Height);
  }

  private protected CodedPacket Packet(byte[] data, long? timestamp, bool key)
    => new(this.Stream.Index, data, PresentationTimestamp: timestamp, DecodeTimestamp: timestamp, Duration: 1, IsKeyFrame: key);

  private protected static void CheckSize(MediaStreamInfo stream, int minimum, int multiple) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("An Electronic Arts video encoder can only encode a video stream.");
    if (stream.Width < minimum || stream.Height < minimum || stream.Width > 65535 || stream.Height > 65535
        || stream.Width % multiple != 0 || stream.Height % multiple != 0)
      throw new NotSupportedException(
        $"This codec needs a picture of at least {minimum} samples each way and a multiple of {multiple}; stream {stream.Index} states {stream.Width}x{stream.Height}.");
  }
}

/// <summary>
/// Encodes Electronic Arts TQI video: every picture MPEG-1 intra blocks under the quality-derived matrix.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no TQI encoder: the forward transform is a projection onto the
/// basis of EA's inverse DCT, at quality 90 of the format's 0-107. Lossy by construction. FFmpeg's decoder
/// is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaTqiVideoEncoder : EaVideoEncoderBase, IVideoCodecEncoder<EaTqiVideoEncoder> {

  private const int _QUALITY = 90;
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tqi ");

  private EaTqiVideoEncoder(MediaStreamInfo stream) : base(stream, _Tag, 12) { }

  public static string CodecName => EaTqiVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;
  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static EaTqiVideoEncoder Create(MediaStreamInfo stream) {
    CheckSize(stream, 1, 1);
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    this.CheckGeometry(frame);
    packet = this.Packet(TqiEncoder.Encode(this.Planes(frame), frame.Width, frame.Height, _QUALITY), presentationTimestamp, true);
    return true;
  }
}

/// <summary>
/// Encodes Electronic Arts TGQ video: macroblocks in TGQ's own coefficient code, a macroblock that has
/// not changed copied from the last picture.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no TGQ encoder; quality 90 of 0-100, a key frame every twelfth
/// picture, and the reference for copying is the decoder's reading of each packet. A macroblock too busy
/// for TGQ's one-byte length is written as its DC values alone. Lossy by construction. FFmpeg's decoder is
/// the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaTgqVideoEncoder : EaVideoEncoderBase, IVideoCodecEncoder<EaTgqVideoEncoder> {

  private const int _QUALITY = 90;
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tgq ");
  private readonly TgqEncoder _encoder;

  private EaTgqVideoEncoder(MediaStreamInfo stream) : base(stream, _Tag, 12) => this._encoder = new(stream.Width, stream.Height, _QUALITY);

  public static string CodecName => EaTgqVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;
  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static EaTgqVideoEncoder Create(MediaStreamInfo stream) {
    CheckSize(stream, 1, 1);
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    this.CheckGeometry(frame);
    var key = this.PictureNumber++ % KeyFrameInterval == 0;
    packet = this.Packet(this._encoder.Encode(this.Planes(frame), key), presentationTimestamp, key);
    return true;
  }
}

/// <summary>
/// Encodes Electronic Arts Madcow (MAD) video: key frames of intra blocks every twelfth picture, and
/// predicted pictures whose macroblocks are copied from the last one with a DC offset where that is close.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no MAD encoder; quantiser scale 4, below which the format's matrix
/// stays inside the sixteen bits FFmpeg reads it through. The reference is the decoder's reading of each
/// packet. Lossy by construction. FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaMadVideoEncoder : EaVideoEncoderBase, IVideoCodecEncoder<EaMadVideoEncoder> {

  private const int _SCALE = 4;
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("mad ");
  private readonly MadEncoder _encoder;

  private EaMadVideoEncoder(MediaStreamInfo stream) : base(stream, _Tag, 12) {
    var rate = this.Stream.FrameRate;
    var milliseconds = rate.Numerator > 0 ? (int)Math.Round(1000.0 * rate.Denominator / rate.Numerator) : 67;
    this._encoder = new(stream.Width, stream.Height, _SCALE, Math.Clamp(milliseconds, 1, 65535));
  }

  public static string CodecName => EaMadVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;
  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static EaMadVideoEncoder Create(MediaStreamInfo stream) {
    CheckSize(stream, 16, 1);
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    this.CheckGeometry(frame);
    var key = this.PictureNumber++ % KeyFrameInterval == 0;
    packet = this.Packet(this._encoder.Encode(this.Planes(frame), key), presentationTimestamp, key);
    return true;
  }
}

/// <summary>
/// Encodes Electronic Arts TGV video: palettised key frames packed by TGV's LZ77 variant every twelfth
/// picture, and predicted frames of four-by-four cells, an unchanged cell copied and the rest raw.
/// </summary>
/// <remarks>
/// This package's own, FFmpeg having no TGV encoder. An <see cref="PixelFormat.Indexed8"/> picture keeps
/// its own palette, a new one starting a key frame; any other picture is taken to a fixed palette of six
/// reds, seven greens and six blues. A picture already in its palette comes back exactly. FFmpeg's
/// decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class EaTgvVideoEncoder : EaVideoEncoderBase, IVideoCodecEncoder<EaTgvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tgv ");
  private static readonly byte[] _Cube = _BuildCube();
  private readonly TgvEncoder _encoder;
  private byte[]? _palette;

  private EaTgvVideoEncoder(MediaStreamInfo stream) : base(stream, _Tag, 8) => this._encoder = new(stream.Width, stream.Height);

  public static string CodecName => EaTgvVideoDecoder.CodecName;
  public static CodecTag Codec => _Tag;
  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(_Tag);

  public static EaTgvVideoEncoder Create(MediaStreamInfo stream) {
    CheckSize(stream, 4, 4);
    return new(stream);
  }

  private static byte[] _BuildCube() {
    var palette = new byte[256 * 3];
    var i = 0;
    for (var r = 0; r < 6; ++r)
      for (var g = 0; g < 7; ++g)
        for (var b = 0; b < 6; ++b, ++i) {
          palette[i * 3] = (byte)(r * 255 / 5);
          palette[i * 3 + 1] = (byte)(g * 255 / 6);
          palette[i * 3 + 2] = (byte)(b * 255 / 5);
        }
    return palette;
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    this.CheckGeometry(frame);
    byte[] indices, palette;
    if (frame.Format == PixelFormat.Indexed8 && frame.Palette is { Length: >= 3 } given) {
      indices = frame.PixelData;
      palette = new byte[256 * 3];
      given.AsSpan(0, Math.Min(given.Length, palette.Length)).CopyTo(palette);
    } else {
      var rgb = frame.ToRgb24();
      indices = new byte[frame.Width * frame.Height];
      for (var p = 0; p < indices.Length; ++p) {
        var r = (rgb[p * 3] * 5 + 127) / 255;
        var g = (rgb[p * 3 + 1] * 6 + 127) / 255;
        var b = (rgb[p * 3 + 2] * 5 + 127) / 255;
        indices[p] = (byte)((r * 7 + g) * 6 + b);
      }
      palette = _Cube;
    }

    var key = this.PictureNumber++ % KeyFrameInterval == 0 || this._palette == null || !palette.AsSpan().SequenceEqual(this._palette);
    this._palette = palette;
    packet = this.Packet(this._encoder.Encode(indices, palette, 256, key), presentationTimestamp, key);
    return true;
  }
}
