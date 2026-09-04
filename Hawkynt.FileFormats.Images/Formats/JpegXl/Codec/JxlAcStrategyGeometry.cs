using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>How many 8x8 blocks each transform covers, and how they are counted.</summary>
/// <remarks>
/// A VarDCT frame is not a grid of 8x8 blocks. A transform may cover a
/// rectangle of them — up to 32 by 32 — and the file states one strategy per
/// covered rectangle rather than per block. Every part of the decode depends on
/// this: how many coefficients a block holds, which entropy context it takes,
/// how far the scan advances, and where the next block begins.
///
/// <para>The tables are libjxl's <c>AcStrategy::covered_blocks_x</c>,
/// <c>covered_blocks_y</c> and <c>log2_covered_blocks</c>, indexed by the raw
/// strategy the bitstream states.</para>
/// </remarks>
internal static class JxlAcStrategyGeometry {

  /// <summary>How many strategies the format defines.</summary>
  public const int Count = 27;

  private static readonly byte[] _BlocksWide = {
    1, 1, 1, 1, 2, 4, 1, 2, 1,
    4, 2, 4, 1, 1, 1, 1, 1, 1,
    8, 4, 8, 16, 8, 16, 32, 16, 32,
  };

  private static readonly byte[] _BlocksHigh = {
    1, 1, 1, 1, 2, 4, 2, 1, 4,
    1, 4, 2, 1, 1, 1, 1, 1, 1,
    8, 8, 4, 16, 16, 8, 32, 32, 16,
  };

  private static readonly byte[] _Log2Blocks = {
    0, 0, 0, 0, 2, 4, 1, 1, 2,
    2, 3, 3, 0, 0, 0, 0, 0, 0,
    6, 5, 5, 8, 7, 7, 10, 9, 9,
  };

  public static bool IsValid(int rawStrategy) => (uint)rawStrategy < Count;

  /// <summary>
  /// Whether a cell marks a block another transform already covers rather than
  /// naming one of its own.
  /// </summary>
  public static bool IsCovered(JxlAcStrategyType strategy) => !IsValid((int)strategy);

  public static int BlocksWide(JxlAcStrategyType strategy)
    => IsCovered(strategy) ? 1 : _BlocksWide[(int)strategy];

  public static int BlocksHigh(JxlAcStrategyType strategy)
    => IsCovered(strategy) ? 1 : _BlocksHigh[(int)strategy];

  /// <summary>
  /// The base-two logarithm of the covered block count.
  /// </summary>
  /// <remarks>
  /// This is not always <c>log2(wide * high)</c>: a 16x8 transform covers two
  /// blocks and states one, but the table is what the format uses and the
  /// entropy contexts are keyed on it.
  /// </remarks>
  public static int Log2Blocks(JxlAcStrategyType strategy)
    => IsCovered(strategy) ? 0 : _Log2Blocks[(int)strategy];

  public static int CoveredBlocks(JxlAcStrategyType strategy) => 1 << Log2Blocks(strategy);

  /// <summary>
  /// Whether a block is where its transform starts rather than one it covers.
  /// </summary>
  /// <remarks>
  /// Every block of a transform's rectangle carries the same strategy, so the
  /// origin is the one with no neighbour above or to the left carrying it.
  /// Blocks are visited in raster order, so those two are the only ones that
  /// need looking at.
  /// </remarks>
  public static bool IsTransformOrigin(JxlAcStrategyType[][] strategies, int bx, int by) {
    var strategy = strategies[by][bx];
    if (IsCovered(strategy))
      return false;
    if (BlocksWide(strategy) == 1 && BlocksHigh(strategy) == 1)
      return true;
    if (bx > 0 && strategies[by][bx - 1] == strategy)
      return false;

    return by <= 0 || strategies[by - 1][bx] != strategy;
  }
}
