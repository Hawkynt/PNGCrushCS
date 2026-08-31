using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes the LOCO lossless and near-lossless video codec.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/loco.c</c>, copyright (c) 2005 Konstantin Shishkov,
/// and the JPEG-LS unsigned Rice helper in <c>libavcodec/golomb.h</c>; both are distributed there
/// under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// LOCO stores one independently Rice-coded predictive plane after another. The Rice parameter adapts
/// from recent residual magnitudes, zeroes have their own run state, and all non-first-row samples use
/// the LOCO-I/JPEG-LS median-edge predictor. AVI carries a 12-byte trailer after the ordinary
/// <c>BITMAPINFOHEADER</c>: version, colour mode, and the near-lossless step.
/// </remarks>
public sealed class LocoVideoDecoder : IVideoCodecDecoder<LocoVideoDecoder> {

  private const int _CYUY2 = -1;
  private const int _CRGB = -2;
  private const int _CRGBA = -3;
  private const int _CYV12 = -4;
  private const int _YUY2 = 1;
  private const int _UYVY = 2;
  private const int _RGB = 3;
  private const int _RGBA = 4;
  private const int _YV12 = 5;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("LOCO");

  private readonly int _width;
  private readonly int _height;
  private readonly int _mode;
  private readonly int _lossy;
  private readonly int _streamIndex;

  private LocoVideoDecoder(int width, int height, int mode, int lossy, int streamIndex) {
    this._width = width;
    this._height = height;
    this._mode = mode;
    this._lossy = lossy;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "LOCO";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static LocoVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"LOCO stream {stream.Index} states an invalid {stream.Width}x{stream.Height} picture.");

    var format = stream.CodecPrivateData.Span;
    var offset = BitmapInfoHeader.StructSize;
    if (format.Length < offset + 12)
      throw new InvalidDataException(
        $"LOCO stream {stream.Index} carries {Math.Max(0, format.Length - offset)} codec-private byte(s), where 12 are required.");

    var extra = format[offset..];
    var version = BinaryPrimitives.ReadInt32LittleEndian(extra);
    var mode = BinaryPrimitives.ReadInt32LittleEndian(extra[4..]);
    var lossy = BinaryPrimitives.ReadInt32LittleEndian(extra[8..]);
    if (lossy < 0 || lossy > 65536)
      throw new InvalidDataException($"LOCO stream {stream.Index} states an invalid near-lossless step of {lossy}.");

    if (version is not (1 or 2)) {
      // FFmpeg accepts later values by using the same trailer fields. Keep that forward-compatible
      // behaviour, but only after validating the mode and lossy value below.
    }

    if (mode is not (_CYUY2 or _CRGB or _CRGBA or _CYV12 or _YUY2 or _UYVY or _RGB or _RGBA or _YV12))
      throw new NotSupportedException($"LOCO stream {stream.Index} uses unknown colour mode {mode}.");

    if (mode is _CYUY2 or _YUY2 or _UYVY && (stream.Width & 1) != 0)
      throw new NotSupportedException($"LOCO stream {stream.Index} has odd-width 4:2:2 chroma ({stream.Width} pixels).");
    if (mode is _CYV12 or _YV12 && ((stream.Width | stream.Height) & 1) != 0)
      throw new NotSupportedException(
        $"LOCO stream {stream.Index} has an odd-sized {stream.Width}x{stream.Height} 4:2:0 picture.");
    if (mode is _CRGB or _RGB or _CRGBA or _RGBA && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"LOCO stream {stream.Index} has odd-width RGB packing. The historical encoder's diagonal row-rotation quirk is not enabled without a native sample oracle.");

    return new(stream.Width, stream.Height, mode, version == 1 ? 0 : lossy, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    if (source.IsEmpty)
      throw new InvalidDataException($"LOCO stream {this._streamIndex} supplied an empty coded frame.");

    frame = this._mode switch {
      _CYUY2 or _YUY2 or _UYVY => this._DecodeYuv422(source),
      _CYV12 or _YV12 => this._DecodeYuv420(source),
      _CRGB or _RGB => this._DecodeRgb(source, withAlpha: false),
      _CRGBA or _RGBA => this._DecodeRgb(source, withAlpha: true),
      _ => throw new InvalidDataException("The LOCO colour mode changed after decoder construction."),
    };
    return true;
  }

  private RawImage _DecodeRgb(ReadOnlySpan<byte> source, bool withAlpha) {
    var pixels = checked(this._width * this._height);
    var blue = new byte[pixels];
    var green = new byte[pixels];
    var red = new byte[pixels];
    byte[]? alpha = withAlpha ? new byte[pixels] : null;

    var at = 0;
    at += this._DecodePlane(source[at..], blue, this._width, this._height);
    at += this._DecodePlane(source[at..], green, this._width, this._height);
    at += this._DecodePlane(source[at..], red, this._width, this._height);
    if (alpha != null)
      at += this._DecodePlane(source[at..], alpha, this._width, this._height);

    var stride = this._width * (withAlpha ? 4 : 3);
    var output = new byte[checked(stride * this._height)];
    for (var y = 0; y < this._height; ++y) {
      var codedRow = this._height - 1 - y;
      var planeAt = codedRow * this._width;
      var outAt = y * stride;
      for (var x = 0; x < this._width; ++x) {
        var p = planeAt + x;
        output[outAt++] = red[p];
        output[outAt++] = green[p];
        output[outAt++] = blue[p];
        if (alpha != null)
          output[outAt++] = alpha[p];
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = withAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = output,
    };
  }

  private RawImage _DecodeYuv422(ReadOnlySpan<byte> source) {
    var y = new byte[checked(this._width * this._height)];
    var chromaWidth = this._width / 2;
    var u = new byte[checked(chromaWidth * this._height)];
    var v = new byte[u.Length];

    var at = 0;
    at += this._DecodePlane(source[at..], y, this._width, this._height);
    at += this._DecodePlane(source[at..], u, chromaWidth, this._height);
    this._DecodePlane(source[at..], v, chromaWidth, this._height);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _YuvToRgb(y, u, v, this._width, this._height, chromaWidth, verticalSubsample: 1),
    };
  }

