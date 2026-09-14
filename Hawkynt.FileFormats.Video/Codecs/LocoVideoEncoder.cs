using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes LOCO lossless RGB, RGBA, YUV 4:2:2 and YUV 4:2:0 video frames.</summary>
/// <remarks>
/// LOCO has no published encoder implementation to reuse. This writer is the inverse of the decoder's
/// independently documented behaviour: the LOCO-I/JPEG-LS median-edge predictor, the adaptive Rice
/// parameter, and the codec's stateful zero-run subcode. FFmpeg's LGPL-2.1-or-later
/// <c>libavcodec/loco.c</c> is used as the external decoder oracle; no encoder code is copied from it.
/// <para/>
/// Version 1 is written, therefore every residual is exact and the near-lossless step is zero. RGB
/// and RGBA use independent B, G, R and optional A planes stored bottom-up. YUV 4:2:2 stores Y, U, V
/// planes and YUV 4:2:0 stores Y, V, U, matching the decoder's native planar modes without a colour
/// conversion. RGB pictures of odd width are refused because the historical RGB decoder applies a
/// non-invertible row-rotation compatibility transform to them. Every frame is independently coded
/// and therefore a key frame; LOCO has no inter-picture references.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class LocoVideoEncoder : IVideoCodecEncoder<LocoVideoEncoder> {

  private const int _CYUY2 = -1;
  private const int _CRGB = -2;
  private const int _CRGBA = -3;
  private const int _CYV12 = -4;
  private const int _YUY2 = 1;
  private const int _UYVY = 2;
  private const int _RGB = 3;
  private const int _RGBA = 4;
  private const int _YV12 = 5;
  private const int _VERSION = 1;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("LOCO");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _mode;

  private LocoVideoEncoder(MediaStreamInfo stream, int mode) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._mode = mode;

    var bitsPerPixel = _BitsPerPixel(mode);
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: (short)bitsPerPixel,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize + 12];
    header.WriteTo(format);
    var extra = format.AsSpan(BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(extra, _VERSION);
    BinaryPrimitives.WriteInt32LittleEndian(extra[4..], mode);
    BinaryPrimitives.WriteInt32LittleEndian(extra[8..], 0);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "LOCO";

  public static CodecTag Codec => _Tag;

  public static LocoVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("LOCO can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A LOCO encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new NotSupportedException($"A {stream.Width}x{stream.Height} picture is too large for a LOCO frame.");

    var mode = _SelectMode(stream);
    if (mode is _CYUY2 or _YUY2 or _UYVY && (stream.Width & 1) != 0)
      throw new NotSupportedException($"LOCO YUV 4:2:2 needs an even width; {stream.Width} was supplied.");
    if (mode is _CYV12 or _YV12 && ((stream.Width | stream.Height) & 1) != 0)
      throw new NotSupportedException(
        $"LOCO YUV 4:2:0 needs even dimensions; {stream.Width}x{stream.Height} was supplied.");
    if (mode is _CRGB or _RGB && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"LOCO RGB mode cannot faithfully encode odd-width pictures ({stream.Width} pixels): its historical decoder applies a non-invertible row rotation. Use RGBA32 or an even width.");

    return new(stream, mode);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    using var output = new MemoryStream();
    switch (this._mode) {
      case _CRGB:
      case _RGB:
        this._EncodeRgb(frame, withAlpha: false, output);
        break;
      case _CRGBA:
      case _RGBA:
        this._EncodeRgb(frame, withAlpha: true, output);
        break;
      case _CYUY2:
      case _YUY2:
      case _UYVY:
        this._EncodeYuv422(frame, output);
        break;
      case _CYV12:
      case _YV12:
        this._EncodeYuv420(frame, output);
        break;
      default:
        throw new InvalidDataException("The LOCO colour mode changed after encoder construction.");
    }

    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private void _EncodeRgb(RawImage frame, bool withAlpha, Stream output) {
    var target = withAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
    var picture = LosslessEncoderInput.Prepare(frame, target, this._width, this._height, CodecName);
    var components = withAlpha ? 4 : 3;

    // The bitstream is B, G, R, [A], while RawImage is R, G, B, [A]. RGB-family planes are coded
    // bottom-up, matching the negative strides used by the reference decoder.
    this._EncodePackedComponent(picture.PixelData, components, 2, output);
    this._EncodePackedComponent(picture.PixelData, components, 1, output);
    this._EncodePackedComponent(picture.PixelData, components, 0, output);
    if (withAlpha)
      this._EncodePackedComponent(picture.PixelData, components, 3, output);
  }

  private void _EncodeYuv422(RawImage frame, Stream output) {
    var picture = this._RequirePlanar(frame, PixelFormat.Yuv422P8, "YUV 4:2:2");
    var chromaWidth = this._width / 2;
    this._EncodePlane(picture.GetPlaneData(0), this._width, this._height, output);
    this._EncodePlane(picture.GetPlaneData(1), chromaWidth, this._height, output);
    this._EncodePlane(picture.GetPlaneData(2), chromaWidth, this._height, output);
  }

  private void _EncodeYuv420(RawImage frame, Stream output) {
    var picture = this._RequirePlanar(frame, PixelFormat.Yuv420P8, "YUV 4:2:0");
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    this._EncodePlane(picture.GetPlaneData(0), this._width, this._height, output);
    this._EncodePlane(picture.GetPlaneData(2), chromaWidth, chromaHeight, output); // YV12 stores V before U
    this._EncodePlane(picture.GetPlaneData(1), chromaWidth, chromaHeight, output);
  }

  private RawImage _RequirePlanar(RawImage frame, PixelFormat format, string layout) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"{CodecName} geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (frame.Format != format)
      throw new NotSupportedException(
        $"LOCO {layout} is lossless and requires {format} input; {frame.Format} would require a colour conversion, so it is refused rather than changed.");
    return frame;
  }

  private void _EncodePackedComponent(ReadOnlySpan<byte> pixels, int components, int component, Stream output) {
    var pixelCount = checked(this._width * this._height);
    var plane = new byte[pixelCount];

    for (var codedY = 0; codedY < this._height; ++codedY) {
      var sourceY = this._height - 1 - codedY;
      var sourceAt = sourceY * this._width * components + component;
      var planeAt = codedY * this._width;
      for (var x = 0; x < this._width; ++x, sourceAt += components)
        plane[planeAt + x] = pixels[sourceAt];
    }

    this._EncodePlane(plane, this._width, this._height, output);
  }

  private void _EncodePlane(ReadOnlySpan<byte> plane, int width, int height, Stream output) {
    var pixelCount = checked(width * height);
    if (plane.Length < pixelCount)
      throw new InvalidDataException("A LOCO source plane is shorter than its declared geometry.");

    var mapped = new byte[pixelCount];
    mapped[0] = _MapResidual(_SignedByteDelta(plane[0], 128));
    for (var x = 1; x < width; ++x)
      mapped[x] = _MapResidual(_SignedByteDelta(plane[x], plane[x - 1]));

    for (var y = 1; y < height; ++y) {
      var row = y * width;
      mapped[row] = _MapResidual(_SignedByteDelta(plane[row], plane[row - width]));
      for (var x = 1; x < width; ++x) {
        var left = plane[row + x - 1];
        var above = plane[row - width + x];
        var aboveLeft = plane[row - width + x - 1];
        var prediction = _Median(left, left + above - aboveLeft, above);
        mapped[row + x] = _MapResidual(_SignedByteDelta(plane[row + x], prediction));
      }
    }

    var bits = new MsbBitWriter(output);
    var rice = new RiceState();
    rice.Write(mapped, bits);
    bits.FinishByte();
  }

  private static int _SelectMode(MediaStreamInfo stream) {
    var format = stream.CodecPrivateData.Span;
    var offset = BitmapInfoHeader.StructSize;
    if (format.Length > offset && format.Length < offset + 12)
      throw new NotSupportedException(
        $"LOCO stream {stream.Index} carries a partial codec-private trailer; 12 bytes are required when one is supplied.");

    if (format.Length >= offset + 12) {
      var mode = BinaryPrimitives.ReadInt32LittleEndian(format[(offset + 4)..]);
      if (mode is _CYUY2 or _CRGB or _CRGBA or _CYV12 or _YUY2 or _UYVY or _RGB or _RGBA or _YV12)
        return mode;
      throw new NotSupportedException($"LOCO stream {stream.Index} requests unknown colour mode {mode}.");
    }

    return stream.BitsPerPixel switch {
      0 or 24 => _RGB,
      32 => _RGBA,
      16 => _YUY2,
      12 => _YV12,
      _ => throw new NotSupportedException(
        $"LOCO writes RGB24, RGBA32, YUV 4:2:2 or YUV 4:2:0; video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel."),
    };
  }

  private static int _BitsPerPixel(int mode) => mode switch {
    _CYUY2 or _YUY2 or _UYVY => 16,
    _CRGB or _RGB => 24,
    _CRGBA or _RGBA => 32,
    _CYV12 or _YV12 => 12,
    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
  };

  private static int _SignedByteDelta(int value, int prediction)
    => unchecked((sbyte)(byte)(value - prediction));

  private static byte _MapResidual(int residual)
    => checked((byte)(residual switch {
      > 0 => residual << 1,
      < 0 => ((-residual) << 1) - 1,
      _ => 0,
    }));

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    if (c < a)
      return a;
    if (c > b)
      return b;
    return c;
  }

  private sealed class RiceState {
    private int _save;
    private int _run2;
    private int _sum = 8;
    private int _count = 1;

    internal void Write(ReadOnlySpan<byte> mapped, MsbBitWriter bits) {
      for (var i = 0; i < mapped.Length;) {
        var encoded = mapped[i];
        bits.WriteUnsignedRice(encoded, this._Parameter());
        this._Update((encoded + 1) >> 1);

        if (encoded == 0) {
          if (this._save >= 0) {
            var run = 0;
            while (i + run + 1 < mapped.Length && mapped[i + run + 1] == 0)
              ++run;

            bits.WriteUnsignedRice(run, 2);
            if (run > 1)
              this._save += run + 1;
            else
              this._save -= 3;

            for (var implicitZero = 0; implicitZero < run; ++implicitZero)
              this._Update(0);

            i += run + 1;
            continue;
          }

          ++this._run2;
          ++i;
          continue;
        }

        if (this._run2 > 0) {
          if (this._run2 > 2)
            this._save += this._run2;
          else
            this._save -= 3;
          this._run2 = 0;
        }

        ++i;
      }
    }

    private int _Parameter() {
      var parameter = 0;
      var value = this._count;
      while (this._sum > value && parameter < 9) {
        value <<= 1;
        ++parameter;
      }
      return parameter;
    }

    private void _Update(int value) {
      this._sum += value;
      ++this._count;
      if (this._count == 16) {
        this._sum >>= 1;
        this._count >>= 1;
      }
    }
  }

  private sealed class MsbBitWriter(Stream output) {
    private int _current;
    private int _bits;

    internal void WriteUnsignedRice(int value, int parameter) {
      var quotient = value >> parameter;
      for (var i = 0; i < quotient; ++i)
        this._WriteBit(0);
      this._WriteBit(1);

      for (var bit = parameter - 1; bit >= 0; --bit)
        this._WriteBit((value >> bit) & 1);
    }

    internal void FinishByte() {
      if (this._bits == 0)
        return;

      output.WriteByte((byte)(this._current << (8 - this._bits)));
      this._current = 0;
      this._bits = 0;
    }

    private void _WriteBit(int bit) {
      this._current = (this._current << 1) | bit;
      if (++this._bits != 8)
        return;

      output.WriteByte((byte)this._current);
      this._current = 0;
      this._bits = 0;
    }
  }
}
