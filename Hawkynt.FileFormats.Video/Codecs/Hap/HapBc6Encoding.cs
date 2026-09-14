using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Encodes RGB binary16 samples as BPTC/BC6H blocks for Hap HDR.</summary>
/// <remarks>
/// Every block compares the direct one-subset ten-bit-endpoint mode with all 32 partitions of BC6H
/// mode 0. The latter stores a ten-bit base endpoint plus three transformed five-bit endpoint deltas
/// and three-bit indices. Trying both endpoint orientations per subset satisfies BPTC's fix-up-index
/// rule without changing the reconstructed colours after the fact. Signed blocks use BC6S semantics
/// throughout and unsigned blocks BC6U.
/// </remarks>
internal static class HapBc6Encoding {

  private static ReadOnlySpan<byte> _Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];
  private static ReadOnlySpan<byte> _Weights3 => [0, 9, 18, 27, 37, 46, 55, 64];

  private static ReadOnlySpan<ushort> _Partitions2 => [
    0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80,
    0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
    0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE,
    0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
  ];

  private static ReadOnlySpan<byte> _Anchor2 => [
    15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
    15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
  ];

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

    Span<byte> direct = stackalloc byte[16];
    Span<byte> partitioned = stackalloc byte[16];
    var directError = _EncodeDirectMode(rgbHalfBits, isSigned, direct);
    var partitionedError = _EncodeBestMode0(rgbHalfBits, isSigned, partitioned);
    (partitionedError < directError ? partitioned : direct).CopyTo(destination);
  }

  private static float _EncodeDirectMode(ReadOnlySpan<ushort> samples, bool isSigned, Span<byte> destination) {
    _FindFarthestPair(samples, 0xFFFF, out var firstPixel, out var secondPixel);

    Span<int> first = stackalloc int[3];
    Span<int> second = stackalloc int[3];
    Span<int> firstUnquantized = stackalloc int[3];
    Span<int> secondUnquantized = stackalloc int[3];
    for (var channel = 0; channel < 3; ++channel) {
      first[channel] = _QuantizeEndpoint(samples[firstPixel * 3 + channel], isSigned);
      second[channel] = _QuantizeEndpoint(samples[secondPixel * 3 + channel], isSigned);
      firstUnquantized[channel] = _Unquantize(first[channel], isSigned);
      secondUnquantized[channel] = _Unquantize(second[channel], isSigned);
    }

    Span<byte> indices = stackalloc byte[16];
    var error = 0.0f;
    for (var pixel = 0; pixel < 16; ++pixel) {
      indices[pixel] = _BestIndex(
        samples.Slice(pixel * 3, 3), firstUnquantized, secondUnquantized, _Weights4, isSigned, out var pixelError);
      error += pixelError;
    }

    if (indices[0] >= 8) {
      _Swap(first, second);
      _Swap(firstUnquantized, secondUnquantized);
      for (var pixel = 0; pixel < 16; ++pixel)
        indices[pixel] = (byte)(15 - indices[pixel]);
    }

    var writer = new BitWriter(destination[..16]);
    writer.Write(0b00011, 5);
    for (var channel = 0; channel < 3; ++channel)
      writer.Write((uint)first[channel] & 0x3FFu, 10);
    for (var channel = 0; channel < 3; ++channel)
      writer.Write((uint)second[channel] & 0x3FFu, 10);
    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 ? 3 : 4);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC6H direct mode produced {writer.BitsWritten} bits instead of 128.");
    return error;
  }

  private static float _EncodeBestMode0(ReadOnlySpan<ushort> samples, bool isSigned, Span<byte> destination) {
    var bestError = float.PositiveInfinity;
    Span<byte> candidate = stackalloc byte[16];

    for (var partition = 0; partition < _Partitions2.Length; ++partition)
      for (var subset0Reverse = 0; subset0Reverse < 2; ++subset0Reverse)
        for (var subset1Reverse = 0; subset1Reverse < 2; ++subset1Reverse) {
          var error = _EncodeMode0Candidate(
            samples, isSigned, partition, subset0Reverse != 0, subset1Reverse != 0, candidate);
          if (error >= bestError)
            continue;

          bestError = error;
          candidate.CopyTo(destination);
        }

    return bestError;
  }

  private static float _EncodeMode0Candidate(
    ReadOnlySpan<ushort> samples,
    bool isSigned,
    int partition,
    bool subset0Reverse,
    bool subset1Reverse,
    Span<byte> destination
  ) {
    var pattern = _Partitions2[partition];
    var mask0 = (ushort)~pattern;
    _FindFarthestPair(samples, mask0, out var subset0First, out var subset0Second);
    _FindFarthestPair(samples, pattern, out var subset1First, out var subset1Second);
    if (subset0Reverse)
      (subset0First, subset0Second) = (subset0Second, subset0First);
    if (subset1Reverse)
      (subset1First, subset1Second) = (subset1Second, subset1First);

    Span<int> r = stackalloc int[4];
    Span<int> g = stackalloc int[4];
    Span<int> b = stackalloc int[4];
    Span<int> unquantized = stackalloc int[12];

    var endpointPixels = (E0: subset0First, E1: subset0Second, E2: subset1First, E3: subset1Second);
    Span<int> pixels = stackalloc int[4] [endpointPixels.E0, endpointPixels.E1, endpointPixels.E2, endpointPixels.E3];

    r[0] = _QuantizeEndpoint(samples[pixels[0] * 3], isSigned);
    g[0] = _QuantizeEndpoint(samples[pixels[0] * 3 + 1], isSigned);
    b[0] = _QuantizeEndpoint(samples[pixels[0] * 3 + 2], isSigned);
    unquantized[0] = _Unquantize(r[0], isSigned);
    unquantized[1] = _Unquantize(g[0], isSigned);
    unquantized[2] = _Unquantize(b[0], isSigned);

    for (var endpoint = 1; endpoint < 4; ++endpoint) {
      r[endpoint] = _QuantizeDelta(samples[pixels[endpoint] * 3], r[0], isSigned, out unquantized[endpoint * 3]);
      g[endpoint] = _QuantizeDelta(samples[pixels[endpoint] * 3 + 1], g[0], isSigned, out unquantized[endpoint * 3 + 1]);
      b[endpoint] = _QuantizeDelta(samples[pixels[endpoint] * 3 + 2], b[0], isSigned, out unquantized[endpoint * 3 + 2]);
    }

    Span<byte> indices = stackalloc byte[16];
    var totalError = 0.0f;
    for (var pixel = 0; pixel < 16; ++pixel) {
      var subset = (pattern >> pixel) & 1;
      var endpoint = subset * 2;
      indices[pixel] = _BestIndex(
        samples.Slice(pixel * 3, 3),
        unquantized.Slice(endpoint * 3, 3),
        unquantized.Slice((endpoint + 1) * 3, 3),
        _Weights3,
        isSigned,
        out var pixelError);
      totalError += pixelError;
    }

    if (indices[0] >= 4 || indices[_Anchor2[partition]] >= 4)
      return float.PositiveInfinity;

    var writer = new BitWriter(destination[..16]);
    writer.Write(0, 2);
    writer.Write((_Bits5(g[2]) >> 4) & 1, 1);
    writer.Write((_Bits5(b[2]) >> 4) & 1, 1);
    writer.Write((_Bits5(b[3]) >> 4) & 1, 1);
    writer.Write((uint)r[0] & 0x3FFu, 10);
    writer.Write((uint)g[0] & 0x3FFu, 10);
    writer.Write((uint)b[0] & 0x3FFu, 10);
    writer.Write(_Bits5(r[1]), 5);
    writer.Write((_Bits5(g[3]) >> 4) & 1, 1);
    writer.Write(_Bits5(g[2]) & 0x0F, 4);
    writer.Write(_Bits5(g[1]), 5);
    writer.Write(_Bits5(b[3]) & 1, 1);
    writer.Write(_Bits5(g[3]) & 0x0F, 4);
    writer.Write(_Bits5(b[1]), 5);
    writer.Write((_Bits5(b[3]) >> 1) & 1, 1);
    writer.Write(_Bits5(b[2]) & 0x0F, 4);
    writer.Write(_Bits5(r[2]), 5);
    writer.Write((_Bits5(b[3]) >> 2) & 1, 1);
    writer.Write(_Bits5(r[3]), 5);
    writer.Write((_Bits5(b[3]) >> 3) & 1, 1);
    writer.Write((uint)partition, 5);

    var subset1Anchor = _Anchor2[partition];
    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 || pixel == subset1Anchor ? 2 : 3);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC6H mode 0 produced {writer.BitsWritten} bits instead of 128.");
    return totalError;
  }

  private static void _FindFarthestPair(ReadOnlySpan<ushort> samples, ushort mask, out int first, out int second) {
    first = -1;
    second = -1;
    var bestDistance = -1.0f;

    for (var a = 0; a < 16; ++a) {
      if (((mask >> a) & 1) == 0)
        continue;
      if (first < 0)
        first = second = a;

      for (var b = a; b < 16; ++b) {
        if (((mask >> b) & 1) == 0)
          continue;

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

    if (first < 0)
      throw new InvalidOperationException("A BC6H partition contained an empty subset.");
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

  private static int _QuantizeDelta(ushort targetBits, int baseEndpoint, bool isSigned, out int unquantized) {
    var target = _Value(targetBits);
    var bestDelta = 0;
    var bestUnquantized = 0;
    var bestError = float.PositiveInfinity;

    for (var raw = 0; raw < 32; ++raw) {
      var delta = _ExtendSign(raw, 5);
      var transformed = (delta + baseEndpoint) & 0x3FF;
      if (isSigned)
        transformed = _ExtendSign(transformed, 10);
      var candidateUnquantized = _Unquantize(transformed, isSigned);
      var decoded = _Value(_FinishUnquantize(candidateUnquantized, isSigned));
      var error = MathF.Abs(decoded - target);
      if (error >= bestError)
        continue;

      bestError = error;
      bestDelta = delta;
      bestUnquantized = candidateUnquantized;
    }

    unquantized = bestUnquantized;
    return bestDelta;
  }

  private static byte _BestIndex(
    ReadOnlySpan<ushort> pixel,
    ReadOnlySpan<int> first,
    ReadOnlySpan<int> second,
    ReadOnlySpan<byte> weights,
    bool isSigned,
    out float bestError
  ) {
    var bestIndex = 0;
    bestError = float.PositiveInfinity;

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

  private static int _ExtendSign(int value, int bits) {
    var shift = 32 - bits;
    return value << shift >> shift;
  }

  private static uint _Bits5(int value) => (uint)value & 0x1Fu;

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
