using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// JPEG XL Color-Correlation Map (a.k.a. Chroma-from-Luma / CfL).
//
// libjxl reference (BSD-3-Clause):
//   - lib/jxl/chroma_from_luma.h    (struct ColorCorrelation, ColorCorrelationMap)
//   - lib/jxl/chroma_from_luma.cc   (DecodeDC, Create — zero-fills the maps)
//   - lib/jxl/dec_modular.cc        (DecodeAcMetadata: channel 0 = ytox_map,
//                                    channel 1 = ytob_map, modular sub-images)
//   - lib/jxl/dec_group.cc          (per-block lookup: cmap.ytox_map.ConstRow(ty)
//                                    indexed by abs_tx, then YtoXRatio / YtoBRatio)
//
// CONCEPTS:
//   * "Tile" = a kColorTileDim × kColorTileDim pixel rectangle (libjxl: 64×64).
//     With 8×8 blocks, that is kColorTileDimInBlocks = 8 blocks per tile side.
//   * Each tile stores TWO signed 8-bit scalars: ytox (Y→X) and ytob (Y→B).
//   * Default factor is 0 (no correction), and ColorCorrelationMap::Create
//     ZeroFillImage's both maps. So a freshly-allocated cmap is a no-op.
//
// COLOR CORRELATION MATH (libjxl chroma_from_luma.h):
//   YtoXRatio(factor) = base_correlation_x + factor * color_scale
//   color_scale       = 1 / color_factor          (default color_factor = 84)
//   base_correlation_x = 0,  base_correlation_b = kYToBRatio (XYB) or 0 (RGB)
//
// CORRECTION (libjxl dec_group.cc, AC dequant path):
//   x_dequant_i = x_quant_i + YtoXRatio(cmap_x[tile]) * y_quant_i
//   b_dequant_i = b_quant_i + YtoBRatio(cmap_y[tile]) * y_quant_i
//
// FIRST-WAVE SIMPLIFICATION (per task spec):
//   The full ColorCorrelation struct (color_factor, base correlations, DC bits)
//   is decoded by JxlFrameDecoder and is not modelled here. The task asks for
//   the simplified per-block formula `factor / 128` (signed). When all factors
//   are 0 (the most common case in real bitstreams), the simplified formula
//   and the full libjxl formula both produce identity (no correction), so the
//   integration path is functionally correct for the no-correlation case.
// =====================================================================================

/// <summary>
/// JPEG XL Color-Correlation Map (CfL). A pair of signed 8-bit per-tile factors
/// describing how much luma (Y) leaks into the chroma channels (X, B) so the
/// decoder can subtract that prediction from the residual AC coefficients.
///
/// <para>Tiles are <see cref="ColorTileDim"/>×<see cref="ColorTileDim"/> pixels
/// (= <see cref="ColorTileDimInBlocks"/>×<see cref="ColorTileDimInBlocks"/>
/// blocks, since blocks are 8×8 pixels).</para>
///
/// <para>libjxl: <c>struct ColorCorrelationMap</c> in
/// <c>lib/jxl/chroma_from_luma.h</c> + <c>ModularFrameDecoder::DecodeAcMetadata</c>
/// in <c>lib/jxl/dec_modular.cc</c> (channel 0 = ytox_map, channel 1 = ytob_map).</para>
/// </summary>
internal sealed class JxlColorCorrelationMap {

  /// <summary>libjxl <c>kColorTileDim</c> from <c>chroma_from_luma.h</c> = 64
  /// pixels per tile side.</summary>
  public const int ColorTileDim = 64;

  /// <summary>libjxl <c>kColorTileDimInBlocks</c> = 8 blocks per tile side
  /// (kColorTileDim / kBlockDim = 64 / 8 = 8).</summary>
  public const int ColorTileDimInBlocks = 8;

  /// <summary>libjxl <c>kDefaultColorFactor</c> = 84. The CfL denominator that
  /// turns raw signed-byte factors into floating-point ratios. Provided as a
  /// constant for callers that want to apply the full libjxl formula instead
  /// of the simplified per-task /128 formula used by
  /// <see cref="ApplyCorrection"/>.</summary>
  public const int DefaultColorFactor = 84;

