using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Lapped Biorthogonal Transform (LBT) for JPEG XR (ITU-T T.832).
/// Combines the 4x4 Photo Core Transform (PCT) with overlap pre/post-filtering
/// at macroblock boundaries for deblocking artifacts.
/// </summary>
/// <remarks>
/// The LBT operates in two stages:
/// 1. Overlap pre-filter (applied at block boundaries before transform)
/// 2. Photo Core Transform (4x4 integer transform per block)
///
/// For the inverse, the order is reversed:
/// 1. Inverse PCT per block
/// 2. Overlap post-filter at block boundaries
///
/// The overlap filter operates on 4 samples straddling a block boundary (2 from each block),
/// reducing blocking artifacts while maintaining perfect reconstruction.
/// </remarks>
internal static class JxrLbt {

  private const int _MACROBLOCK_SIZE = 16;
  private const int _BLOCK_SIZE = 4;
  private const int _BLOCKS_PER_MB = _MACROBLOCK_SIZE / _BLOCK_SIZE; // 4

  /// <summary>
  /// Applies the full forward LBT pipeline to a single-channel macroblock (16x16).
  /// Steps: overlap pre-filter at internal block boundaries, then forward PCT on each 4x4 block.
  /// </summary>
  /// <param name="mb">256-element array (16x16 in row-major order).</param>
  public static void ForwardLbt(Span<int> mb) {
    if (mb.Length < 256)
      throw new ArgumentException("Macroblock must have at least 256 elements.", nameof(mb));

    _ApplyForwardOverlap(mb);
    _ApplyForwardPctAllBlocks(mb);
  }

  /// <summary>
  /// Applies the full inverse LBT pipeline to a single-channel macroblock.
  /// Steps: inverse PCT on each 4x4 block, then overlap post-filter at boundaries.
  /// </summary>
  public static void InverseLbt(Span<int> mb) {
    if (mb.Length < 256)
      throw new ArgumentException("Macroblock must have at least 256 elements.", nameof(mb));

    _ApplyInversePctAllBlocks(mb);
    _ApplyInverseOverlap(mb);
  }

  /// <summary>
  /// Applies the forward LBT pipeline to a multi-channel macroblock.
  /// Each channel is independently transformed.
  /// </summary>
  public static void ForwardLbt(int[][] channels) {
    for (var c = 0; c < channels.Length; ++c)
      ForwardLbt(channels[c]);
  }

  /// <summary>
  /// Applies the inverse LBT pipeline to a multi-channel macroblock.
  /// </summary>
  public static void InverseLbt(int[][] channels) {
    for (var c = 0; c < channels.Length; ++c)
      InverseLbt(channels[c]);
  }

  /// <summary>
  /// Applies overlap pre-filtering at inter-macroblock boundaries for a row of macroblocks.
  /// This handles the boundary between adjacent macroblocks horizontally and vertically.
  /// </summary>
  /// <param name="mbRow">Array of macroblocks in the current row, each 256 elements.</param>
  /// <param name="mbRowAbove">Array of macroblocks in the row above (null for first row).</param>
  /// <param name="channel">Which channel to process.</param>
  public static void ForwardInterMbOverlap(int[][][] mbRow, int[][][]? mbRowAbove, int channel) {
    if (mbRow.Length == 0)
      return;

    // Horizontal inter-MB overlap (between adjacent MBs in the same row)
    for (var i = 0; i < mbRow.Length - 1; ++i)
      _ApplyInterMbOverlapH(mbRow[i][channel], mbRow[i + 1][channel]);

    // Vertical inter-MB overlap (between current row and row above)
    if (mbRowAbove == null)
      return;

    for (var i = 0; i < mbRow.Length && i < mbRowAbove.Length; ++i)
      _ApplyInterMbOverlapV(mbRowAbove[i][channel], mbRow[i][channel]);
  }

  /// <summary>
  /// Applies inverse overlap post-filtering at inter-macroblock boundaries.
  /// </summary>
  public static void InverseInterMbOverlap(int[][][] mbRow, int[][][]? mbRowAbove, int channel) {
    if (mbRow.Length == 0)
      return;

    // Reverse order: vertical first, then horizontal
    if (mbRowAbove != null)
      for (var i = 0; i < mbRow.Length && i < mbRowAbove.Length; ++i)
        _ApplyInverseInterMbOverlapV(mbRowAbove[i][channel], mbRow[i][channel]);

    for (var i = 0; i < mbRow.Length - 1; ++i)
      _ApplyInverseInterMbOverlapH(mbRow[i][channel], mbRow[i + 1][channel]);
  }

