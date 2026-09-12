using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// AC (high-frequency) coefficient decoder for one VarDCT group.
//
// Spec reference: ISO/IEC 18181-1 §G.7
// libjxl reference (BSD-3-Clause):
//   - lib/jxl/dec_group.cc       (`DecodeACVarBlock` template + GetBlockFromBitstream)
//   - lib/jxl/coeff_order.cc     (coefficient permutation reading)
//   - lib/jxl/coeff_order.h      (kStrategyOrder, kCoeffOrderOffset)
//   - lib/jxl/ac_context.h       (BlockCtxMap, ZeroDensityContext,
//                                  kCoeffFreqContext, kCoeffNumNonzeroContext)
// =====================================================================================

internal static class JxlAcDecoder {

  // -------------------------------------------------------------------------
  // libjxl constants from ac_context.h
  // -------------------------------------------------------------------------

  /// <summary>Number of "predicted nzeros" buckets used for the non-zero context.
  /// libjxl <c>kNonZeroBuckets = 37</c>.</summary>
  internal const int NonZeroBuckets = 37;

  /// <summary>libjxl <c>kZeroDensityContextCount = 458</c>.</summary>
  internal const int ZeroDensityContextCount = 458;

  /// <summary>libjxl <c>kCoeffFreqContext[64]</c> from ac_context.h.</summary>
  internal static readonly ushort[] CoeffFreqContext = new ushort[64] {
    0,  0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14,
    15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
    23, 23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26,
    27, 27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30,
  };

  /// <summary>libjxl <c>kCoeffNumNonzeroContext[64]</c> from ac_context.h.</summary>
  internal static readonly ushort[] CoeffNumNonzeroContext = new ushort[64] {
    0,   0,   31,  62,  62,  93,  93,  93,  93,  123, 123, 123, 123,
    152, 152, 152, 152, 152, 152, 152, 152, 180, 180, 180, 180, 180,
    180, 180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
    206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
    206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
  };

  /// <summary>Natural-coefficient-order LUT for DCT8.</summary>
  internal static readonly ushort[] Dct8NaturalOrder = _ComputeDct8NaturalOrder();

  private static ushort[] _ComputeDct8NaturalOrder() {
    var order = new ushort[64];
    var cx = 1; var cy = 1; const int kBlockDim = 8;
    var cur = (ushort)(cx * cy);
    for (var i = 0; i < cx * kBlockDim; i++) {
      for (var j = 0; j <= i; j++) {
        var x = j;
        var y = i - j;
        if ((i & 1) != 0) (x, y) = (y, x);
        ushort val;
        if (x < cx && y < cy)
          val = (ushort)(y * cx + x);
        else
          val = cur++;
        order[val] = (ushort)(x * cx * kBlockDim + y);
      }
    }
    for (var ip = cx * kBlockDim - 1; ip > 0; ip--) {
      var i = ip - 1;
      for (var j = 0; j <= i; j++) {
        var x = cx * kBlockDim - 1 - (i - j);
        var y = cx * kBlockDim - 1 - j;
        if ((i & 1) != 0) (x, y) = (y, x);
        var val = cur++;
        order[val] = (ushort)(x * cx * kBlockDim + y);
      }
    }
    return order;
  }

  // -------------------------------------------------------------------------
  // Public API
  // -------------------------------------------------------------------------

