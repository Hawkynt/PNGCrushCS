using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// AC (high-frequency) coefficient decoder for one VarDCT group.
//
// Spec reference: ISO/IEC 18181-1 §G.7
// libjxl reference (BSD-3-Clause):
//   - lib/jxl/dec_group.cc       (`DecodeACVarBlock` template + GetBlockFromBitstream)
//     https://github.com/libjxl/libjxl/blob/main/lib/jxl/dec_group.cc
//   - lib/jxl/coeff_order.cc     (coefficient permutation reading)
//     https://github.com/libjxl/libjxl/blob/main/lib/jxl/coeff_order.cc
//   - lib/jxl/coeff_order.h      (kStrategyOrder, kCoeffOrderOffset)
//     https://github.com/libjxl/libjxl/blob/main/lib/jxl/coeff_order.h
//   - lib/jxl/ac_context.h       (BlockCtxMap, ZeroDensityContext,
//                                  kCoeffFreqContext, kCoeffNumNonzeroContext)
//     https://github.com/libjxl/libjxl/blob/main/lib/jxl/ac_context.h
//
// ALGORITHM (libjxl `DecodeACVarBlock` for DCT8):
//
//   For each block (in row-major order across the group):
//     1. Compute the block_ctx via `BlockCtxMap.Context(dc_idx, qf, ord, c)`.
//     2. Compute `predicted_nzeros` from top + left neighbour nzeros (uses an
//        `nzeros` plane stored per channel, one int32 per 8x8 block). For our
//        first cut we use `predicted = 32` (the libjxl clamp value, equivalent
//        to "no neighbour data").
//     3. nzero_ctx = block_ctx_map.NonZeroContext(predicted, block_ctx) +
//                    ctx_offset
//        Read `nzeros` via `entropy.ReadInt(nzero_ctx)`. Bound check:
//        `nzeros <= size - covered_blocks` (i.e. at most 63 for DCT8).
//     4. Store nzeros into the row_nzeros plane (used by the next row's
//        prediction). For DCT8 (covered_blocks=1, log2_covered_blocks=0)
//        this is just one cell per block.
//     5. histo_offset = ctx_offset + ZeroDensityContextsOffset(block_ctx).
//        Iterate scan positions k = covered_blocks ... size while nzeros > 0:
//          a. ctx = histo_offset + ZeroDensityContext(nzeros, k,
//                                  covered_blocks, log2_covered_blocks, prev)
//          b. u_coeff = entropy.ReadInt(ctx)
//          c. value = UnpackSigned(u_coeff)  (hand-rolled: magnitude xor sign-mask)
//          d. block[order[k]] += value (we store as scan-order short[] though,
//             not natural-order — see "FIRST-WAVE SIMPLIFICATIONS" below).
//          e. prev = (u_coeff != 0)
//          f. if prev: --nzeros
//     6. After the loop, nzeros must be 0 (else: corrupted bitstream).
//
// FIRST-WAVE SIMPLIFICATIONS (this implementation):
//   - DCT8 only. Larger AC strategies (DCT16+, rectangular, AFV, Hornuss) and
//     non-first sub-blocks (`JxlAcStrategyDecoder.CoveredByNeighbour`) are
//     accepted in the strategy grid, but rejected with `NotImplementedException`
//     at decode time. This matches the JxlVarDctIdct first-wave scope.
//   - We DO NOT yet read the per-block `coeff_order` permutation (libjxl
//     `DecodeCoeffOrders`). For DCT8 the natural order is identical to the
//     bitstream's "scan order" when no permutation is signalled, and our
//     output array is in scan order regardless. The downstream IDCT path will
//     need un-zigzag-to-natural before consuming this — wired in
//     JxlVarDctIdct, which already documents that input is natural-order.
//   - Predicted nzeros is hard-coded to libjxl's `min(32, ...)` clamp value
//     (32) — i.e. the same prediction we'd get with a top-left of zero. This
//     matches libjxl behaviour at the top-left corner of every group, so the
//     contexts at (bx=0, by=0) are correct; the contexts at later blocks may
//     be sub-optimal but will still decode correctly because the entropy
//     decoder's context map is provided externally.
//   - `ctx_offset` (multi-pass histogram selector) is fixed to 0 — single-pass
//     decode only. Multi-pass requires the frame's pass list and a histogram
//     selector bit-field, which is part of the wider VarDCT group orchestrator
//     (still TODO). See FIRST-WAVE NOTES at the bottom of this file.
// =====================================================================================

