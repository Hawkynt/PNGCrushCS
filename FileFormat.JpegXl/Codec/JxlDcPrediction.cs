using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// JPEG XL VarDCT DC prediction (Y → X / B chroma-from-luma at the DC level).
//
// libjxl reference (BSD-3-Clause):
//   - lib/jxl/chroma_from_luma.h    (ColorCorrelation::DCFactors,
//                                    YtoXRatio, YtoBRatio, ytox_dc_, ytob_dc_)
//   - lib/jxl/chroma_from_luma.cc   (ColorCorrelation::DecodeDC reads ytox_dc / ytob_dc
//                                    as int8-biased uint8s)
//   - lib/jxl/dec_modular.cc        (DequantDC applies cmap.base().DCFactors() to
//                                    Y plane and adds to X / B residual planes)
//   - lib/jxl/ac_context.h          (kDcCtxs default mapping uses thresholds on
//                                    decoded DC magnitude — feeds BlockContextMap)
//
// CONCEPTS:
//   * After Y-channel DC values are decoded, X and B DC bitstream residuals
//     are added to a Y-prediction:
//         X_DC[block] = X_residual[block] + ytoxRatio * Y_DC[block]
//         B_DC[block] = B_residual[block] + ytobRatio * Y_DC[block]
//     Where ytoxRatio/ytobRatio are *per-tile* (not per-block) cmap factors.
//   * libjxl additionally has a global ytox_dc_/ytob_dc_ DC-level bias decoded
//     by ColorCorrelation::DecodeDC; the per-task spec asks us to use the
//     per-tile cmap factor directly, so that bias is omitted (it is 0 for the
//     "is_default = 1" bitstream case, which is by far the most common).
//
//   * After full DC reconstruction, each block's DC value is compared against
//     the per-channel <c>dc_thresholds</c> (decoded by JxlBlockContextMap) to
//     produce a "DC bucket" 0..N-1 used as one factor in the AC entropy
//     context selection (libjxl: <c>BlockCtxMap::DCContext</c>).
// =====================================================================================

/// <summary>
/// Y-channel-driven DC prediction for JPEG XL VarDCT chroma channels.
///
/// <para>libjxl applies CfL twice in a VarDCT decoder: once at the AC level
/// (see <see cref="JxlColorCorrelationMap.ApplyCorrection"/>) and once at the
/// DC level — the DC residual added by the entropy coder is the chroma DC
/// value <em>minus</em> the prediction from luma DC, so the decoder must add
/// it back before continuing. This static helper performs that addition for
/// the X and B channels of one DC group.</para>
///
/// <para>References: <c>lib/jxl/dec_modular.cc DequantDC</c> and
/// <c>lib/jxl/chroma_from_luma.h ColorCorrelation::DCFactors</c>.</para>
/// </summary>
internal static class JxlDcPrediction {