  private RawImage _DecodeYuv420(ReadOnlySpan<byte> source) {
    var y = new byte[checked(this._width * this._height)];
    var chromaWidth = this._width / 2;
    var chromaHeight = this._height / 2;
    var u = new byte[checked(chromaWidth * chromaHeight)];
    var v = new byte[u.Length];

    var at = 0;
    at += this._DecodePlane(source[at..], y, this._width, this._height);
    at += this._DecodePlane(source[at..], v, chromaWidth, chromaHeight); // YV12 stores V before U
    this._DecodePlane(source[at..], u, chromaWidth, chromaHeight);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _YuvToRgb(y, u, v, this._width, this._height, chromaWidth, verticalSubsample: 2),
    };
  }

  private int _DecodePlane(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height) {
    if (width == 0 || height == 0)
      return 0;
    if (source.IsEmpty)
      throw new InvalidDataException("A LOCO plane has no entropy data.");

    var bits = new MsbBitReader(source);
    var rice = new RiceState(this._lossy);

    destination[0] = unchecked((byte)(128 + rice.Read(ref bits)));
    for (var x = 1; x < width; ++x)
      destination[x] = unchecked((byte)(destination[x - 1] + rice.Read(ref bits)));

    for (var y = 1; y < height; ++y) {
      var row = y * width;
      destination[row] = unchecked((byte)(destination[row - width] + rice.Read(ref bits)));
      for (var x = 1; x < width; ++x) {
        var left = destination[row + x - 1];
        var above = destination[row - width + x];
        var aboveLeft = destination[row - width + x - 1];
        var prediction = _Median(left, left + above - aboveLeft, above);
        destination[row + x] = unchecked((byte)(prediction + rice.Read(ref bits)));
      }
    }

    return (bits.Position + 7) >> 3;
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    if (c < a)
      return a;
    if (c > b)
      return b;
    return c;
  }

  private static byte[] _YuvToRgb(
    ReadOnlySpan<byte> y,
    ReadOnlySpan<byte> u,
    ReadOnlySpan<byte> v,
    int width,
    int height,
    int chromaWidth,
    int verticalSubsample
  ) {
    var output = new byte[checked(width * height * 3)];
    var at = 0;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column) {
        var yy = y[row * width + column];
        var chromaAt = (row / verticalSubsample) * chromaWidth + (column >> 1);
        var cb = u[chromaAt];
        var cr = v[chromaAt];
        var c = yy - 16;
        var d = cb - 128;
        var e = cr - 128;
        output[at++] = _Clamp((298 * c + 409 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        output[at++] = _Clamp((298 * c + 516 * d + 128) >> 8);
      }
    return output;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private sealed class RiceState {
    private int _save;
    private int _run;
    private int _run2;
    private int _sum = 8;
    private int _count = 1;
    private readonly int _lossy;

    internal RiceState(int lossy) => this._lossy = lossy;

    internal int Read(ref MsbBitReader bits) {
      if (this._run > 0) {
        --this._run;
        this._Update(0);
        return 0;
      }

      var encoded = bits.ReadUnsignedRice(this._Parameter());
      this._Update((encoded + 1) >> 1);
      if (encoded == 0) {
        if (this._save >= 0) {
          this._run = bits.ReadUnsignedRice(2);
          if (this._run > 1)
            this._save += this._run + 1;
          else
            this._save -= 3;
        } else {
          ++this._run2;
        }
        return 0;
      }

      var value = ((encoded >> 1) + this._lossy) ^ -(encoded & 1);
      if (this._run2 > 0) {
        if (this._run2 > 2)
          this._save += this._run2;
        else
          this._save -= 3;
        this._run2 = 0;
      }
      return value;
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

  private ref struct MsbBitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    internal MsbBitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._position = 0;
    }

    internal int Position => this._position;

    internal int ReadUnsignedRice(int parameter) {
      var quotient = 0;
      while (true) {
        if (this._position >= this._data.Length * 8)
          throw new InvalidDataException("A LOCO Rice code ends before its unary terminator.");
        if (this._ReadBit() != 0)
          break;
        if (++quotient > (int.MaxValue >> parameter))
          throw new InvalidDataException("A LOCO Rice quotient is too large to represent.");
      }

      return checked((quotient << parameter) | (int)this._ReadBits(parameter));
    }

    private int _ReadBit() {
      var absolute = this._position++;
      return (this._data[absolute >> 3] >> (7 - (absolute & 7))) & 1;
    }

    private uint _ReadBits(int count) {
      if (count == 0)
        return 0;
      if (count < 0 || count > 31 || this._data.Length * 8 - this._position < count)
        throw new InvalidDataException("A LOCO Rice code runs out of residual bits.");

      uint value = 0;
      for (var i = 0; i < count; ++i)
        value = (value << 1) | (uint)this._ReadBit();
      return value;
    }
  }
}