internal static class JxlAcDecoder {

  // -------------------------------------------------------------------------
  // libjxl constants from ac_context.h
  // -------------------------------------------------------------------------

  /// <summary>Number of "predicted nzeros" buckets used for the non-zero context.
  /// libjxl <c>kNonZeroBuckets = 37</c>. Predicted nzeros is in 0..1008 but is
  /// clustered to ceil(log2(predicted+1)) → 0..10, then offset by 32 = 37
  /// total buckets. Used by <see cref="_NonZeroContext"/>.</summary>
  internal const int NonZeroBuckets = 37;

  /// <summary>libjxl <c>kZeroDensityContextCount = 458</c>. Supremum of
  /// <see cref="_ZeroDensityContext"/>(x, y) + 1, when x + y &lt; 64.</summary>
  internal const int ZeroDensityContextCount = 458;

  /// <summary>libjxl <c>kCoeffFreqContext[64]</c> from ac_context.h. Maps a
  /// scan-order index k ∈ [1..63] to a frequency context bucket. Index 0 is
  /// unused (DC is decoded separately) and stored as <c>0xBAD</c> in libjxl;
  /// we store 0 instead since this entry is never read.</summary>
  internal static readonly ushort[] CoeffFreqContext = new ushort[64] {
    0,  0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  10, 11, 12, 13, 14,
    15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22,
    23, 23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26,
    27, 27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30,
  };

  /// <summary>libjxl <c>kCoeffNumNonzeroContext[64]</c> from ac_context.h.
  /// Maps a number-of-non-zeros-left value (after clustering) to a base
  /// offset added to the freq-context bucket. Index 0 is unused and stored as
  /// <c>0xBAD</c> in libjxl; we store 0.</summary>
  internal static readonly ushort[] CoeffNumNonzeroContext = new ushort[64] {
    0,   0,   31,  62,  62,  93,  93,  93,  93,  123, 123, 123, 123,
    152, 152, 152, 152, 152, 152, 152, 152, 180, 180, 180, 180, 180,
    180, 180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
    206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
    206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
  };

  /// <summary>Natural-coefficient-order LUT for DCT8: scan-position k → natural
  /// index <c>order[k]</c> within an 8×8 block (row-major y*8+x). Computed by
  /// libjxl <c>AcStrategy::ComputeNaturalCoeffOrder</c> with covered_blocks=1.
  /// For DCT8 this is the standard JPEG zigzag traversal.
  /// libjxl ref: lib/jxl/ac_strategy.cc::CoeffOrderAndLut.</summary>
  internal static readonly ushort[] Dct8NaturalOrder = _ComputeDct8NaturalOrder();

