using System.Drawing;

namespace Optimizer.Gif;

/// <summary>Converts between FileFormat.Gif's <c>byte[]</c> packed-RGB palettes and
/// <see cref="System.Drawing.Color"/> arrays the optimizer was originally written against.
/// Net effect: the optimizer keeps its System.Drawing-based palette math intact while the on-wire
/// representation (FileFormat.Gif) stays cross-platform.</summary>
internal static class PaletteAdapter {

  public static Color[] ToColors(byte[]? packed) {
    if (packed == null) return [];
    var n = packed.Length / 3;
    var result = new Color[n];
    for (var i = 0; i < n; ++i)
      result[i] = Color.FromArgb(packed[i * 3], packed[i * 3 + 1], packed[i * 3 + 2]);
    return result;
  }

  public static byte[] ToBytes(Color[]? colors) {
    if (colors == null) return [];
    var result = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i) {
      result[i * 3] = colors[i].R;
      result[i * 3 + 1] = colors[i].G;
      result[i * 3 + 2] = colors[i].B;
    }
    return result;
  }
}