  /// <summary>
  /// Extracts the 4x4 DC sub-band from a transformed macroblock.
  /// After PCT, each 4x4 block's [0,0] coefficient forms the DC sub-band.
  /// </summary>
  /// <param name="mb">Transformed 256-element macroblock.</param>
  /// <param name="dc">16-element output for the 4x4 DC sub-band.</param>
  public static void ExtractDcSubBand(ReadOnlySpan<int> mb, Span<int> dc) {
    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx)
      dc[by * _BLOCKS_PER_MB + bx] = mb[by * _BLOCK_SIZE * _MACROBLOCK_SIZE + bx * _BLOCK_SIZE];
  }

  /// <summary>
  /// Inserts the 4x4 DC sub-band back into a macroblock's transform domain.
  /// </summary>
  public static void InsertDcSubBand(ReadOnlySpan<int> dc, Span<int> mb) {
    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx)
      mb[by * _BLOCK_SIZE * _MACROBLOCK_SIZE + bx * _BLOCK_SIZE] = dc[by * _BLOCKS_PER_MB + bx];
  }

  /// <summary>
  /// Applies a second-stage 4x4 Hadamard transform on the DC sub-band
  /// to produce the hierarchical DC/LP decomposition used by JPEG XR.
  /// </summary>
  public static void ForwardDcTransform(Span<int> dc) {
    if (dc.Length < 16)
      throw new ArgumentException("DC sub-band must have at least 16 elements.", nameof(dc));

    JxrTransform.ForwardPCT(dc);
  }

  /// <summary>
  /// Applies the inverse second-stage transform on the DC sub-band.
  /// </summary>
  public static void InverseDcTransform(Span<int> dc) {
    if (dc.Length < 16)
      throw new ArgumentException("DC sub-band must have at least 16 elements.", nameof(dc));

    JxrTransform.InversePCT(dc);
  }

  #region Forward internal overlap

  /// <summary>Applies forward overlap pre-filter at all internal 4x4 block boundaries within a macroblock.</summary>
  private static void _ApplyForwardOverlap(Span<int> mb) {
    Span<int> boundary = stackalloc int[4];

    // Vertical block boundaries (between columns of blocks at x=4,8,12)
    for (var row = 0; row < _MACROBLOCK_SIZE; ++row)
    for (var bx = 1; bx < _BLOCKS_PER_MB; ++bx) {
      var col = bx * _BLOCK_SIZE - 2;
      boundary[0] = mb[row * _MACROBLOCK_SIZE + col];
      boundary[1] = mb[row * _MACROBLOCK_SIZE + col + 1];
      boundary[2] = mb[row * _MACROBLOCK_SIZE + col + 2];
      boundary[3] = mb[row * _MACROBLOCK_SIZE + col + 3];
      _ForwardOverlap4(boundary);
      mb[row * _MACROBLOCK_SIZE + col] = boundary[0];
      mb[row * _MACROBLOCK_SIZE + col + 1] = boundary[1];
      mb[row * _MACROBLOCK_SIZE + col + 2] = boundary[2];
      mb[row * _MACROBLOCK_SIZE + col + 3] = boundary[3];
    }

    // Horizontal block boundaries (between rows of blocks at y=4,8,12)
    for (var col = 0; col < _MACROBLOCK_SIZE; ++col)
    for (var by = 1; by < _BLOCKS_PER_MB; ++by) {
      var row = by * _BLOCK_SIZE - 2;
      boundary[0] = mb[row * _MACROBLOCK_SIZE + col];
      boundary[1] = mb[(row + 1) * _MACROBLOCK_SIZE + col];
      boundary[2] = mb[(row + 2) * _MACROBLOCK_SIZE + col];
      boundary[3] = mb[(row + 3) * _MACROBLOCK_SIZE + col];
      _ForwardOverlap4(boundary);
      mb[row * _MACROBLOCK_SIZE + col] = boundary[0];
      mb[(row + 1) * _MACROBLOCK_SIZE + col] = boundary[1];
      mb[(row + 2) * _MACROBLOCK_SIZE + col] = boundary[2];
      mb[(row + 3) * _MACROBLOCK_SIZE + col] = boundary[3];
    }
  }

  /// <summary>Applies inverse overlap post-filter at all internal block boundaries within a macroblock.</summary>
  private static void _ApplyInverseOverlap(Span<int> mb) {
    Span<int> boundary = stackalloc int[4];

    // Reverse order: horizontal first, then vertical
    for (var col = 0; col < _MACROBLOCK_SIZE; ++col)
    for (var by = 1; by < _BLOCKS_PER_MB; ++by) {
      var row = by * _BLOCK_SIZE - 2;
      boundary[0] = mb[row * _MACROBLOCK_SIZE + col];
      boundary[1] = mb[(row + 1) * _MACROBLOCK_SIZE + col];
      boundary[2] = mb[(row + 2) * _MACROBLOCK_SIZE + col];
      boundary[3] = mb[(row + 3) * _MACROBLOCK_SIZE + col];
      _InverseOverlap4(boundary);
      mb[row * _MACROBLOCK_SIZE + col] = boundary[0];
      mb[(row + 1) * _MACROBLOCK_SIZE + col] = boundary[1];
      mb[(row + 2) * _MACROBLOCK_SIZE + col] = boundary[2];
      mb[(row + 3) * _MACROBLOCK_SIZE + col] = boundary[3];
    }

    for (var row = 0; row < _MACROBLOCK_SIZE; ++row)
    for (var bx = 1; bx < _BLOCKS_PER_MB; ++bx) {
      var col = bx * _BLOCK_SIZE - 2;
      boundary[0] = mb[row * _MACROBLOCK_SIZE + col];
      boundary[1] = mb[row * _MACROBLOCK_SIZE + col + 1];
      boundary[2] = mb[row * _MACROBLOCK_SIZE + col + 2];
      boundary[3] = mb[row * _MACROBLOCK_SIZE + col + 3];
      _InverseOverlap4(boundary);
      mb[row * _MACROBLOCK_SIZE + col] = boundary[0];
      mb[row * _MACROBLOCK_SIZE + col + 1] = boundary[1];
      mb[row * _MACROBLOCK_SIZE + col + 2] = boundary[2];
      mb[row * _MACROBLOCK_SIZE + col + 3] = boundary[3];
    }
  }

  #endregion

  #region Inter-macroblock overlap

  /// <summary>
  /// Applies horizontal inter-macroblock overlap between the right edge of <paramref name="left"/>
  /// and the left edge of <paramref name="right"/>.
  /// </summary>
  private static void _ApplyInterMbOverlapH(Span<int> left, Span<int> right) {
    Span<int> boundary = stackalloc int[4];
    for (var row = 0; row < _MACROBLOCK_SIZE; ++row) {
      boundary[0] = left[row * _MACROBLOCK_SIZE + 14];
      boundary[1] = left[row * _MACROBLOCK_SIZE + 15];
      boundary[2] = right[row * _MACROBLOCK_SIZE];
      boundary[3] = right[row * _MACROBLOCK_SIZE + 1];
      _ForwardOverlap4(boundary);
      left[row * _MACROBLOCK_SIZE + 14] = boundary[0];
      left[row * _MACROBLOCK_SIZE + 15] = boundary[1];
      right[row * _MACROBLOCK_SIZE] = boundary[2];
      right[row * _MACROBLOCK_SIZE + 1] = boundary[3];
    }
  }

  /// <summary>
  /// Applies vertical inter-macroblock overlap between the bottom edge of <paramref name="above"/>
  /// and the top edge of <paramref name="below"/>.
  /// </summary>
  private static void _ApplyInterMbOverlapV(Span<int> above, Span<int> below) {
    Span<int> boundary = stackalloc int[4];
    for (var col = 0; col < _MACROBLOCK_SIZE; ++col) {
      boundary[0] = above[14 * _MACROBLOCK_SIZE + col];
      boundary[1] = above[15 * _MACROBLOCK_SIZE + col];
      boundary[2] = below[col];
      boundary[3] = below[_MACROBLOCK_SIZE + col];
      _ForwardOverlap4(boundary);
      above[14 * _MACROBLOCK_SIZE + col] = boundary[0];
      above[15 * _MACROBLOCK_SIZE + col] = boundary[1];
      below[col] = boundary[2];
      below[_MACROBLOCK_SIZE + col] = boundary[3];
    }
  }

  private static void _ApplyInverseInterMbOverlapH(Span<int> left, Span<int> right) {
    Span<int> boundary = stackalloc int[4];
    for (var row = 0; row < _MACROBLOCK_SIZE; ++row) {
      boundary[0] = left[row * _MACROBLOCK_SIZE + 14];
      boundary[1] = left[row * _MACROBLOCK_SIZE + 15];
      boundary[2] = right[row * _MACROBLOCK_SIZE];
      boundary[3] = right[row * _MACROBLOCK_SIZE + 1];
      _InverseOverlap4(boundary);
      left[row * _MACROBLOCK_SIZE + 14] = boundary[0];
      left[row * _MACROBLOCK_SIZE + 15] = boundary[1];
      right[row * _MACROBLOCK_SIZE] = boundary[2];
      right[row * _MACROBLOCK_SIZE + 1] = boundary[3];
    }
  }

  private static void _ApplyInverseInterMbOverlapV(Span<int> above, Span<int> below) {
    Span<int> boundary = stackalloc int[4];
    for (var col = 0; col < _MACROBLOCK_SIZE; ++col) {
      boundary[0] = above[14 * _MACROBLOCK_SIZE + col];
      boundary[1] = above[15 * _MACROBLOCK_SIZE + col];
      boundary[2] = below[col];
      boundary[3] = below[_MACROBLOCK_SIZE + col];
      _InverseOverlap4(boundary);
      above[14 * _MACROBLOCK_SIZE + col] = boundary[0];
      above[15 * _MACROBLOCK_SIZE + col] = boundary[1];
      below[col] = boundary[2];
      below[_MACROBLOCK_SIZE + col] = boundary[3];
    }
  }

  #endregion

  #region Forward PCT on all blocks

  /// <summary>Applies forward PCT to all sixteen 4x4 blocks within the 16x16 macroblock.</summary>
  private static void _ApplyForwardPctAllBlocks(Span<int> mb) {
    Span<int> block = stackalloc int[16];
    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx) {
      _ExtractBlock(mb, block, bx * _BLOCK_SIZE, by * _BLOCK_SIZE);
      JxrTransform.ForwardPCT(block);
      _InsertBlock(mb, block, bx * _BLOCK_SIZE, by * _BLOCK_SIZE);
    }
  }

  /// <summary>Applies inverse PCT to all sixteen 4x4 blocks within the 16x16 macroblock.</summary>
  private static void _ApplyInversePctAllBlocks(Span<int> mb) {
    Span<int> block = stackalloc int[16];
    for (var by = 0; by < _BLOCKS_PER_MB; ++by)
    for (var bx = 0; bx < _BLOCKS_PER_MB; ++bx) {
      _ExtractBlock(mb, block, bx * _BLOCK_SIZE, by * _BLOCK_SIZE);
      JxrTransform.InversePCT(block);
      _InsertBlock(mb, block, bx * _BLOCK_SIZE, by * _BLOCK_SIZE);
    }
  }

  #endregion

  #region Block extract/insert

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _ExtractBlock(ReadOnlySpan<int> mb, Span<int> block, int x, int y) {
    for (var r = 0; r < _BLOCK_SIZE; ++r)
    for (var c = 0; c < _BLOCK_SIZE; ++c)
      block[r * _BLOCK_SIZE + c] = mb[(y + r) * _MACROBLOCK_SIZE + x + c];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _InsertBlock(Span<int> mb, ReadOnlySpan<int> block, int x, int y) {
    for (var r = 0; r < _BLOCK_SIZE; ++r)
    for (var c = 0; c < _BLOCK_SIZE; ++c)
      mb[(y + r) * _MACROBLOCK_SIZE + x + c] = block[r * _BLOCK_SIZE + c];
  }

  #endregion

  #region 4-element overlap filters

  /// <summary>
  /// Forward 4-element overlap pre-filter (lifting-based).
  /// Operates on 4 samples straddling a block boundary: 2 from each side.
  /// Based on the T.832 4-tap overlap operator.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _ForwardOverlap4(Span<int> data) {
    // Stage 1: Predict
    data[1] -= (data[0] + data[2] + 1) >> 1;
    data[2] -= (data[1] + data[3] + 1) >> 1;

    // Stage 2: Update
    data[0] += (data[1] + 1) >> 1;
    data[3] += (data[2] + 1) >> 1;
  }

  /// <summary>
  /// Inverse 4-element overlap post-filter (reverses the forward filter).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _InverseOverlap4(Span<int> data) {
    // Reverse Stage 2
    data[3] -= (data[2] + 1) >> 1;
    data[0] -= (data[1] + 1) >> 1;

    // Reverse Stage 1
    data[2] += (data[1] + data[3] + 1) >> 1;
    data[1] += (data[0] + data[2] + 1) >> 1;
  }

  #endregion
}