  private static ushort[] _ComputeDct8NaturalOrder() {
    // Mirrors libjxl `CoeffOrderAndLut<is_lut=false>` for the DCT8 case
    // (cx=cy=1, kBlockDim=8). libjxl stores AC blocks in COLUMN-MAJOR
    // ("Transposed") order: index `i*N + j` is (col=i, row=j). For our
    // ROW-MAJOR IDCT (which expects index `y*N + x` to mean (row y, col x)),
    // we transpose libjxl's natural index here: libjxl's `y*N + x` →
    // our `x*N + y`. That way scan-to-spatial is identical to libjxl.
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
        // Transposed natural index: libjxl writes to (col=y, row=x); for our
        // row-major IDCT, that corresponds to (row=x, col=y) = x * N + y.
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

  /// <summary>Decode all AC (high-frequency) coefficients for one VarDCT group.
  /// For each block, the AC stream produces an array of quantized coefficients
  /// in scan order (one short per coefficient position 1..63 for DCT8). The
  /// DC coefficient (index 0) is decoded separately via the LF stream and is
  /// left as zero in the returned blocks.</summary>
  /// <param name="reader">Bit reader positioned at the start of the group's
  /// AC data.</param>
  /// <param name="entropy">Entropy decoder seeded for AC contexts (one
  /// <c>numContexts</c> comes from <see cref="JxlBlockContextMap.NumContexts"/>
  /// expanded to cover the nzeros + zero-density spaces).</param>
  /// <param name="strategies">AC strategy per block, from
  /// <see cref="JxlAcStrategyDecoder"/>. Indexed
  /// <c>strategies[blockY][blockX]</c>.</param>
  /// <param name="contextMap">Block context map for context lookup.</param>
  /// <param name="groupBlocksWide">Number of 8×8 blocks across the group.</param>
  /// <param name="groupBlocksHigh">Number of 8×8 blocks down the group.</param>
  /// <param name="numChannels">Number of XYB channels (typically 3).</param>
  /// <param name="coeffOrders">The scan order per bucket and channel, as the
  /// frame stated it. Null falls back to the order each shape implies, which is
  /// right only for a frame that states none of its own.</param>
  /// <param name="origins">Which cell each transform starts at, as the file
  /// stated it. Without it the start has to be guessed from the shapes of the
  /// cells around it, and that guess is wrong wherever two transforms of the
  /// same shape sit side by side — which is most of a picture coded in large
  /// transforms. Callers that only ever pass single-block transforms may leave
  /// it null.</param>
  /// <returns>Per-channel AC blocks, indexed
  /// <c>[channel][blockIdx]</c> where
  /// <c>blockIdx = blockY * groupBlocksWide + blockX</c>.</returns>
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
    int[][][]? coeffOrders = null
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
    var result = new JxlDctBlock[numChannels][];
    for (var c = 0; c < numChannels; ++c) {
      result[c] = new JxlDctBlock[totalBlocks];
      for (var by = 0; by < groupBlocksHigh; ++by)
      for (var bx = 0; bx < groupBlocksWide; ++bx) {
        // A transform holds sixty-four coefficients for every block it covers,
        // so the block at its origin carries all of them and the ones it covers
        // carry none.
        var strategy = strategies[by][bx];
        var coefficients = JxlAcStrategyGeometry.CoveredBlocks(strategy) * 64;
        result[c][by * groupBlocksWide + bx] = new JxlDctBlock {
          Width = 8 * JxlAcStrategyGeometry.BlocksWide(strategy),
          Height = 8 * JxlAcStrategyGeometry.BlocksHigh(strategy),
          Coefficients = new short[coefficients],
        };
      }
    }

    if (totalBlocks == 0)
      return result;

    // Per-channel running "predicted nzeros" plane. libjxl uses a separate
    // ImageI per channel and predicts from top + left neighbour. For first-wave
    // we keep just the "previous-row" and "this-row, previous-col" arrays so
    // PredictFromTopAndLeft can be evaluated per block. Allocated lazily —
    // for DCT8 only.
    var nzerosPlane = new int[numChannels][];
    for (var c = 0; c < numChannels; ++c)
      nzerosPlane[c] = new int[totalBlocks];

    // Per-block decode loop. We iterate channels in libjxl's order {1, 0, 2}
    // (Y first, then X, then B) — matching `DecodeGroupImpl`'s `for (size_t c
    // : {1, 0, 2})` traversal. This affects the order of bits read from the
    // entropy stream and is part of the wire contract.
    ReadOnlySpan<int> channelOrder = stackalloc int[3] { 1, 0, 2 };
    for (var by = 0; by < groupBlocksHigh; ++by)
      for (var bx = 0; bx < groupBlocksWide;) {
        var strategy = strategies[by][bx];
        var wide = JxlAcStrategyGeometry.BlocksWide(strategy);

        // A cell that names no transform of its own is one another already
        // covers, and nothing is read for it.
        if (JxlAcStrategyGeometry.IsCovered(strategy)) {
          ++bx;
          continue;
        }

        // Only the block a transform starts at is read. The rest of the
        // rectangle it covers is stepped over, which is what libjxl's
        // IsFirstBlock check amounts to.
        if (!(origins is not null ? origins[by][bx] : JxlAcStrategyGeometry.IsTransformOrigin(strategies, bx, by))) {
          bx += wide;
          continue;
        }

        for (var ci = 0; ci < numChannels; ++ci) {
          // Map the iteration index to a logical channel. For numChannels < 3
          // we use the natural channel index; for == 3, libjxl's {1,0,2}.
          var c = numChannels == 3 ? channelOrder[ci] : ci;
          if (c >= numChannels) continue;

          _DecodeAcVarBlock(
            entropy, contextMap, strategy,
            nzerosPlane[c], bx, by, groupBlocksWide, groupBlocksHigh,
            quantField is null ? 0 : quantField[by][bx],
            channel: c,
            outBlock: result[c][by * groupBlocksWide + bx].Coefficients,
            coeffOrders: coeffOrders);
        }

        bx += wide;
      }

    return result;
  }

