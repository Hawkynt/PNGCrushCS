using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes the VBLE lossless YUV 4:2:0 codec.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/vble.c</c>, copyright (c) 2011 Derek Buitenhuis, and the
/// median-predictor helper in <c>libavcodec/lossless_videodsp.c</c>; both are distributed there under
/// LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// VBLE stores, for every output sample, a reverse-unary bit length first and all residual payloads
/// afterwards. Residual magnitudes use a signed zig-zag mapping; the first row of each plane predicts
/// from the sample to its left and later rows use the median of left, above, and the wrapped gradient
/// <c>left + above - aboveLeft</c>. The decoded planes are YUV 4:2:0 and are converted to packed RGB24
/// at the public boundary.
/// </remarks>
public sealed class VbleVideoDecoder : IVideoCodecDecoder<VbleVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VBLE");

  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;
  private readonly int _sampleCount;

  private VbleVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._chromaWidth = width / 2;
    this._chromaHeight = height / 2;
    this._sampleCount = checked(width * height + 2 * this._chromaWidth * this._chromaHeight);
  }

  public static string CodecName => "VBLE Lossless Codec";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static VbleVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"VBLE stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no samples.");

    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"VBLE stream {stream.Index} states an odd-sized {stream.Width}x{stream.Height} YUV 4:2:0 picture. "
        + "This decoder requires complete 2x2 chroma groups rather than guessing the unused fringe layout.");

    var sampleCount = (long)stream.Width * stream.Height * 3 / 2;
    if (sampleCount > int.MaxValue)
      throw new InvalidDataException($"VBLE stream {stream.Index} is too large to hold in one managed frame.");

    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 4)
      throw new InvalidDataException("A VBLE packet is shorter than its four-byte version field.");

    var version = BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (version != 1)
      throw new NotSupportedException($"This VBLE packet states version {version}; the defined codec version is 1.");

    var bits = new LittleEndianBitReader(data[4..]);
    var lengths = new byte[this._sampleCount];
    long payloadBits = 0;
    for (var i = 0; i < lengths.Length; ++i) {
      var length = _ReadReverseUnary(ref bits);
      lengths[i] = checked((byte)length);
      payloadBits += length;
    }

    if (payloadBits > bits.BitsRemaining)
      throw new InvalidDataException(
        $"This VBLE packet announces {payloadBits} residual payload bit(s), but only {bits.BitsRemaining} remain.");

    var y = new byte[this._width * this._height];
    var u = new byte[this._chromaWidth * this._chromaHeight];
    var v = new byte[this._chromaWidth * this._chromaHeight];

    var offset = 0;
    _RestorePlane(ref bits, lengths, offset, y, this._width, this._height);
    offset += y.Length;
    _RestorePlane(ref bits, lengths, offset, u, this._chromaWidth, this._chromaHeight);
    offset += u.Length;
    _RestorePlane(ref bits, lengths, offset, v, this._chromaWidth, this._chromaHeight);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(y, u, v),
    };
    return true;
  }

  private static int _ReadReverseUnary(ref LittleEndianBitReader bits) {
    for (var length = 0; length < 8; ++length) {
      bits.Require(1, "a reverse-unary length");
      if (bits.ReadBit() != 0)
        return length;
    }

    bits.Require(1, "the terminator of an eight-bit reverse-unary length");
    if (bits.ReadBit() == 0)
      throw new InvalidDataException("A VBLE reverse-unary length contains more than eight zero bits.");
    return 8;
  }

  private static void _RestorePlane(
    ref LittleEndianBitReader bits,
    ReadOnlySpan<byte> lengths,
    int lengthOffset,
    Span<byte> destination,
    int width,
    int height
  ) {
    if (width == 0 || height == 0)
      return;

    for (var row = 0; row < height; ++row) {
      var rowAt = row * width;
      for (var column = 0; column < width; ++column) {
        var length = lengths[lengthOffset + rowAt + column];
        var diff = _ReadSignedResidual(ref bits, length);

        if (row == 0) {
          var left = column == 0 ? 0 : destination[rowAt + column - 1];
          destination[rowAt + column] = unchecked((byte)(left + diff));
          continue;
        }

        var aboveAt = rowAt - width + column;
        var leftValue = column == 0 ? 0 : destination[rowAt + column - 1];
        var above = destination[aboveAt];
        var aboveLeft = column == 0 ? destination[rowAt - width] : destination[aboveAt - 1];
        var gradient = (leftValue + above - aboveLeft) & 0xFF;
        var prediction = _Median(leftValue, above, gradient);
        destination[rowAt + column] = unchecked((byte)(prediction + diff));
      }
    }
  }

  private static int _ReadSignedResidual(ref LittleEndianBitReader bits, int length) {
    if (length == 0)
      return 0;

    var encoded = (1 << length) + checked((int)bits.ReadBits(length)) - 1;
    return (encoded >> 1) ^ -(encoded & 1);
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

  private byte[] _ToRgb24(ReadOnlySpan<byte> y, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v) {
    var result = new byte[this._width * this._height * 3];
    var at = 0;
    for (var row = 0; row < this._height; ++row) {
      for (var column = 0; column < this._width; ++column) {
        var yy = y[row * this._width + column];
        var cb = u[(row >> 1) * this._chromaWidth + (column >> 1)];
        var cr = v[(row >> 1) * this._chromaWidth + (column >> 1)];

        var c = yy - 16;
        var d = cb - 128;
        var e = cr - 128;
        result[at++] = _Clamp((298 * c + 409 * e + 128) >> 8);
        result[at++] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
        result[at++] = _Clamp((298 * c + 516 * d + 128) >> 8);
      }
    }
    return result;
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private ref struct LittleEndianBitReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    internal LittleEndianBitReader(ReadOnlySpan<byte> data) {
      this._data = data;
      this._position = 0;
    }

    internal int BitsRemaining => this._data.Length * 8 - this._position;

    internal int ReadBit() => checked((int)this.ReadBits(1));

    internal uint ReadBits(int count) {
      if ((uint)count > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      this.Require(count, $"a {count}-bit residual");

      uint value = 0;
      for (var i = 0; i < count; ++i) {
        var absolute = this._position++;
        value |= (uint)((this._data[absolute >> 3] >> (absolute & 7)) & 1) << i;
      }
      return value;
    }

    internal void Require(int count, string what) {
      if (count < 0 || this.BitsRemaining < count)
        throw new InvalidDataException($"A VBLE packet runs out of bits while reading {what}.");
    }
  }
}
