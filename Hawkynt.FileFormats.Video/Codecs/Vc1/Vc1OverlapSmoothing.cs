using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// The overlapped transform's smoothing filter (SMPTE 421M 8.5).
/// </summary>
/// <remarks>
/// An overlapped transform is simulated by coupling an ordinary 8x8 block transform with a filter
/// across the block edges afterwards, which is what keeps a coarsely quantised intra picture from
/// showing its block grid. In Simple and Main profile it runs on an I picture only when the sequence
/// asked for it and the picture quantiser is 9 or above (8.5.1); below that the encoder was fine
/// enough that the edges do not need it.
/// <para/>
/// Three details decide whether the output is exact. It runs on the unclamped reconstruction, because
/// the filter can push a sample outside what a byte holds and clamping first would lose it. Vertical
/// edges are filtered before horizontal ones, and the two-by-two corner where both apply keeps the
/// full precision of the vertical result rather than a rounded one. And the rounding constants
/// alternate: 4 then 3 down odd rows and columns, 3 then 4 down even ones, counting from one inside
/// the block.
/// </remarks>
internal static class Vc1OverlapSmoothing {

  /// <summary>Filters every 8x8 block edge of a plane, vertical edges first (8.5).</summary>
  internal static void Apply(int[] plane, int width, int height) {
    // Every internal vertical edge: the four samples straddling it are two from each block.
    for (var x = 8; x < width; x += 8)
      for (var y = 0; y < height; ++y) {
        var at = (y * width) + x;
        _Filter(plane, at - 2, at - 1, at, at + 1, (y & 1) == 0);
      }

    for (var y = 8; y < height; y += 8)
      for (var x = 0; x < width; ++x) {
        var at = (y * width) + x;
        _Filter(plane, at - (2 * width), at - width, at, at + width, (x & 1) == 0);
      }
  }

  /// <summary>
  /// The core filter over the four samples straddling one edge.
  /// </summary>
  /// <param name="oddLine">
  /// Whether this row or column is an odd one counting from one, which decides which way round the two
  /// rounding constants go.
  /// </param>
  private static void _Filter(int[] plane, int i0, int i1, int i2, int i3, bool oddLine) {
    var x0 = plane[i0];
    var x1 = plane[i1];
    var x2 = plane[i2];
    var x3 = plane[i3];

    var r0 = oddLine ? 4 : 3;
    var r1 = oddLine ? 3 : 4;

    plane[i0] = ((x0 * 7) + x3 + r0) >> 3;
    plane[i1] = (-x0 + (x1 * 7) + x2 + x3 + r1) >> 3;
    plane[i2] = (x0 + x1 + (x2 * 7) - x3 + r0) >> 3;
    plane[i3] = (x0 + (x3 * 7) + r1) >> 3;
  }
}