  // -------------------------------------------------------------------------
  // libjxl `DecodeACVarBlock` for DCT8 (covered_blocks = 1, log2 = 0)
  // -------------------------------------------------------------------------

  /// <summary>Decode the AC stream for one DCT8 block (one channel).
  /// Mirrors libjxl <c>DecodeACVarBlock&lt;ACType::k16, false&gt;</c> with
  /// <c>log2_covered_blocks = 0</c> and <c>covered_blocks = 1</c>.</summary>
  private static void _DecodeAcVarBlock(
    JxlEntropyDecoder entropy,
    JxlBlockContextMap contextMap,
    JxlAcStrategyType strategy,
    int[] rowNzeros,
    int bx, int by, int blocksWide, int blocksHigh,
    int quantField,
    int channel,
    short[] outBlock,
    int[][][]? coeffOrders
  ) {
    var log2CoveredBlocks = JxlAcStrategyGeometry.Log2Blocks(strategy);
    var coveredBlocks = 1 << log2CoveredBlocks;
    var size = coveredBlocks * 64;

    // (1) Predicted nzeros from the blocks above and to the left, per libjxl
    //     `PredictFromTopAndLeft` in entropy_coder.h. The average is only taken
    //     when both neighbours exist: down the first column the block above is
    //     the whole prediction, along the first row the block to the left is,
    //     and only the very first block of the group falls back to 32. This
    //     used to average whichever neighbour existed against that 32 and then
    //     clamp the result, so three of the four cases predicted a number the
    //     encoder never predicted, read the count of non-zero coefficients from
    //     the wrong histogram, and lost the stream at the first block that had
    //     any. A flat picture hid it: where every block's count is zero, every
    //     histogram answers zero without spending a bit, so the wrong one
    //     answers the same as the right one.
    var predicted = _PredictNonZeros(rowNzeros, blocksWide, bx, by);

    // (2) The block's context, keyed on the quantisation step the metadata
    //     stated for it. The DC bucket stays zero until the DC plane is
    //     thresholded, which only matters for a file that states DC thresholds.
    var blockCtx = contextMap.GetContext(channel, strategy, contextMap.QuantFieldIndex(quantField));

    // (3) nzeros context. libjxl `NonZeroContext`:
    //   ctx = predicted < 8 ? predicted : 4 + predicted/2
    //   ctx = ctx * num_ctxs + block_ctx
    var nzeroCtx = _NonZeroContext(predicted, blockCtx, contextMap.NumContexts);

    var nzeros = entropy.ReadInt(nzeroCtx);
    if (nzeros < 0 || nzeros > size - coveredBlocks)
      throw new System.IO.InvalidDataException(
        $"Invalid AC: nzeros={nzeros} out of [0, {size - coveredBlocks}] " +
        $"at block ({bx},{by}) channel {channel}.");

    // (4) Store the count over every block the transform covers, so the
    //     neighbours that predict from it see the same number.
    // The frame may state a scan order of its own for this shape. Where it
    // does, taking the natural one instead puts every coefficient of every
    // block of that shape somewhere else than the file put it.
    var order = coeffOrders is null
      ? JxlNaturalCoeffOrder.For(strategy)
      : JxlCoeffOrderDecoder.For(coeffOrders, strategy, channel);
    var stored = (nzeros + coveredBlocks - 1) >> log2CoveredBlocks;
    var wide = JxlAcStrategyGeometry.BlocksWide(strategy);
    var high = JxlAcStrategyGeometry.BlocksHigh(strategy);
    for (var cy = 0; cy < high && by + cy < blocksHigh; ++cy)
    for (var cx = 0; cx < wide && bx + cx < blocksWide; ++cx)
      rowNzeros[(by + cy) * blocksWide + bx + cx] = stored;

    // (5) Iterate non-zero coefficient positions.
    var histoOffset = _ZeroDensityContextsOffset(blockCtx, contextMap.NumContexts);
    var prev = nzeros > size / 16 ? 0 : 1;
    for (var k = coveredBlocks; k < size && nzeros != 0; ++k) {
      var ctx = histoOffset + _ZeroDensityContext(
        (uint)nzeros, (uint)k, coveredBlocks, log2CoveredBlocks, (uint)prev);
      var uCoeff = entropy.ReadInt(ctx);
      // Hand-rolled UnpackSigned: (u_coeff >> 1) ^ -(u_coeff & 1).
      // libjxl uses `(magnitude ^ (neg_sign - 1))` form for branch-free SIMD;
      // both produce the same result.
      var magnitude = (uint)uCoeff >> 1;
      var negSign = (~(uint)uCoeff) & 1u;
      // negSign: 1 if positive, 0 if negative — opposite of (u_coeff & 1).
      // libjxl: coeff = (magnitude ^ (negSign - 1)). When negSign=0:
      // magnitude ^ 0xFFFFFFFF = -magnitude - 1. When negSign=1: magnitude.
      var coeff = (int)(magnitude ^ (negSign - 1u));

      // libjxl `block.ptr16[order[k]] += coeff` — write to NATURAL-order
      // position so the downstream IDCT (which expects natural order) sees
      // coefficients at the correct frequency positions. For DCT8 the
      // permutation is the JPEG zigzag (precomputed in Dct8NaturalOrder).
      // Where the coefficient belongs, from the order the transform states.
      outBlock[order[k]] += (short)coeff;

      prev = uCoeff != 0 ? 1 : 0;
      nzeros -= prev;
    }

    if (nzeros != 0)
      throw new System.IO.InvalidDataException(
        $"Invalid AC: nzeros at end of block is {nzeros}, should be 0. " +
        $"Block ({bx},{by}), channel {channel}.");
  }

