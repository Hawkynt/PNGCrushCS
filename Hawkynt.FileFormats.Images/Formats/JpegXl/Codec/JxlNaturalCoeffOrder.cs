using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>The order a transform's coefficients are stated in.</summary>
/// <remarks>
/// Coefficients do not arrive in the arrangement they are used in. They arrive
/// roughly lowest frequency first, along the diagonals of the block, and the
/// order says where each one belongs. For a plain 8x8 that is the zigzag every
/// JPEG-like format uses; for the larger and the rectangular transforms it is
/// the same walk over a square of the longer side, with the rows a tall
/// transform does not have dropped from it.
///
/// <para>libjxl <c>AcStrategy::ComputeNaturalCoeffOrder</c>. The lowest
/// coefficients — one per block the transform covers — are placed first, in
/// their own small raster, because they are what the DC of each covered block
/// becomes.</para>
/// </remarks>
internal static class JxlNaturalCoeffOrder {

  private const int _BlockDim = 8;

  private static readonly Dictionary<JxlAcStrategyType, int[]> _Cache = new();

  /// <summary>Where each coefficient of a transform belongs, in arrival order.</summary>
  public static int[] For(JxlAcStrategyType strategy) {
    lock (_Cache) {
      if (_Cache.TryGetValue(strategy, out var cached))
        return cached;

      var order = _Compute(strategy);
      _Cache[strategy] = order;
      return order;
    }
  }

  private static int[] _Compute(JxlAcStrategyType strategy) {
    // The walk is over the wider of the two dimensions; a transform taller than
    // it is wide is laid out transposed.
    var wide = JxlAcStrategyGeometry.BlocksWide(strategy);
    var high = JxlAcStrategyGeometry.BlocksHigh(strategy);
    var cy = Math.Min(wide, high);
    var cx = Math.Max(wide, high);

    var aspect = cx / cy;
    var aspectMask = aspect - 1;
    var aspectShift = _CeilLog2(aspect);

    var order = new int[cx * cy * _BlockDim * _BlockDim];
    var next = cx * cy;
    var span = cx * _BlockDim;

    // The walk runs over a matrix `span` wide and `cy * 8` high. The inverse
    // transform reads a block of the shape the transform actually has, so the
    // positions have to be written at that width — which for some shapes is the
    // walk's own width and for others is the other one, and the two are a
    // transpose apart. Where both are the same, as for a plain 8x8, the
    // transposed form is the one that reproduces the table this decoder has
    // always used, entry for entry.
    var (blockWidth, _) = JxlVarDctIdct.BlockSize(strategy);
    var transposed = blockWidth != span || blockWidth == cy * _BlockDim;
    var stride = transposed ? cy * _BlockDim : span;

    // The diagonals from the top-left corner, alternating direction.
    for (var i = 0; i < span; ++i)
    for (var j = 0; j <= i; ++j) {
      var x = j;
      var y = i - j;
      if ((i & 1) != 0)
        (x, y) = (y, x);
      if ((y & aspectMask) != 0)
        continue;

      y >>= aspectShift;
      var at = x < cx && y < cy ? y * cx + x : next++;
      order[at] = transposed ? x * stride + y : y * stride + x;
    }

    // The diagonals running back to the bottom-right corner.
    for (var ip = span - 1; ip > 0; --ip) {
      var i = ip - 1;
      for (var j = 0; j <= i; ++j) {
        var x = span - 1 - (i - j);
        var y = span - 1 - j;
        if ((i & 1) != 0)
          (x, y) = (y, x);
        if ((y & aspectMask) != 0)
          continue;

        y >>= aspectShift;
        order[next++] = transposed ? x * stride + y : y * stride + x;
      }
    }

    return order;
  }

  private static int _CeilLog2(int value) {
    var bits = 0;
    while ((1 << bits) < value)
      ++bits;

    return bits;
  }
}