  /// <summary>
  /// Decode one AC pass for a VarDCT group. When <paramref name="destination"/>
  /// is supplied, the pass is accumulated into those already-decoded blocks.
  /// This is how JPEG XL spectral and quantized-LSB progression reconstruct the
  /// final coefficient image: each pass contributes signed coefficients shifted
  /// by <paramref name="coefficientShift"/> bits.
  /// </summary>
  /// <param name="contextOffset">Selected AC-histogram bank times
  /// <see cref="JxlBlockContextMap.NumACContexts"/> for this pass/group.</param>
  /// <param name="coefficientShift">Pass left shift from FrameHeader.Passes.</param>
  /// <param name="destination">Previously decoded blocks to accumulate into,
  /// or <c>null</c> for the first pass.</param>
  public static JxlDctBlock[][] DecodeGroup(
    JxlBitReader reader,
    JxlEntropyDecoder entropy,
    JxlAcStrategyType[][] strategies,
    JxlBlockContextMap contextMap,
    int groupBlocksWide,
    int groupBlocksHigh,
    int numChannels,
    int[][]? quantField = null,
    bool[][]? origins = null,
    int[][][]? coeffOrders = null,
    int contextOffset = 0,
    int coefficientShift = 0,
    JxlDctBlock[][]? destination = null
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(entropy);
    ArgumentNullException.ThrowIfNull(strategies);
    ArgumentNullException.ThrowIfNull(contextMap);
    if (groupBlocksWide < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksWide), "Must be >= 0.");
    if (groupBlocksHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksHigh), "Must be >= 0.");
    if (numChannels <= 0)
      throw new ArgumentOutOfRangeException(nameof(numChannels), "Must be positive.");
    if (contextOffset < 0)
      throw new ArgumentOutOfRangeException(nameof(contextOffset));
    if (coefficientShift is < 0 or > 3)
      throw new ArgumentOutOfRangeException(nameof(coefficientShift), "JPEG XL pass shifts are two-bit values.");
    if (strategies.Length != groupBlocksHigh)
      throw new ArgumentException(
        $"Strategies grid has {strategies.Length} rows but groupBlocksHigh = {groupBlocksHigh}.",
        nameof(strategies));
    for (var y = 0; y < groupBlocksHigh; ++y) {
      if (strategies[y] is null)
        throw new ArgumentException($"strategies[{y}] is null.", nameof(strategies));
      if (strategies[y].Length != groupBlocksWide)
        throw new ArgumentException(
          $"strategies[{y}] has {strategies[y].Length} columns but groupBlocksWide = {groupBlocksWide}.",
          nameof(strategies));
    }

    var totalBlocks = groupBlocksWide * groupBlocksHigh;
    var result = destination ?? _CreateBlocks(strategies, groupBlocksWide, groupBlocksHigh, numChannels);
    _ValidateDestination(result, strategies, groupBlocksWide, groupBlocksHigh, numChannels);

    // Each pass/group has its own ANS state word even though its histograms were
    // decoded once in AC global. The histogram selector bits, if any, are read
    // by the caller immediately before this reset.
    entropy.ResetForGroup(reader, distanceMultiplier: 0);

    if (totalBlocks == 0)
      return result;

    // Nonzero-count prediction is per pass in libjxl, not shared between
    // progressive passes.
    var nzerosPlane = new int[numChannels][];
    for (var c = 0; c < numChannels; ++c)
      nzerosPlane[c] = new int[totalBlocks];

    // Bitstream channel order is Y, X, B.
    ReadOnlySpan<int> channelOrder = stackalloc int[3] { 1, 0, 2 };
    for (var by = 0; by < groupBlocksHigh; ++by)
      for (var bx = 0; bx < groupBlocksWide;) {
        var strategy = strategies[by][bx];
        var wide = JxlAcStrategyGeometry.BlocksWide(strategy);

        if (JxlAcStrategyGeometry.IsCovered(strategy)) {
          ++bx;
          continue;
        }

        if (!(origins is not null ? origins[by][bx] : JxlAcStrategyGeometry.IsTransformOrigin(strategies, bx, by))) {
          bx += wide;
          continue;
        }

        for (var ci = 0; ci < numChannels; ++ci) {
          var c = numChannels == 3 ? channelOrder[ci] : ci;
          if (c >= numChannels) continue;

          _DecodeAcVarBlock(
            entropy, contextMap, strategy,
            nzerosPlane[c], bx, by, groupBlocksWide, groupBlocksHigh,
            quantField is null ? 0 : quantField[by][bx],
            channel: c,
            outBlock: result[c][by * groupBlocksWide + bx].Coefficients,
            coeffOrders: coeffOrders,
            contextOffset: contextOffset,
            coefficientShift: coefficientShift);
        }

        bx += wide;
      }

    return result;
  }

  private static JxlDctBlock[][] _CreateBlocks(
    JxlAcStrategyType[][] strategies, int groupBlocksWide, int groupBlocksHigh, int numChannels
  ) {
    var totalBlocks = groupBlocksWide * groupBlocksHigh;
    var result = new JxlDctBlock[numChannels][];
    for (var c = 0; c < numChannels; ++c) {
      result[c] = new JxlDctBlock[totalBlocks];
      for (var by = 0; by < groupBlocksHigh; ++by)
      for (var bx = 0; bx < groupBlocksWide; ++bx) {
        var strategy = strategies[by][bx];
        var coefficients = JxlAcStrategyGeometry.CoveredBlocks(strategy) * 64;
        result[c][by * groupBlocksWide + bx] = new JxlDctBlock {
          Width = 8 * JxlAcStrategyGeometry.BlocksWide(strategy),
          Height = 8 * JxlAcStrategyGeometry.BlocksHigh(strategy),
          Coefficients = new short[coefficients],
        };
      }
    }
    return result;
  }

  private static void _ValidateDestination(
    JxlDctBlock[][] result,
    JxlAcStrategyType[][] strategies,
    int groupBlocksWide,
    int groupBlocksHigh,
    int numChannels
  ) {
    if (result.Length != numChannels)
      throw new ArgumentException($"Destination has {result.Length} channels, expected {numChannels}.", nameof(result));
    var totalBlocks = groupBlocksWide * groupBlocksHigh;
    for (var c = 0; c < numChannels; ++c) {
      if (result[c].Length != totalBlocks)
        throw new ArgumentException($"Destination channel {c} has {result[c].Length} blocks, expected {totalBlocks}.", nameof(result));
      for (var by = 0; by < groupBlocksHigh; ++by)
      for (var bx = 0; bx < groupBlocksWide; ++bx) {
        var block = result[c][by * groupBlocksWide + bx];
        var strategy = strategies[by][bx];
        var expected = JxlAcStrategyGeometry.CoveredBlocks(strategy) * 64;
        if (block.Coefficients.Length != expected)
          throw new ArgumentException(
            $"Destination block ({bx},{by}) channel {c} has {block.Coefficients.Length} coefficients, expected {expected}.",
            nameof(result));
      }
    }
  }

  // -------------------------------------------------------------------------
  // libjxl DecodeACVarBlock
  // -------------------------------------------------------------------------

  private static void _DecodeAcVarBlock(
    JxlEntropyDecoder entropy,
    JxlBlockContextMap contextMap,
    JxlAcStrategyType strategy,
    int[] rowNzeros,
    int bx, int by, int blocksWide, int blocksHigh,
    int quantField,
    int channel,
    short[] outBlock,
    int[][][]? coeffOrders,
    int contextOffset,
    int coefficientShift
  ) {
    var log2CoveredBlocks = JxlAcStrategyGeometry.Log2Blocks(strategy);
    var coveredBlocks = 1 << log2CoveredBlocks;
    var size = coveredBlocks * 64;

    var predicted = _PredictNonZeros(rowNzeros, blocksWide, bx, by);
    var blockCtx = contextMap.GetContext(channel, strategy, contextMap.QuantFieldIndex(quantField));

    var nzeroCtx = contextOffset + _NonZeroContext(predicted, blockCtx, contextMap.NumContexts);
    var nzeros = entropy.ReadInt(nzeroCtx);
    if (nzeros < 0 || nzeros > size - coveredBlocks)
      throw new System.IO.InvalidDataException(
        $"Invalid AC: nzeros={nzeros} out of [0, {size - coveredBlocks}] " +
        $"at block ({bx},{by}) channel {channel}.");

    var order = coeffOrders is null
      ? JxlNaturalCoeffOrder.For(strategy)
      : JxlCoeffOrderDecoder.For(coeffOrders, strategy, channel);
    var stored = (nzeros + coveredBlocks - 1) >> log2CoveredBlocks;
    var wide = JxlAcStrategyGeometry.BlocksWide(strategy);
    var high = JxlAcStrategyGeometry.BlocksHigh(strategy);
    for (var cy = 0; cy < high && by + cy < blocksHigh; ++cy)
    for (var cx = 0; cx < wide && bx + cx < blocksWide; ++cx)
      rowNzeros[(by + cy) * blocksWide + bx + cx] = stored;

    var histoOffset = contextOffset + _ZeroDensityContextsOffset(blockCtx, contextMap.NumContexts);
    var prev = nzeros > size / 16 ? 0 : 1;
    for (var k = coveredBlocks; k < size && nzeros != 0; ++k) {
      var ctx = histoOffset + _ZeroDensityContext(
        (uint)nzeros, (uint)k, coveredBlocks, log2CoveredBlocks, (uint)prev);
      var uCoeff = entropy.ReadInt(ctx);
      var magnitude = (uint)uCoeff >> 1;
      var negSign = (~(uint)uCoeff) & 1u;
      var coeff = (int)(magnitude ^ (negSign - 1u));

      // Progressive passes are summed in coefficient space. Quantized-LSB
      // progression uses this shift; spectral progression normally has zero
      // shifts and simply contributes disjoint frequency coefficients.
      var shifted = checked(coeff * (1 << coefficientShift));
      var index = order[k];
      var accumulated = checked(outBlock[index] + shifted);
      if (accumulated is < short.MinValue or > short.MaxValue)
        throw new System.IO.InvalidDataException(
          $"Progressive AC coefficient {accumulated} exceeds the decoder's 16-bit coefficient range.");
      outBlock[index] = (short)accumulated;

      prev = uCoeff != 0 ? 1 : 0;
      nzeros -= prev;
    }

    if (nzeros != 0)
      throw new System.IO.InvalidDataException(
        $"Invalid AC: nzeros at end of block is {nzeros}, should be 0. " +
        $"Block ({bx},{by}), channel {channel}.");
  }

  private static int _PredictNonZeros(int[] rowNzeros, int blocksWide, int bx, int by) {
    if (bx == 0)
      return by > 0 ? rowNzeros[(by - 1) * blocksWide] : 32;
    if (by == 0)
      return rowNzeros[bx - 1];

    return (rowNzeros[(by - 1) * blocksWide + bx] + rowNzeros[by * blocksWide + bx - 1] + 1) / 2;
  }

  // -------------------------------------------------------------------------
  // libjxl context-formula helpers (BlockCtxMap inline methods)
  // -------------------------------------------------------------------------

  internal static int _NonZeroContext(int nonZeros, int blockCtx, int numCtxs) {
    if (nonZeros >= 64) nonZeros = 64;
    var ctx = nonZeros < 8 ? nonZeros : 4 + nonZeros / 2;
    return ctx * numCtxs + blockCtx;
  }

  internal static int _ZeroDensityContextsOffset(int blockCtx, int numCtxs)
    => numCtxs * NonZeroBuckets + ZeroDensityContextCount * blockCtx;

  internal static int _ZeroDensityContext(uint nonzerosLeft, uint k, int coveredBlocks, int log2CoveredBlocks, uint prev) {
    nonzerosLeft = (nonzerosLeft + (uint)coveredBlocks - 1u) >> log2CoveredBlocks;
    k >>= log2CoveredBlocks;
    if (nonzerosLeft >= 64u || k >= 64u)
      throw new System.IO.InvalidDataException(
        $"ZeroDensityContext: nonzerosLeft={nonzerosLeft}, k={k} out of range.");
    return (CoeffNumNonzeroContext[nonzerosLeft] + CoeffFreqContext[k]) * 2 + (int)prev;
  }
}