  /// <summary>
  /// libjxl <c>PredictFromTopAndLeft</c>: the count of non-zero coefficients a
  /// block is expected to hold, from the blocks above and to its left.
  /// </summary>
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

  /// <summary>libjxl <c>BlockCtxMap::NonZeroContext</c>:
  /// <code>
  /// if (non_zeros >= 64) non_zeros = 64;
  /// ctx = non_zeros &lt; 8 ? non_zeros : 4 + non_zeros / 2;
  /// return ctx * num_ctxs + block_ctx;
  /// </code>
  /// </summary>
  internal static int _NonZeroContext(int nonZeros, int blockCtx, int numCtxs) {
    if (nonZeros >= 64) nonZeros = 64;
    var ctx = nonZeros < 8 ? nonZeros : 4 + nonZeros / 2;
    return ctx * numCtxs + blockCtx;
  }

  /// <summary>libjxl <c>BlockCtxMap::ZeroDensityContextsOffset</c>:
  /// <code>num_ctxs * kNonZeroBuckets + kZeroDensityContextCount * block_ctx</code>.
  /// </summary>
  internal static int _ZeroDensityContextsOffset(int blockCtx, int numCtxs)
    => numCtxs * NonZeroBuckets + ZeroDensityContextCount * blockCtx;

  /// <summary>libjxl <c>ZeroDensityContext</c> from ac_context.h:
  /// <code>
  /// nonzeros_left = (nonzeros_left + covered_blocks - 1) &gt;&gt; log2_covered_blocks;
  /// k &gt;&gt;= log2_covered_blocks;
  /// return (kCoeffNumNonzeroContext[nonzeros_left] + kCoeffFreqContext[k]) * 2 + prev;
  /// </code>
  /// Caller asserts <c>k &gt; 0</c> and <c>nonzeros_left &gt; 0</c>.
  /// </summary>
  internal static int _ZeroDensityContext(uint nonzerosLeft, uint k, int coveredBlocks, int log2CoveredBlocks, uint prev) {
    nonzerosLeft = (nonzerosLeft + (uint)coveredBlocks - 1u) >> log2CoveredBlocks;
    k >>= log2CoveredBlocks;
    if (nonzerosLeft >= 64u || k >= 64u)
      throw new System.IO.InvalidDataException(
        $"ZeroDensityContext: nonzerosLeft={nonzerosLeft}, k={k} out of range.");
    return (CoeffNumNonzeroContext[nonzerosLeft] + CoeffFreqContext[k]) * 2 + (int)prev;
  }

