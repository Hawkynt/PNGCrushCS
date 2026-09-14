using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Encodes RGB binary16 samples as BPTC/BC6H blocks for Hap HDR.</summary>
/// <remarks>
/// BC6H has fourteen endpoint modes. This writer uses the specification's one-subset mode with two
/// direct ten-bit RGB endpoints and four-bit indices. It is intentionally a conforming baseline,
/// not a rate-distortion search across every mode; adding such a search later changes quality, not
/// syntax. Signed blocks use the BC6S interpretation and unsigned blocks the BC6U interpretation.
/// </remarks>
internal static class HapBc6Encoding {

  private static ReadOnlySpan<byte> _Weights => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

  /// <summary>Compresses one 4x4 RGB half-float block into sixteen BC6H bytes.</summary>
  public static void Compress(ReadOnlySpan<ushort> rgbHalfBits, bool isSigned, Span<byte> destination) {
    if (rgbHalfBits.Length < 48)
      throw new ArgumentException("A BC6H block needs sixteen RGB half-float pixels.", nameof(rgbHalfBits));
    if (destination.Length < 16)
      throw new ArgumentException("A BC6H block needs sixteen destination bytes.", nameof(destination));

    for (var i = 0; i < 48; ++i) {
      var bits = rgbHalfBits[i];
      if ((bits & 0x7C00) == 0x7C00)
        throw new ArgumentException("BC6H input must be finite; infinities and NaNs have no finite BC6H endpoint representation.", nameof(rgbHalfBits));
      if (!isSigned && (bits & 0x8000) != 0 && (bits & 0x7FFF) != 0)
        throw new ArgumentException("BC6U cannot represent negative samples.", nameof(rgbHalfBits));
    }

    _FindFarthestPair(rgbHalfBits, out var firstPixel, out var secondPixel);

    Span<int> first = stackalloc int[3];
    Span<int> second = stackalloc int[3];
    Span<int> firstUnquantized = stackalloc int[3];
    Span<int> secondUnquantized = stackalloc int[3];
    for (var channel = 0; channel < 3; ++channel) {
      first[channel] = _QuantizeEndpoint(rgbHalfBits[firstPixel * 3 + channel], isSigned);
      second[channel] = _QuantizeEndpoint(rgbHalfBits[secondPixel * 3 + channel], isSigned);
      firstUnquantized[channel] = _Unquantize(first[channel], isSigned);
      secondUnquantized[channel] = _Unquantize(second[channel], isSigned);
    }

    Span<byte> indices = stackalloc byte[16];
    for (var pixel = 0; pixel < 16; ++pixel)
      indices[pixel] = _BestIndex(rgbHalfBits.Slice(pixel * 3, 3), firstUnquantized, secondUnquantized, isSigned);

    // The first texel is the one-subset fix-up index and stores three rather than four bits. Swapping
    // the endpoints and complementing all indices preserves the reconstructed block exactly.
    if (indices[0] >= 8) {
      _Swap(first, second);
      _Swap(firstUnquantized, secondUnquantized);
      for (var i = 0; i < indices.Length; ++i)
        indices[i] = (byte)(15 - indices[i]);
    }

    var writer = new BitWriter(destination[..16]);
    writer.Write(0b00011, 5); // one-subset mode: two direct 10-bit RGB endpoints
    for (var channel = 0; channel < 3; ++channel)
      writer.Write((uint)first[channel] & 0x3FFu, 10);
    for (var channel = 0; channel < 3; ++channel)
      writer.Write((uint)second[channel] & 0x3FFu, 10);

    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 ? 3 : 4);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC6H one-subset mode produced {writer.BitsWritten} bits instead of 128.");
  }

  private static void _FindFarthestPair(ReadOnlySpan<ushort> samples, out int first, out int second) {
    first = 0;
    second = 0;
    var bestDistance = -1.0f;

    for (var a = 0; a < 16; ++a)
      for (var b = a; b < 16; ++b) {
        var distance = 0.0f;
        for (var channel = 0; channel < 3; ++channel) {
          var delta = _Value(samples[a * 3 + channel]) - _Value(samples[b * 3 + channel]);
          distance += delta * delta;
        }

        if (distance <= bestDistance)
          continue;

        bestDistance = distance;
        first = a;
        second = b;
      }
  }

  private static int _QuantizeEndpoint(ushort targetBits, bool isSigned) {
    var negative = (targetBits & 0x8000) != 0 && (targetBits & 0x7FFF) != 0;
    var magnitudeBits = targetBits & 0x7FFF;
    var divisor = isSigned ? 62 : 31;
    var maximum = isSigned ? 511 : 1023;
    var estimate = Math.Clamp((magnitudeBits + divisor / 2) / divisor, 0, maximum);
    var target = _Value(targetBits);

    var best = 0;
    var bestError = float.PositiveInfinity;
    var first = Math.Max(0, estimate - 3);
    var last = Math.Min(maximum, estimate + 3);
    for (var magnitude = first; magnitude <= last; ++magnitude) {
      var candidate = isSigned && negative ? -magnitude : magnitude;
      var decoded = _Value(_DecodedHalfBits(candidate, isSigned));
      var error = MathF.Abs(decoded - target);
      if (error >= bestError)
        continue;

      bestError = error;
      best = candidate;
    }

    return best;
  }

  private static byte _BestIndex(
    ReadOnlySpan<ushort> pixel,
    ReadOnlySpan<int> first,
    ReadOnlySpan<int> second,
    bool isSigned
  ) {
    var weights = _Weights;
    var bestIndex = 0;
    var bestError = float.PositiveInfinity;

    for (var index = 0; index < weights.Length; ++index) {
      var weight = weights[index];
      var error = 0.0f;
      for (var channel = 0; channel < 3; ++channel) {
        var interpolated = (first[channel] * (64 - weight) + second[channel] * weight + 32) >> 6;
        var candidate = _Value(_FinishUnquantize(interpolated, isSigned));
        var delta = _Value(pixel[channel]) - candidate;
        error += delta * delta;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestIndex = index;
    }

    return (byte)bestIndex;
  }

  private static int _Unquantize(int value, bool isSigned) {
    const int bits = 10;
    if (!isSigned) {
      if (value == 0)
        return 0;
      if (value == (1 << bits) - 1)
        return 0xFFFF;
      return ((value << 16) + 0x8000) >> bits;
    }

    var negative = value < 0;
    if (negative)
      value = -value;

    int unquantized;
    if (value == 0)
      unquantized = 0;
    else if (value >= (1 << (bits - 1)) - 1)
      unquantized = 0x7FFF;
    else
      unquantized = ((value << 15) + 0x4000) >> (bits - 1);

    return negative ? -unquantized : unquantized;
  }

  private static ushort _DecodedHalfBits(int value, bool isSigned)
    => _FinishUnquantize(_Unquantize(value, isSigned), isSigned);

  private static ushort _FinishUnquantize(int value, bool isSigned) {
    if (!isSigned)
      return (ushort)((value * 31) >> 6);

    value = value < 0 ? -((-value * 31) >> 5) : value * 31 >> 5;
    var sign = 0;
    if (value < 0) {
      sign = 0x8000;
      value = -value;
    }

    return (ushort)(sign | value);
  }

  private static float _Value(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

  private static void _Swap(Span<int> first, Span<int> second) {
    for (var i = 0; i < first.Length; ++i)
      (first[i], second[i]) = (second[i], first[i]);
  }

  private ref struct BitWriter {
    private readonly Span<byte> _data;
    private int _bit;

    public BitWriter(Span<byte> data) {
      this._data = data;
      this._data.Clear();
      this._bit = 0;
    }

    public int BitsWritten => this._bit;

    public void Write(uint value, int bitCount) {
      for (var bit = 0; bit < bitCount; ++bit) {
        if (((value >> bit) & 1) != 0)
          this._data[this._bit >> 3] |= (byte)(1 << (this._bit & 7));
        ++this._bit;
      }
    }
  }
}