  /// <summary>
  /// Apply Y-channel-based DC prediction to the X and B channels of one DC
  /// group. The X and B arrays are mutated in place — on entry they hold the
  /// signed DC residuals decoded from the bitstream, on exit they hold the
  /// reconstructed DC values <c>residual + ytoxRatio*Y</c> /
  /// <c>residual + ytobRatio*Y</c>.
  ///
  /// <para>The cmap factor is looked up per <em>tile</em> (one 64×64-pixel /
  /// 8×8-block region), not per block. The tile coordinate is derived from
  /// the absolute block position
  /// <c>((groupY/8)+by) / 8, ((groupX/8)+bx) / 8</c>.</para>
  ///
  /// <para>Fast path: if every cmap factor in the looked-up region is 0 (the
  /// no-correlation default), this method is a no-op — equivalent to libjxl's
  /// behaviour for a freshly-allocated zero-filled cmap.</para>
  /// </summary>
  /// <param name="yDcs">Decoded Y-channel DC values, length =
  /// <paramref name="groupBlocksWide"/> * <paramref name="groupBlocksHigh"/>.
  /// Read-only.</param>
  /// <param name="xDcsResidual">In: X-channel DC residuals.
  /// Out: residual + Y-prediction. Same length as <paramref name="yDcs"/>.</param>
  /// <param name="bDcsResidual">In: B-channel DC residuals.
  /// Out: residual + Y-prediction. Same length as <paramref name="yDcs"/>.</param>
  /// <param name="cmap">Per-tile color-correlation factors covering at least
  /// the block region described by <paramref name="groupX"/>/<paramref name="groupY"/>
  /// + <paramref name="groupBlocksWide"/>/<paramref name="groupBlocksHigh"/>.</param>
  /// <param name="groupBlocksWide">Group width in 8×8 blocks.</param>
  /// <param name="groupBlocksHigh">Group height in 8×8 blocks.</param>
  /// <param name="groupX">Group's top-left X in image-pixel coords (used for
  /// absolute tile lookup; see libjxl <c>block_rect.x0()</c>).</param>
  /// <param name="groupY">Group's top-left Y in image-pixel coords.</param>
  public static void PredictXandBFromY(
    short[] yDcs,
    short[] xDcsResidual,
    short[] bDcsResidual,
    JxlColorCorrelationMap cmap,
    int groupBlocksWide, int groupBlocksHigh,
    int groupX, int groupY
  ) {
    ArgumentNullException.ThrowIfNull(yDcs);
    ArgumentNullException.ThrowIfNull(xDcsResidual);
    ArgumentNullException.ThrowIfNull(bDcsResidual);
    ArgumentNullException.ThrowIfNull(cmap);
    if (groupBlocksWide < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksWide), "Must be >= 0.");
    if (groupBlocksHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksHigh), "Must be >= 0.");
    if (groupX < 0)
      throw new ArgumentOutOfRangeException(nameof(groupX), "Must be >= 0.");
    if (groupY < 0)
      throw new ArgumentOutOfRangeException(nameof(groupY), "Must be >= 0.");

    var total = groupBlocksWide * groupBlocksHigh;
    if (yDcs.Length != total)
      throw new ArgumentException(
        $"yDcs length {yDcs.Length} != groupBlocksWide*groupBlocksHigh ({total}).",
        nameof(yDcs));
    if (xDcsResidual.Length != total)
      throw new ArgumentException(
        $"xDcsResidual length {xDcsResidual.Length} != {total}.",
        nameof(xDcsResidual));
    if (bDcsResidual.Length != total)
      throw new ArgumentException(
        $"bDcsResidual length {bDcsResidual.Length} != {total}.",
        nameof(bDcsResidual));

    // Group origin in *block* coordinates. libjxl: groupX/Y are pixel coords
    // and the block grid is groupX/8 × groupY/8.
    if ((groupX % 8) != 0)
      throw new ArgumentException(
        $"groupX {groupX} must be a multiple of 8 (block size).", nameof(groupX));
    if ((groupY % 8) != 0)
      throw new ArgumentException(
        $"groupY {groupY} must be a multiple of 8 (block size).", nameof(groupY));
    var groupBlockX0 = groupX / 8;
    var groupBlockY0 = groupY / 8;

    for (var by = 0; by < groupBlocksHigh; ++by) {
      var absBlockY = groupBlockY0 + by;
      var tileY = absBlockY / JxlColorCorrelationMap.ColorTileDimInBlocks;
      if (tileY >= cmap.TilesHigh)
        throw new ArgumentOutOfRangeException(nameof(cmap),
          $"Block row {absBlockY} maps to tileY={tileY} but cmap.TilesHigh={cmap.TilesHigh}.");

      for (var bx = 0; bx < groupBlocksWide; ++bx) {
        var absBlockX = groupBlockX0 + bx;
        var tileX = absBlockX / JxlColorCorrelationMap.ColorTileDimInBlocks;
        if (tileX >= cmap.TilesWide)
          throw new ArgumentOutOfRangeException(nameof(cmap),
            $"Block col {absBlockX} maps to tileX={tileX} but cmap.TilesWide={cmap.TilesWide}.");

        var tileIdx = tileY * cmap.TilesWide + tileX;
        int factorX = cmap.CmapX[tileIdx];
        int factorB = cmap.CmapY[tileIdx];

        var blockIdx = by * groupBlocksWide + bx;
        var y = yDcs[blockIdx];

        // Same per-task simplified formula as ApplyCorrection: factor / 128
        // with signed arithmetic-shift-right-7. When y == 0 (and the default
        // residual encoding is 0 for fully-zero images), the result equals
        // the input residual.
        if (factorX != 0 && y != 0) {
          var add = (factorX * y) >> 7;
          xDcsResidual[blockIdx] = _ClampToShort(xDcsResidual[blockIdx] + add);
        }
        if (factorB != 0 && y != 0) {
          var add = (factorB * y) >> 7;
          bDcsResidual[blockIdx] = _ClampToShort(bDcsResidual[blockIdx] + add);
        }
      }
    }
  }

  /// <summary>
  /// Compute the DC bucket index for one decoded DC value, used as one of the
  /// inputs to <see cref="JxlBlockContextMap.GetContext"/> when selecting the
  /// AC entropy context for a block.
  ///
  /// <para>libjxl: <c>BlockCtxMap::DCContext</c> in
  /// <c>lib/jxl/ac_context.h</c>. The bucket is the count of thresholds the
  /// signed DC value strictly exceeds, so <c>thresholds = []</c> always yields
  /// 0 (matching the default block context map's <c>num_dc_ctxs = 1</c>).</para>
  ///
  /// <para>Convention (libjxl): thresholds are sorted ascending; the bucket
  /// index is the upper-bound position (i.e. <c>0..thresholds.Length</c>).</para>
  /// </summary>
  /// <param name="dc">Decoded DC value for one block.</param>
  /// <param name="dcThresholds">Per-channel DC thresholds previously decoded
  /// by <see cref="JxlBlockContextMap"/>. Empty => single-bucket (always 0).</param>
  public static int DcBucketIndex(short dc, int[] dcThresholds) {
    ArgumentNullException.ThrowIfNull(dcThresholds);
    if (dcThresholds.Length == 0)
      return 0;

    // libjxl's DCContext compares the signed DC against signed thresholds and
    // counts how many it strictly exceeds. We mirror that with a linear scan
    // (threshold counts are ≤ 15 in practice — bounded by the 4-bit count
    // field in DecodeBlockCtxMap).
    var bucket = 0;
    for (var i = 0; i < dcThresholds.Length; ++i)
      if (dc > dcThresholds[i])
        ++bucket;
    return bucket;
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  private static short _ClampToShort(int value) {
    if (value < short.MinValue) return short.MinValue;
    if (value > short.MaxValue) return short.MaxValue;
    return (short)value;
  }
}