  // =====================================================================================
  // FIRST-WAVE NOTES (left here for the next implementer):
  //
  // 1) MULTI-PASS. libjxl threads `num_passes` and a `histo_selector_bits` count
  //    through `GetBlockFromBitstream::Init`, which reads
  //    `cur_histogram = readers[pass]->ReadBits(histo_selector_bits)` and
  //    sets `ctx_offset[pass] = cur_histogram * NumACContexts()`. We assume
  //    a single pass with `ctx_offset = 0`. To wire in: extend the public API
  //    with `numPasses, histoSelectorBits` parameters and call
  //    entropy.ReadInt with ctx + ctx_offset[pass] in the inner loop.
  //
  // 2) PER-STRATEGY COEFFICIENT ORDER. libjxl's `DecodeCoeffOrders` reads up
  //    to 13 coefficient permutations (one per strategy size class) into
  //    `dec_state->shared->coeff_orders`. The bitstream signals which orders
  //    are non-default via `used_orders` (16-bit bitmap). Our first-wave
  //    output is in scan order; to consume by IDCT we need natural-order, so
  //    a separate un-zigzag pass (using the embedded natural order from
  //    `AcStrategy::ComputeNaturalCoeffOrder`) belongs in the orchestrator.
  //
  // 3) SHIFT/PROGRESSIVE. libjxl shifts coefficients by `shift_for_pass[pass]`
  //    bits when accumulating across passes (for progressive decode). With
  //    single-pass we use shift=0.
  //
  // 4) JPEG-COMPATIBLE PATH. libjxl has a separate path for JPEG-XL streams
  //    that wrap a JPEG bitstream (re-decode to JPEG coefficients). We do not
  //    implement that here — it is orthogonal to lossy VarDCT.
  // =====================================================================================
}