  /// <summary>Per-tile signed 8-bit Y→X factor (libjxl <c>ytox_map</c>),
  /// indexed <c>[tileY * TilesWide + tileX]</c>. Default = 0 (no correction).</summary>
  public required sbyte[] CmapX { get; init; }

  /// <summary>Per-tile signed 8-bit Y→B factor (libjxl <c>ytob_map</c>),
  /// indexed <c>[tileY * TilesWide + tileX]</c>. Default = 0 (no correction).</summary>
  public required sbyte[] CmapY { get; init; }

  /// <summary>Number of cmap tiles across the image width
  /// (<c>ceil(width / ColorTileDim)</c>).</summary>
  public int TilesWide { get; init; }

  /// <summary>Number of cmap tiles down the image height
  /// (<c>ceil(height / ColorTileDim)</c>).</summary>
  public int TilesHigh { get; init; }

  /// <summary>libjxl <c>cms::kYToBRatio = 1.0f</c> from
  /// <c>cms/opsin_params.h</c>. Default base for the Y→B correlation; the per-DC
  /// addend <c>YtoBRatio(ytob_dc)</c> is added on top.</summary>
  public const float DefaultYtoBRatio = 1.0f;

  /// <summary>The pair of DC-level CfL factors (libjxl
  /// <c>ColorCorrelation::DCFactors()</c>): <c>[YtoX_dc, 0, YtoB_dc, 0]</c>
  /// stored in canonical XYB order. Index 0 is the Y→X correction applied
  /// during DC dequant, index 2 is Y→B; indices 1 and 3 are unused (libjxl
  /// keeps them as zero placeholders for the SIMD store).</summary>
  public readonly record struct DcCorrelationFactors(float YtoX, float YtoB);

  /// <summary>libjxl <c>ColorCorrelation::DecodeDC</c>: reads the 1-bit
  /// <c>all_default</c> flag and, when 0, the four DC correlation parameters
  /// (color factor, base X/B correlations, ytox/ytob DC offsets). Returns the
  /// per-frame DC CfL factors used by <c>DequantDC</c>:
  /// <list type="bullet">
  ///   <item><c>YtoX = base_correlation_x + ytox_dc / color_factor</c></item>
  ///   <item><c>YtoB = base_correlation_b + ytob_dc / color_factor</c></item>
  /// </list>
  /// Defaults: <c>color_factor=84</c>, <c>base_correlation_x=0</c>,
  /// <c>base_correlation_b=kYToBRatio=1.0</c>, both <c>ytox/ytob_dc=0</c> →
  /// <c>YtoX=0</c>, <c>YtoB=1.0</c>.</summary>
  public static DcCorrelationFactors DecodeDc(JxlBitReader r) {
    ArgumentNullException.ThrowIfNull(r);
    var allDefault = r.ReadBool();
    if (allDefault)
      return new DcCorrelationFactors(YtoX: 0f, YtoB: DefaultYtoBRatio);

    var colorFactor = r.ReadU32(84, 0, 256, 0, 2, 0, 0, 8); // color_factor
    if (colorFactor == 0)
      throw new System.IO.InvalidDataException("ColorCorrelation.DecodeDc: color_factor must be non-zero.");
    var baseCorrelationX = _ReadF16(r);
    var baseCorrelationB = _ReadF16(r);
    var ytoxDc = (sbyte)((int)r.ReadBits(8) - 128); // signed offset from -128
    var ytobDc = (sbyte)((int)r.ReadBits(8) - 128);
    var colorScale = 1f / colorFactor;
    return new DcCorrelationFactors(
      YtoX: baseCorrelationX + ytoxDc * colorScale,
      YtoB: baseCorrelationB + ytobDc * colorScale);
  }

