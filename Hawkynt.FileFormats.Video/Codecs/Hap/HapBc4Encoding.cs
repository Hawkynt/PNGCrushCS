using System;

namespace FileFormat.Codecs.Hap;

/// <summary>Encodes one unsigned RGTC1/BC4 block, the texture format used by Hap Alpha-Only and
/// the alpha image of Hap Q Alpha.</summary>
/// <remarks>
/// Derived directly from the RGTC block definition named by the Hap specification. The eight-byte
/// block is the same endpoint-and-three-bit-index ramp as DXT5's alpha block: two endpoint bytes,
/// followed by sixteen packed indices into the six interpolated values between them. This is an
/// independent encoder rather than another copy of the DXT writer's private alpha routine so the
/// single-channel formats can consume a native byte plane without manufacturing RGBA pixels first.
/// </remarks>
internal static class HapBc4Encoding {

  /// <summary>Compresses sixteen unsigned samples into one eight-byte BC4 block.</summary>
  public static void Compress(ReadOnlySpan<byte> samples, Span<byte> destination) {
    if (samples.Length < 16)
      throw new ArgumentException("A BC4 block needs sixteen samples.", nameof(samples));
    if (destination.Length < 8)
      throw new ArgumentException("A BC4 block needs eight destination bytes.", nameof(destination));

    var minimum = byte.MaxValue;
    var maximum = byte.MinValue;
    for (var i = 0; i < 16; ++i) {
      var value = samples[i];
      if (value < minimum)
        minimum = value;
      if (value > maximum)
        maximum = value;
    }

    // The eight-value branch has six interpolants and therefore the useful precision for every
    // non-constant block. A constant block deliberately leaves equal endpoints: index zero then
    // reproduces that byte exactly.
    destination[0] = maximum;
    destination[1] = minimum;

    Span<byte> palette = stackalloc byte[8];
    _BuildPalette(maximum, minimum, palette);

    ulong packed = 0;
    for (var pixel = 0; pixel < 16; ++pixel) {
      var value = samples[pixel];
      var bestIndex = 0;
      var bestError = int.MaxValue;

      for (var index = 0; index < palette.Length; ++index) {
        var error = Math.Abs(value - palette[index]);
        if (error >= bestError)
          continue;

        bestError = error;
        bestIndex = index;
      }

      packed |= (ulong)bestIndex << (pixel * 3);
    }

    for (var i = 0; i < 6; ++i)
      destination[2 + i] = (byte)(packed >> (i * 8));
  }

  private static void _BuildPalette(byte a0, byte a1, Span<byte> values) {
    values[0] = a0;
    values[1] = a1;

    if (a0 > a1) {
      values[2] = (byte)((6 * a0 + a1) / 7);
      values[3] = (byte)((5 * a0 + 2 * a1) / 7);
      values[4] = (byte)((4 * a0 + 3 * a1) / 7);
      values[5] = (byte)((3 * a0 + 4 * a1) / 7);
      values[6] = (byte)((2 * a0 + 5 * a1) / 7);
      values[7] = (byte)((a0 + 6 * a1) / 7);
      return;
    }

    values[2] = (byte)((4 * a0 + a1) / 5);
    values[3] = (byte)((3 * a0 + 2 * a1) / 5);
    values[4] = (byte)((2 * a0 + 3 * a1) / 5);
    values[5] = (byte)((a0 + 4 * a1) / 5);
    values[6] = 0;
    values[7] = 255;
  }
}