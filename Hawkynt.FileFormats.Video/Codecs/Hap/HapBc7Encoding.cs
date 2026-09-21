using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Encodes RGBA pixels as BPTC/BC7 blocks for Hap R.</summary>
/// <remarks>
/// Every block compares two conforming encodings: mode 6's single subset with four-bit indices and
/// all 64 mode-7 two-subset partitions. Mode 6 remains the strong general-purpose fallback; mode 7
/// wins on blocks whose colours separate naturally into two regions. The partition patterns and
/// fix-up anchors are specification-defined BPTC data, not implementation-specific tables.
/// </remarks>
internal static class HapBc7Encoding {

  private static ReadOnlySpan<byte> _Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];
  private static ReadOnlySpan<byte> _Weights2 => [0, 21, 43, 64];

  private static ReadOnlySpan<ushort> _Partitions2 => [
    0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80,
    0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
    0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE,
    0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
    0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A,
    0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
    0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C,
    0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22,
  ];

  private static ReadOnlySpan<byte> _Anchor2 => [
    15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
    15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
    15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
    6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
  ];

  /// <summary>Compresses one tightly packed 4x4 RGBA block into sixteen BC7 bytes.</summary>
  public static void Compress(ReadOnlySpan<byte> rgba, Span<byte> destination) {
    if (rgba.Length < 64)
      throw new ArgumentException("A BC7 block needs sixteen RGBA pixels.", nameof(rgba));
    if (destination.Length < 16)
      throw new ArgumentException("A BC7 block needs sixteen destination bytes.", nameof(destination));

    Span<byte> mode6 = stackalloc byte[16];
    Span<byte> mode7 = stackalloc byte[16];
    var mode6Error = _EncodeMode6(rgba, mode6);
    var mode7Error = _EncodeBestMode7(rgba, mode7);
    (mode7Error < mode6Error ? mode7 : mode6).CopyTo(destination);
  }

  private static int _EncodeMode6(ReadOnlySpan<byte> rgba, Span<byte> destination) {
    _FindFarthestPair(rgba, 0xFFFF, out var firstPixel, out var secondPixel);

    Span<byte> quantized = stackalloc byte[8];
    Span<byte> decoded = stackalloc byte[8];
    _QuantizeMode6Endpoint(rgba.Slice(firstPixel * 4, 4), quantized[..4], decoded[..4], out var firstPBit);
    _QuantizeMode6Endpoint(rgba.Slice(secondPixel * 4, 4), quantized[4..], decoded[4..], out var secondPBit);

    Span<byte> indices = stackalloc byte[16];
    var error = 0;
    for (var pixel = 0; pixel < 16; ++pixel) {
      indices[pixel] = _BestIndex(rgba.Slice(pixel * 4, 4), decoded[..4], decoded[4..], _Weights4, out var pixelError);
      error += pixelError;
    }

    if (indices[0] >= 8) {
      _Swap(quantized[..4], quantized[4..]);
      (firstPBit, secondPBit) = (secondPBit, firstPBit);
      for (var pixel = 0; pixel < 16; ++pixel)
        indices[pixel] = (byte)(15 - indices[pixel]);
    }

    var writer = new BitWriter(destination[..16]);
    writer.Write(1u << 6, 7);
    for (var channel = 0; channel < 4; ++channel) {
      writer.Write(quantized[channel], 7);
      writer.Write(quantized[4 + channel], 7);
    }
    writer.Write(firstPBit, 1);
    writer.Write(secondPBit, 1);
    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 ? 3 : 4);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC7 mode 6 produced {writer.BitsWritten} bits instead of 128.");
    return error;
  }

  private static int _EncodeBestMode7(ReadOnlySpan<byte> rgba, Span<byte> destination) {
    var bestError = int.MaxValue;
    Span<byte> candidate = stackalloc byte[16];
    var partitions = _Partitions2;

    for (var partition = 0; partition < partitions.Length; ++partition) {
      var error = _EncodeMode7Partition(rgba, partition, candidate);
      if (error >= bestError)
        continue;

      bestError = error;
      candidate.CopyTo(destination);
    }

    return bestError;
  }

  private static int _EncodeMode7Partition(ReadOnlySpan<byte> rgba, int partition, Span<byte> destination) {
    var pattern = _Partitions2[partition];
    Span<byte> quantized = stackalloc byte[16];
    Span<byte> decoded = stackalloc byte[16];
    Span<byte> pBits = stackalloc byte[4];

    for (var subset = 0; subset < 2; ++subset) {
      var mask = subset == 0 ? (ushort)~pattern : pattern;
      _FindFarthestPair(rgba, mask, out var firstPixel, out var secondPixel);
      var endpoint = subset * 2;
      _QuantizeMode7Endpoint(
        rgba.Slice(firstPixel * 4, 4),
        quantized.Slice(endpoint * 4, 4),
        decoded.Slice(endpoint * 4, 4),
        out pBits[endpoint]);
      _QuantizeMode7Endpoint(
        rgba.Slice(secondPixel * 4, 4),
        quantized.Slice((endpoint + 1) * 4, 4),
        decoded.Slice((endpoint + 1) * 4, 4),
        out pBits[endpoint + 1]);
    }

    Span<byte> indices = stackalloc byte[16];
    var totalError = 0;
    for (var pixel = 0; pixel < 16; ++pixel) {
      var subset = (pattern >> pixel) & 1;
      var endpoint = subset * 2;
      indices[pixel] = _BestIndex(
        rgba.Slice(pixel * 4, 4),
        decoded.Slice(endpoint * 4, 4),
        decoded.Slice((endpoint + 1) * 4, 4),
        _Weights2,
        out var pixelError);
      totalError += pixelError;
    }

    _OrientMode7Subset(0, 0, pattern, quantized, decoded, pBits, indices);
    _OrientMode7Subset(1, _Anchor2[partition], pattern, quantized, decoded, pBits, indices);

    var writer = new BitWriter(destination[..16]);
    writer.Write(1u << 7, 8);
    writer.Write((uint)partition, 6);
    for (var channel = 0; channel < 4; ++channel)
      for (var endpoint = 0; endpoint < 4; ++endpoint)
        writer.Write(quantized[endpoint * 4 + channel], 5);
    for (var endpoint = 0; endpoint < 4; ++endpoint)
      writer.Write(pBits[endpoint], 1);

    var subset1Anchor = _Anchor2[partition];
    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 || pixel == subset1Anchor ? 1 : 2);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC7 mode 7 produced {writer.BitsWritten} bits instead of 128.");
    return totalError;
  }

  private static void _OrientMode7Subset(
    int subset,
    int anchor,
    ushort pattern,
    Span<byte> quantized,
    Span<byte> decoded,
    Span<byte> pBits,
    Span<byte> indices
  ) {
    if (indices[anchor] < 2)
      return;

    var first = subset * 2;
    var second = first + 1;
    _Swap(quantized.Slice(first * 4, 4), quantized.Slice(second * 4, 4));
    _Swap(decoded.Slice(first * 4, 4), decoded.Slice(second * 4, 4));
    (pBits[first], pBits[second]) = (pBits[second], pBits[first]);

    for (var pixel = 0; pixel < 16; ++pixel)
      if (((pattern >> pixel) & 1) == subset)
        indices[pixel] = (byte)(3 - indices[pixel]);
  }

  private static void _FindFarthestPair(ReadOnlySpan<byte> rgba, ushort mask, out int first, out int second) {
    first = -1;
    second = -1;
    var bestDistance = -1;

    for (var a = 0; a < 16; ++a) {
      if (((mask >> a) & 1) == 0)
        continue;

      if (first < 0)
        first = second = a;

      for (var b = a; b < 16; ++b) {
        if (((mask >> b) & 1) == 0)
          continue;

        var distance = 0;
        for (var channel = 0; channel < 4; ++channel) {
          var delta = rgba[a * 4 + channel] - rgba[b * 4 + channel];
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
      throw new InvalidOperationException("A BC7 partition contained an empty subset.");
  }

  private static void _QuantizeMode6Endpoint(
    ReadOnlySpan<byte> source,
    Span<byte> quantized,
    Span<byte> decoded,
    out byte pBit
  ) {
    var bestError = int.MaxValue;
    pBit = 0;
    Span<byte> trial = stackalloc byte[4];

    for (byte candidatePBit = 0; candidatePBit < 2; ++candidatePBit) {
      var error = 0;
      for (var channel = 0; channel < 4; ++channel) {
        var value = (byte)(source[channel] >> 1);
        trial[channel] = value;
        var reconstruction = (value << 1) | candidatePBit;
        var delta = source[channel] - reconstruction;
        error += delta * delta;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      pBit = candidatePBit;
      trial.CopyTo(quantized);
    }

    for (var channel = 0; channel < 4; ++channel)
      decoded[channel] = (byte)((quantized[channel] << 1) | pBit);
  }

  private static void _QuantizeMode7Endpoint(
    ReadOnlySpan<byte> source,
    Span<byte> quantized,
    Span<byte> decoded,
    out byte pBit
  ) {
    var bestError = int.MaxValue;
    pBit = 0;
    Span<byte> bestQuantized = stackalloc byte[4];

    for (byte candidatePBit = 0; candidatePBit < 2; ++candidatePBit) {
      var error = 0;
      Span<byte> trial = stackalloc byte[4];
      for (var channel = 0; channel < 4; ++channel) {
        var bestChannelError = int.MaxValue;
        var bestValue = 0;
        for (var value = 0; value < 32; ++value) {
          var reconstruction = _Expand6((value << 1) | candidatePBit);
          var delta = source[channel] - reconstruction;
          var channelError = delta * delta;
          if (channelError >= bestChannelError)
            continue;
          bestChannelError = channelError;
          bestValue = value;
        }
        trial[channel] = (byte)bestValue;
        error += bestChannelError;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      pBit = candidatePBit;
      trial.CopyTo(bestQuantized);
    }

    bestQuantized.CopyTo(quantized);
    for (var channel = 0; channel < 4; ++channel)
      decoded[channel] = (byte)_Expand6((quantized[channel] << 1) | pBit);
  }

  private static int _Expand6(int value) => (value << 2) | (value >> 4);

  private static byte _BestIndex(
    ReadOnlySpan<byte> pixel,
    ReadOnlySpan<byte> first,
    ReadOnlySpan<byte> second,
    ReadOnlySpan<byte> weights,
    out int bestError
  ) {
    var bestIndex = 0;
    bestError = int.MaxValue;

    for (var index = 0; index < weights.Length; ++index) {
      var weight = weights[index];
      var error = 0;
      for (var channel = 0; channel < 4; ++channel) {
        var value = (first[channel] * (64 - weight) + second[channel] * weight + 32) >> 6;
        var delta = pixel[channel] - value;
        error += delta * delta;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestIndex = index;
    }

    return (byte)bestIndex;
  }

  private static void _Swap(Span<byte> first, Span<byte> second) {
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
