using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Encodes RGBA pixels as BPTC/BC7 blocks for Hap R.</summary>
/// <remarks>
/// This is a deliberately small, specification-derived encoder. BC7 defines eight interchangeable
/// block modes; a writer is not required to search all eight. Mode 6 is the useful baseline for Hap:
/// one subset, RGBA endpoints at effectively eight-bit precision, and one four-bit interpolation
/// index per pixel. Searching more modes can improve rate-distortion quality later without changing
/// the bitstream contract or decoder.
/// </remarks>
internal static class HapBc7Encoding {

  private static ReadOnlySpan<byte> _Weights => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

  /// <summary>Compresses one tightly packed 4x4 RGBA block into sixteen BC7 bytes.</summary>
  public static void Compress(ReadOnlySpan<byte> rgba, Span<byte> destination) {
    if (rgba.Length < 64)
      throw new ArgumentException("A BC7 block needs sixteen RGBA pixels.", nameof(rgba));
    if (destination.Length < 16)
      throw new ArgumentException("A BC7 block needs sixteen destination bytes.", nameof(destination));

    _FindFarthestPair(rgba, out var firstPixel, out var secondPixel);

    Span<byte> first = stackalloc byte[4];
    Span<byte> second = stackalloc byte[4];
    Span<byte> firstDecoded = stackalloc byte[4];
    Span<byte> secondDecoded = stackalloc byte[4];
    _QuantizeEndpoint(rgba.Slice(firstPixel * 4, 4), first, firstDecoded, out var firstPBit);
    _QuantizeEndpoint(rgba.Slice(secondPixel * 4, 4), second, secondDecoded, out var secondPBit);

    Span<byte> indices = stackalloc byte[16];
    for (var pixel = 0; pixel < 16; ++pixel)
      indices[pixel] = _BestIndex(rgba.Slice(pixel * 4, 4), firstDecoded, secondDecoded);

    // Pixel zero is mode 6's fix-up texel: its index omits the high bit. Reversing the endpoint pair
    // and complementing every index produces exactly the same interpolation while making that bit 0.
    if (indices[0] >= 8) {
      _Swap(first, second);
      (firstPBit, secondPBit) = (secondPBit, firstPBit);
      for (var i = 0; i < indices.Length; ++i)
        indices[i] = (byte)(15 - indices[i]);
    }

    var writer = new BitWriter(destination[..16]);
    writer.Write(1u << 6, 7); // mode 6: six zero mode bits followed by one

    for (var channel = 0; channel < 4; ++channel) {
      writer.Write(first[channel], 7);
      writer.Write(second[channel], 7);
    }

    writer.Write(firstPBit, 1);
    writer.Write(secondPBit, 1);

    for (var pixel = 0; pixel < 16; ++pixel)
      writer.Write(indices[pixel], pixel == 0 ? 3 : 4);

    if (writer.BitsWritten != 128)
      throw new InvalidOperationException($"BC7 mode 6 produced {writer.BitsWritten} bits instead of 128.");
  }

  private static void _FindFarthestPair(ReadOnlySpan<byte> rgba, out int first, out int second) {
    first = 0;
    second = 0;
    var bestDistance = -1;

    for (var a = 0; a < 16; ++a)
      for (var b = a; b < 16; ++b) {
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

  private static void _QuantizeEndpoint(
    ReadOnlySpan<byte> source,
    Span<byte> quantized,
    Span<byte> decoded,
    out byte pBit
  ) {
    var oddChannels = 0;
    for (var channel = 0; channel < 4; ++channel)
      oddChannels += source[channel] & 1;

    pBit = oddChannels > 2 ? (byte)1 : (byte)0;
    for (var channel = 0; channel < 4; ++channel) {
      quantized[channel] = (byte)(source[channel] >> 1);
      decoded[channel] = (byte)((quantized[channel] << 1) | pBit);
    }
  }

  private static byte _BestIndex(ReadOnlySpan<byte> pixel, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var bestIndex = 0;
    var bestError = int.MaxValue;
    var weights = _Weights;

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