  /// <summary>Half-precision F16 reader (mirror of <c>JxlFrameQuantizer._ReadF16</c>).
  /// Inlined here to keep <see cref="JxlColorCorrelationMap"/> self-contained.</summary>
  private static float _ReadF16(JxlBitReader r) {
    var bits = (ushort)r.ReadBits(16);
    var sign = (bits >> 15) & 1;
    var exp = (bits >> 10) & 0x1F;
    var frac = bits & 0x3FF;
    if (exp == 0) return sign != 0 ? -0f : 0f;
    if (exp == 31) return frac == 0 ? (sign != 0 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
    var mantissa = 1.0f + frac / 1024.0f;
    var value = mantissa * MathF.Pow(2.0f, exp - 15);
    return sign != 0 ? -value : value;
  }

  /// <summary>
  /// Construct a no-correction cmap of the given pixel dimensions. Equivalent
  /// to libjxl's <c>ColorCorrelationMap::Create</c> followed by
  /// <c>ZeroFillImage</c> on both maps.
  /// </summary>
  /// <param name="widthPixels">Image width in pixels. Tile count is
  /// <c>ceil(widthPixels / 64)</c>.</param>
  /// <param name="heightPixels">Image height in pixels. Tile count is
  /// <c>ceil(heightPixels / 64)</c>.</param>
  public static JxlColorCorrelationMap CreateZero(int widthPixels, int heightPixels) {
    if (widthPixels < 0)
      throw new ArgumentOutOfRangeException(nameof(widthPixels), "Must be >= 0.");
    if (heightPixels < 0)
      throw new ArgumentOutOfRangeException(nameof(heightPixels), "Must be >= 0.");

    var tilesWide = (widthPixels + ColorTileDim - 1) / ColorTileDim;
    var tilesHigh = (heightPixels + ColorTileDim - 1) / ColorTileDim;
    var count = tilesWide * tilesHigh;
    return new JxlColorCorrelationMap {
      CmapX = new sbyte[count],
      CmapY = new sbyte[count],
      TilesWide = tilesWide,
      TilesHigh = tilesHigh,
    };
  }

  /// <summary>
  /// Read <c>cmap_x</c> and <c>cmap_y</c> from previously-decoded modular
  /// sub-image planes. libjxl: <c>dec_modular.cc DecodeAcMetadata</c> reads
  /// channel 0 = <c>ytox_map</c> and channel 1 = <c>ytob_map</c> as 32-bit
  /// signed modular planes; we clamp them to <c>sbyte</c> range to match the
  /// <c>ImageSB</c> destination type.
  /// </summary>
  /// <param name="cmapXPlane">Raw modular channel-0 values, length = tilesWide*tilesHigh.</param>
  /// <param name="cmapYPlane">Raw modular channel-1 values, length = tilesWide*tilesHigh.</param>
  /// <param name="tilesWide">Number of tiles across.</param>
  /// <param name="tilesHigh">Number of tiles down.</param>
  public static JxlColorCorrelationMap FromModularChannels(
    int[] cmapXPlane, int[] cmapYPlane, int tilesWide, int tilesHigh
  ) {
    ArgumentNullException.ThrowIfNull(cmapXPlane);
    ArgumentNullException.ThrowIfNull(cmapYPlane);
    if (tilesWide < 0)
      throw new ArgumentOutOfRangeException(nameof(tilesWide), "Must be >= 0.");
    if (tilesHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(tilesHigh), "Must be >= 0.");

    var expected = tilesWide * tilesHigh;
    if (cmapXPlane.Length != expected)
      throw new ArgumentException(
        $"cmapXPlane has length {cmapXPlane.Length}, expected {expected} (tilesWide * tilesHigh).",
        nameof(cmapXPlane));
    if (cmapYPlane.Length != expected)
      throw new ArgumentException(
        $"cmapYPlane has length {cmapYPlane.Length}, expected {expected} (tilesWide * tilesHigh).",
        nameof(cmapYPlane));

    var x = new sbyte[expected];
    var y = new sbyte[expected];
    for (var i = 0; i < expected; ++i) {
      x[i] = _ClampToSByte(cmapXPlane[i]);
      y[i] = _ClampToSByte(cmapYPlane[i]);
    }

    return new JxlColorCorrelationMap {
      CmapX = x,
      CmapY = y,
      TilesWide = tilesWide,
      TilesHigh = tilesHigh,
    };
  }

  /// <summary>
  /// Look up the cmap tile index covering the block at block-grid coordinates
  /// (<paramref name="blockX"/>, <paramref name="blockY"/>). Each tile spans
  /// <see cref="ColorTileDimInBlocks"/> blocks per side, so
  /// <c>tileX = blockX / 8</c>. libjxl: <c>const size_t ty = (block_rect.y0() + by) / kColorTileDimInBlocks;</c>
  /// in <c>dec_group.cc</c>.
  /// </summary>
  public int GetTileIndex(int blockX, int blockY) {
    if (blockX < 0)
      throw new ArgumentOutOfRangeException(nameof(blockX), "Must be >= 0.");
    if (blockY < 0)
      throw new ArgumentOutOfRangeException(nameof(blockY), "Must be >= 0.");

    var tileX = blockX / ColorTileDimInBlocks;
    var tileY = blockY / ColorTileDimInBlocks;
    if (tileX >= this.TilesWide)
      throw new ArgumentOutOfRangeException(nameof(blockX),
        $"blockX={blockX} maps to tileX={tileX} but TilesWide={this.TilesWide}.");
    if (tileY >= this.TilesHigh)
      throw new ArgumentOutOfRangeException(nameof(blockY),
        $"blockY={blockY} maps to tileY={tileY} but TilesHigh={this.TilesHigh}.");

    return tileY * this.TilesWide + tileX;
  }

  /// <summary>
  /// Apply the per-block color-correlation correction to the AC coefficients
  /// of the X and B channels using the previously-decoded Y channel. The
  /// correction follows the simplified per-task formula
  /// <c>x_corrected[i] = x[i] + (cmap_x[tile] * y[i]) / 128</c> and
  /// <c>b_corrected[i] = b[i] + (cmap_y[tile] * y[i]) / 128</c>, which is the
  /// CfL un-mixing step described in libjxl <c>dec_group.cc</c> with the
  /// libjxl color_factor approximated by 128 (the actual default is 84 — see
  /// <see cref="DefaultColorFactor"/>).
  ///
  /// <para>Channel ordering matches the rest of the codec:
  /// <c>perChannelBlocks[(int)JxlVarDctChannel.X]</c>,
  /// <c>perChannelBlocks[(int)JxlVarDctChannel.Y]</c>,
  /// <c>perChannelBlocks[(int)JxlVarDctChannel.B]</c>.</para>
  ///
  /// <para>When all cmap factors are 0 (the default / no-correlation case),
  /// this method is a no-op and returns without touching coefficients —
  /// equivalent to libjxl's behaviour for the zero-filled default cmap.</para>
  /// </summary>
  /// <param name="perChannelBlocks">Per-channel array of AC blocks, indexed
  /// [channel][block]. Block array is in row-major order, length =
  /// <paramref name="blocksWide"/> * <paramref name="blocksHigh"/>.</param>
  /// <param name="blocksWide">Image width in 8×8 blocks.</param>
  /// <param name="blocksHigh">Image height in 8×8 blocks.</param>
  public void ApplyCorrection(
    JxlDctBlock[][] perChannelBlocks,
    int blocksWide, int blocksHigh
  ) {
    ArgumentNullException.ThrowIfNull(perChannelBlocks);
    if (perChannelBlocks.Length < 3)
      throw new ArgumentException(
        $"perChannelBlocks must have at least 3 channels (X, Y, B); got {perChannelBlocks.Length}.",
        nameof(perChannelBlocks));
    if (blocksWide < 0)
      throw new ArgumentOutOfRangeException(nameof(blocksWide), "Must be >= 0.");
    if (blocksHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(blocksHigh), "Must be >= 0.");

    var totalBlocks = blocksWide * blocksHigh;
    var xBlocks = perChannelBlocks[(int)JxlVarDctChannel.X];
    var yBlocks = perChannelBlocks[(int)JxlVarDctChannel.Y];
    var bBlocks = perChannelBlocks[(int)JxlVarDctChannel.B];
    ArgumentNullException.ThrowIfNull(xBlocks);
    ArgumentNullException.ThrowIfNull(yBlocks);
    ArgumentNullException.ThrowIfNull(bBlocks);
    if (xBlocks.Length != totalBlocks || yBlocks.Length != totalBlocks || bBlocks.Length != totalBlocks)
      throw new ArgumentException(
        $"Per-channel block array length must equal blocksWide*blocksHigh ({totalBlocks}); got " +
        $"X={xBlocks.Length}, Y={yBlocks.Length}, B={bBlocks.Length}.",
        nameof(perChannelBlocks));

    // Fast-path: all-zero cmap (the most common default case) is a guaranteed no-op.
    if (_AllZero(this.CmapX) && _AllZero(this.CmapY))
      return;

    for (var by = 0; by < blocksHigh; ++by) {
      var tileY = by / ColorTileDimInBlocks;
      if (tileY >= this.TilesHigh)
        throw new InvalidOperationException(
          $"Block row {by} maps to tileY={tileY} but TilesHigh={this.TilesHigh}.");
      for (var bx = 0; bx < blocksWide; ++bx) {
        var tileX = bx / ColorTileDimInBlocks;
        if (tileX >= this.TilesWide)
          throw new InvalidOperationException(
            $"Block col {bx} maps to tileX={tileX} but TilesWide={this.TilesWide}.");

        var tileIdx = tileY * this.TilesWide + tileX;
        int factorX = this.CmapX[tileIdx];
        int factorB = this.CmapY[tileIdx];
        if (factorX == 0 && factorB == 0)
          continue; // no correction for this tile

        var blockIdx = by * blocksWide + bx;
        var xb = xBlocks[blockIdx];
        var yb = yBlocks[blockIdx];
        var bb = bBlocks[blockIdx];
        ArgumentNullException.ThrowIfNull(xb);
        ArgumentNullException.ThrowIfNull(yb);
        ArgumentNullException.ThrowIfNull(bb);

        var xCoeffs = xb.Coefficients;
        var yCoeffs = yb.Coefficients;
        var bCoeffs = bb.Coefficients;
        ArgumentNullException.ThrowIfNull(xCoeffs);
        ArgumentNullException.ThrowIfNull(yCoeffs);
        ArgumentNullException.ThrowIfNull(bCoeffs);
        var n = xCoeffs.Length;
        if (yCoeffs.Length != n || bCoeffs.Length != n)
          throw new InvalidOperationException(
            $"At block ({bx},{by}): per-channel coefficient arrays must match length; " +
            $"got X={n}, Y={yCoeffs.Length}, B={bCoeffs.Length}.");

        // x_corrected = x + (factorX * y) / 128
        // b_corrected = b + (factorB * y) / 128
        // Use signed integer arithmetic with arithmetic-shift-right-7 to match
        // a /128 with proper sign propagation. Result is clamped to short.
        if (factorX != 0) {
          for (var i = 0; i < n; ++i) {
            var prod = factorX * yCoeffs[i];
            var add = prod >> 7; // signed /128
            xCoeffs[i] = _ClampToShort(xCoeffs[i] + add);
          }
        }
        if (factorB != 0) {
          for (var i = 0; i < n; ++i) {
            var prod = factorB * yCoeffs[i];
            var add = prod >> 7; // signed /128
            bCoeffs[i] = _ClampToShort(bCoeffs[i] + add);
          }
        }
      }
    }
  }

  /// <summary>
  /// Compute the floating-point Y→X ratio for a given signed factor using the
  /// full libjxl formula (<c>factor / kDefaultColorFactor</c>). Provided for
  /// callers that want libjxl-bit-exact dequantization rather than the
  /// simplified <c>/128</c> path used by <see cref="ApplyCorrection"/>.
  /// libjxl: <c>ColorCorrelation::YtoXRatio</c>.
  /// </summary>
  public static float YtoXRatio(sbyte factor)
    => factor / (float)DefaultColorFactor;

  /// <summary>
  /// Compute the floating-point Y→B ratio for a given signed factor using the
  /// full libjxl formula (<c>factor / kDefaultColorFactor</c> — note that the
  /// real libjxl adds <c>kYToBRatio</c> as a base for XYB frames; that base
  /// is not modelled here and must be applied by the caller if needed).
  /// libjxl: <c>ColorCorrelation::YtoBRatio</c>.
  /// </summary>
  public static float YtoBRatio(sbyte factor)
    => factor / (float)DefaultColorFactor;

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  private static bool _AllZero(sbyte[] values) {
    for (var i = 0; i < values.Length; ++i)
      if (values[i] != 0)
        return false;
    return true;
  }

  private static sbyte _ClampToSByte(int value) {
    if (value < sbyte.MinValue) return sbyte.MinValue;
    if (value > sbyte.MaxValue) return sbyte.MaxValue;
    return (sbyte)value;
  }

  private static short _ClampToShort(int value) {
    if (value < short.MinValue) return short.MinValue;
    if (value > short.MaxValue) return short.MaxValue;
    return (short)value;
  }
}
