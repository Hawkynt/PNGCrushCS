using System;

namespace FileFormat.Core;

/// <summary>Palettes for indexed pictures whose file does not carry one.</summary>
/// <remarks>
/// Several formats store nothing but indices: the colours live in the game's own palette file
/// (Build Engine's <c>palette.dat</c>, Quake's <c>colormap</c>), in a sidecar the picture does not
/// name, or in hardware registers a screen dump never captured. A reader for one of those still has
/// to return something a caller can draw, and an <see cref="PixelFormat.Indexed8"/> image with a
/// null <see cref="RawImage.Palette"/> is not that — every conversion out of it throws, so the read
/// capability the registry advertises does not actually arrive.
/// <para/>
/// A grey ramp is the honest substitute. It says "these are indices and here is their order"
/// without inventing colours that would look like the real ones and be wrong.
/// </remarks>
public static class IndexedPalette {

  /// <summary>A ramp of <paramref name="count"/> greys from black to white, as RGB triplets.</summary>
  /// <param name="count">Number of entries, 1 to 256.</param>
  public static byte[] GrayRamp(int count) {
    ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 256);

    var result = new byte[count * 3];
    var last = count - 1;
    for (var i = 0; i < count; ++i) {
      // A single entry has no range to spread over; anything else ends on white so the top index is
      // visible rather than a shade of near-black.
      var level = (byte)(last == 0 ? 0 : i * 255 / last);
      result[i * 3] = level;
      result[i * 3 + 1] = level;
      result[i * 3 + 2] = level;
    }

    return result;
  }
}
