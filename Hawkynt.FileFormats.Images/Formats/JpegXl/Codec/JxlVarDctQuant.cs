using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// VarDCT quantization helpers (ISO/IEC 18181-1 §G.6, libjxl
// `lib/jxl/quant_weights.cc` / `quant_weights.h`).
//
// JPEG XL VarDCT uses one of 4 selectable quantization tables per channel; the
// default DCT8 table is the spec's predefined "library" preset. This file
// reproduces libjxl's `kDefaultQuantWeights` for DCT8x8 verbatim — these are
// frozen, spec-fixed constants.
//
// First-wave scope: DCT8x8 only. Larger DCT shapes (16x16, 32x32, …) and the
// non-DCT block strategies (Identity, DCT2, AFV) are TODO.
// =====================================================================================

internal static class JxlVarDctQuant {

  // -------------------------------------------------------------------------
  // Default DCT8x8 quant weights — directly transcribed from libjxl
  // `DequantMatricesLibraryDef::DCT()` in `lib/jxl/quant_weights.cc`.
  //
  // libjxl stores 6 "distance band" parameters per channel (X, Y, B); the
  // 8x8 quant matrix is generated from them at table-build time via
  // `GetQuantWeights(8, 8, …)`. We reproduce that generation at C# class-load
  // time so the resulting 8x8 weight tables are bitwise-identical to the
  // libjxl runtime values.
  //
  // Distance bands per channel (X, Y, B) — DCT8 default:
  //   X: { 3150.0, 0.0,  -0.4, -0.4, -0.4, -2.0 }
  //   Y: {  560.0, 0.0,  -0.3, -0.3, -0.3, -0.3 }
  //   B: {  512.0, -2.0, -1.0,  0.0, -1.0, -2.0 }
  // -------------------------------------------------------------------------

  private static readonly float[][] _kDefaultDct8DistanceBands = new[] {
    new float[] { 3150.0f, 0.0f, -0.4f, -0.4f, -0.4f, -2.0f },
    new float[] {  560.0f, 0.0f, -0.3f, -0.3f, -0.3f, -0.3f },
    new float[] {  512.0f, -2.0f, -1.0f, 0.0f, -1.0f, -2.0f },
  };

  /// <summary>Cached 8x8 dequantization weight tables per channel, built once
  /// from the distance bands using the libjxl `GetQuantWeights` algorithm.</summary>
  private static readonly float[][] _kDefaultDct8Weights = _BuildDefaultDct8Weights();

  /// <summary>libjxl's `Mult`: maps a distance-band parameter to a multiplicative
  /// step. From `quant_weights.cc`: `Mult(v) = v > 0 ? 1+v : 1/(1-v)`.</summary>
  private static float _Mult(float v) => v > 0.0f ? 1.0f + v : 1.0f / (1.0f - v);

  /// <summary>libjxl's `InterpolateVec` (the path actually taken at runtime):
  /// `scaled_pos` is already pre-scaled by `(num_bands-1) / kSqrt2` via the
  /// caller's `rcpcol`/`rcprow`, so the sample index is simply `floor(scaled_pos)`.
  /// Returns `a * (b/a)^frac` with `a = array[idx], b = array[idx+1]`.</summary>
  private static float _InterpolateVec(float scaledPos, float[] array, int len) {
    var idx = (int)scaledPos;
    if (idx < 0) idx = 0;
    if (idx >= len - 1) idx = len - 2;
    var a = array[idx];
    var b = array[idx + 1];
    var frac = scaledPos - idx;
    return a * MathF.Pow(b / a, frac);
  }

  /// <summary>Build the 8x8 weight table for one channel from its 6 distance
  /// bands. Directly mirrors `GetQuantWeights(8, 8, distance_bands, 6, …)`
  /// in `lib/jxl/quant_weights.cc`.</summary>
  private static float[] _BuildOneDct8Channel(float[] distanceBands) {
    const int rows = 8;
    const int cols = 8;
    const int numBands = 6;
    const float kAlmostZero = 1e-8f;
    const float kSqrt2 = 1.4142135623730951f;

    // Compose absolute band values (libjxl: bands[0] is direct, the rest
    // multiply by Mult(distance_bands[c][i])).
    var bands = new float[numBands];
    bands[0] = distanceBands[0];
    if (bands[0] < kAlmostZero)
      throw new InvalidOperationException("DCT8 band[0] too small.");
    for (var i = 1; i < numBands; i++) {
      bands[i] = bands[i - 1] * _Mult(distanceBands[i]);
      if (bands[i] < kAlmostZero)
        throw new InvalidOperationException($"DCT8 band[{i}] too small.");
    }

    var scale = (numBands - 1) / (kSqrt2 + 1e-6f);
    var rcpcol = scale / (cols - 1);
    var rcprow = scale / (rows - 1);

    var inverseWeights = new float[rows * cols];
    for (var y = 0; y < rows; y++) {
      var dy = y * rcprow;
      var dy2 = dy * dy;
      for (var x = 0; x < cols; x++) {
        var dx = x * rcpcol;
        var scaledDistance = MathF.Sqrt(dx * dx + dy2);
        // libjxl's `InterpolateVec` takes the already-pre-scaled distance
        // directly as the band-index position.
        var weight = _InterpolateVec(scaledDistance, bands, numBands);
        inverseWeights[y * cols + x] = weight;
      }
    }

    // libjxl stores the inverse of the visible weight (1 / inv_table = table),
    // and the dequantization step in `DequantizeBlock` is
    // `pixel = coeff * (1 / inv_quant)`; the public weight returned to callers
    // here is the dequant multiplier (1 / inv_table).
    var weights = new float[rows * cols];
    for (var i = 0; i < rows * cols; i++) {
      var inv = inverseWeights[i];
      if (inv < kAlmostZero || inv > 1.0f / kAlmostZero)
        throw new InvalidOperationException("DCT8 weight out of range.");
      weights[i] = 1.0f / inv;
    }
    return weights;
  }

  private static float[][] _BuildDefaultDct8Weights() {
    var result = new float[3][];
    for (var c = 0; c < 3; c++)
      result[c] = _BuildOneDct8Channel(_kDefaultDct8DistanceBands[c]);
    return result;
  }

  /// <summary>Default quantization weights for DCT8x8 per channel from libjxl
  /// (kDefaultQuantWeights in lib/jxl/quant_weights.cc). Returns a 8x8 float
  /// array with weights in scan order; multiply each AC coefficient by its
  /// weight to dequantize. Channel index: 0=X, 1=Y, 2=B (XYB).</summary>
  public static JxlQuantTable DefaultDct8x8(int channel) {
    if (channel < 0 || channel > 2)
      throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be 0 (X), 1 (Y), or 2 (B).");

    var src = _kDefaultDct8Weights[channel];
    var copy = new float[src.Length];
    Array.Copy(src, copy, src.Length);
    return new JxlQuantTable {
      Width = 8,
      Height = 8,
      Weights = copy,
    };
  }

  /// <summary>Build all 3 default DCT8x8 tables in one struct.</summary>
  public static JxlQuantTableSet DefaultTableSetXyb() {
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; c++)
      tables[c] = DefaultDct8x8(c);
    return new JxlQuantTableSet { Tables = tables };
  }

  /// <summary>
  /// Generate a quant table at arbitrary block dimensions from per-channel
  /// distance bands. Mirrors libjxl's <c>GetQuantWeights</c> in
  /// <c>quant_weights.cc</c>: each output cell is sampled along the diagonal
  /// using <c>InterpolateVec</c> over the absolute band values, with the
  /// band index pre-scaled by <c>(numBands - 1) / sqrt(2)</c>.
  /// </summary>
  /// <param name="rows">Block height in samples.</param>
  /// <param name="cols">Block width in samples.</param>
  /// <param name="distanceBands">Per-band parameters; first is the absolute
  /// scaler, the rest are `Mult` deltas that compose multiplicatively.</param>
  public static JxlQuantTable BuildFromDistanceBands(int rows, int cols, float[] distanceBands) {
    var inv = _BandValues(rows, cols, distanceBands);

    const float kAlmostZero = 1e-8f;
    var weights = new float[rows * cols];
    for (var i = 0; i < rows * cols; i++) {
      var v = inv[i];
      if (v < kAlmostZero || v > 1.0f / kAlmostZero)
        throw new InvalidOperationException("Quant weight out of range.");
      weights[i] = 1.0f / v;
    }
    return new JxlQuantTable { Width = cols, Height = rows, Weights = weights };
  }

  /// <summary>
  /// The curve itself, before it is inverted into dequantisation multipliers.
  /// The AFV shape is three curves laid into one block and needs them in this
  /// form; every other shape inverts them straight away.
  /// </summary>
  private static float[] _BandValues(int rows, int cols, float[] distanceBands) {
    if (rows <= 0 || cols <= 0)
      throw new ArgumentOutOfRangeException(nameof(rows));
    if (distanceBands is null)
      throw new ArgumentNullException(nameof(distanceBands));
    if (distanceBands.Length < 1)
      throw new ArgumentException("Need at least one distance band.", nameof(distanceBands));

    const float kAlmostZero = 1e-8f;
    const float kSqrt2 = 1.4142135623730951f;

    var numBands = distanceBands.Length;
    var bands = new float[numBands];
    bands[0] = distanceBands[0];
    if (bands[0] < kAlmostZero)
      throw new InvalidOperationException("Quant band[0] too small.");
    for (var i = 1; i < numBands; i++) {
      bands[i] = bands[i - 1] * _Mult(distanceBands[i]);
      if (bands[i] < kAlmostZero)
        throw new InvalidOperationException($"Quant band[{i}] too small.");
    }

    var scale = (numBands - 1) / (kSqrt2 + 1e-6f);
    var rcpcol = cols > 1 ? scale / (cols - 1) : 0.0f;
    var rcprow = rows > 1 ? scale / (rows - 1) : 0.0f;

    var inv = new float[rows * cols];
    for (var y = 0; y < rows; y++) {
      var dy = y * rcprow;
      var dy2 = dy * dy;
      for (var x = 0; x < cols; x++) {
        var dx = x * rcpcol;
        var d = MathF.Sqrt(dx * dx + dy2);
        inv[y * cols + x] = _InterpolateVec(d, bands, numBands);
      }
    }

    return inv;
  }

  /// <summary>
  /// Build a quant table set for an arbitrary AC strategy using libjxl's
  /// per-strategy default distance bands. Returns null when the strategy is
  /// not yet implemented (in which case the caller should fall back to
  /// DCT8x8 defaults).
  /// </summary>
  /// <remarks>
  /// libjxl `quant_weights.cc::DequantMatricesLibraryDef` defines distance
  /// bands per-strategy and per-channel. This implementation supports the
  /// most common strategies; rare ones (DCT64+, AFV, Hornuss) fall through
  /// to null. The dimensions match libjxl's `required_size_x[t] * 8` and
  /// `required_size_y[t] * 8`.
  /// </remarks>
  public static JxlQuantTableSet? DefaultsForStrategy(JxlAcStrategyType strategy) {
    // Two shapes state their weights outright rather than as a curve over
    // distance, because neither is a plain transform: the Hornuss shape carries
    // one value over the whole block with three corners of its own, and the 2x2
    // one is four nested squares. Both were being dequantised with the 8x8
    // curve, which is a different set of numbers entirely.
    switch (strategy) {
      case JxlAcStrategyType.Hornuss: return _BuildFromLayout(_HornussWeights, _LayOutHornuss);
      case JxlAcStrategyType.Dct2x2: return _BuildFromLayout(_Dct2Weights, _LayOutDct2);
      case JxlAcStrategyType.Afv0:
      case JxlAcStrategyType.Afv1:
      case JxlAcStrategyType.Afv2:
      case JxlAcStrategyType.Afv3:
        // All four are the same shape turned about, and the format gives them
        // one table between them.
        return _BuildAfv();
    }

    var bands = _DefaultBandsForStrategy(strategy);
    if (bands is null) return null;
    // The weights are applied to the block as this decoder stores it, so they
    // have to be laid out the same way round. Taking the shape's name for that
    // laid every rectangle's curve down the wrong axis, which no square shape
    // could show.
    var (blockWidth, blockHeight) = JxlVarDctIdct.BlockSize(strategy);
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; c++)
      tables[c] = BuildFromDistanceBands(blockHeight, blockWidth, bands[c]);
    return new JxlQuantTableSet { Tables = tables };
  }

  /// <summary>libjxl per-strategy default distance bands. Sources:
  /// <c>quant_weights.cc::DequantMatricesLibraryDef::DCT/IDENTITY/DCT2X2/
  /// DCT4X4/DCT16X16/DCT32X32/DCT8X16/DCT8X32/DCT16X32/DCT4X8</c>.
  /// Returns null for strategies not yet covered.</summary>
  /// <summary>The curve the format states for a shape, or null where the shape
  /// states its weights outright instead.</summary>
  internal static float[][]? DistanceBandsForStrategy(JxlAcStrategyType strategy)
    => _DefaultBandsForStrategy(strategy);

  private static float[][]? _DefaultBandsForStrategy(JxlAcStrategyType s) => s switch {
    JxlAcStrategyType.Dct8x8 => _kDefaultDct8DistanceBands,
    JxlAcStrategyType.Dct16x16 => _kDct16Bands,
    JxlAcStrategyType.Dct32x32 => _kDct32Bands,
    JxlAcStrategyType.Dct8x16 or JxlAcStrategyType.Dct16x8 => _kDct16x8Bands,
    JxlAcStrategyType.Dct8x32 or JxlAcStrategyType.Dct32x8 => _kDct32x8Bands,
    JxlAcStrategyType.Dct16x32 or JxlAcStrategyType.Dct32x16 => _kDct32x16Bands,
    JxlAcStrategyType.Dct4x4 => _kDct4Bands,
    JxlAcStrategyType.Dct4x8 or JxlAcStrategyType.Dct8x4 => _kDct4x8Bands,
    JxlAcStrategyType.Dct64x64 => _kDct64Bands,
    JxlAcStrategyType.Dct64x32 or JxlAcStrategyType.Dct32x64 => _kDct64x32Bands,
    _ => null,
  };

  /// <summary>The AFV shape's own nine weights, per channel: the two beside the
  /// corner, the three of the corner itself, then four bands (libjxl
  /// <c>AFV0</c>).</summary>
  private static readonly float[][] _AfvWeights = [
    [3072.0f, 3072.0f, 256.0f, 256.0f, 256.0f, 414.0f, 0.0f, 0.0f, 0.0f],
    [1024.0f, 1024.0f, 50.0f, 50.0f, 50.0f, 58.0f, 0.0f, 0.0f, 0.0f],
    [384.0f, 384.0f, 12.0f, 12.0f, 12.0f, 22.0f, -0.25f, -0.25f, -0.25f],
  ];

  /// <summary>Where each of the AFV shape's own sixteen entries sits along the
  /// four bands. The four marked here are the corner, which is stated outright
  /// rather than interpolated.</summary>
  private static readonly float[] _AfvFrequencies = [
    _AfvStated, _AfvStated, 0.8517778890324296f, 5.37778436506804f,
    _AfvStated, _AfvStated, 4.734747904497923f, 5.449245381693219f,
    1.6598270267479331f, 4.0f, 7.275749096817861f, 10.423227632456525f,
    2.662932286148962f, 7.630657783650829f, 8.962388608184032f, 12.97166202570235f,
  ];

  private const float _AfvStated = 0xBAD;
  private const float _AfvLow = 0.8517778890324296f;
  private const float _AfvHigh = 12.97166202570235f - _AfvLow + 1e-6f;

  /// <summary>
  /// The AFV shape's weights (libjxl <c>ComputeQuantTable</c>'s AFV case). The
  /// block is three curves laid together: its odd rows are a 4x8, its even rows
  /// and odd columns a 4x4, and what is left is the shape's own — five entries
  /// stated outright at the corner and the rest read off four bands.
  /// </summary>
  private static JxlQuantTableSet _BuildAfv() {
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; ++c) {
      var w = _AfvWeights[c];
      var bands = new float[4];
      bands[0] = w[5];
      for (var i = 1; i < 4; ++i)
        bands[i] = bands[i - 1] * _Mult(w[i + 5]);

      var inverse = new float[64];
      inverse[0] = 1.0f; // never read: the lowest coefficient comes from the DC.
      inverse[1 * 8 + 0] = w[0];
      inverse[0 * 8 + 1] = w[1];
      inverse[2 * 8 + 0] = w[2];
      inverse[0 * 8 + 2] = w[3];
      inverse[2 * 8 + 2] = w[4];

      for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        if (x < 2 && y < 2)
          continue;

        var position = (_AfvFrequencies[y * 4 + x] - _AfvLow) * 3.0f / _AfvHigh;
        inverse[2 * y * 8 + 2 * x] = _InterpolateVec(position, bands, 4);
      }

      var band4x8 = _BandValues(4, 8, _kDct4x8Bands[c]);
      for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 8; ++x) {
        if (x == 0 && y == 0)
          continue;

        inverse[(2 * y + 1) * 8 + x] = band4x8[y * 8 + x];
      }

      var band4x4 = _BandValues(4, 4, _kDct4Bands[c]);
      for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x) {
        if (x == 0 && y == 0)
          continue;

        inverse[2 * y * 8 + 2 * x + 1] = band4x4[y * 4 + x];
      }

      // Laid out the way the format writes a block down; the weights are
      // applied to the block as this decoder stores it, which is the other way
      // round. The two corner entries either side of the diagonal hold the same
      // value, but the two curves laid into the rows and columns do not.
      var weights = new float[64];
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        weights[x * 8 + y] = 1.0f / inverse[y * 8 + x];

      tables[c] = new JxlQuantTable { Width = 8, Height = 8, Weights = weights };
    }

    return new JxlQuantTableSet { Tables = tables };
  }

  /// <summary>The Hornuss shape's weights: one over the block, then the two
  /// neighbours of the corner and the corner itself (libjxl <c>IDENTITY</c>).</summary>
  private static readonly float[][] _HornussWeights = [
    [280.0f, 3160.0f, 3160.0f],
    [60.0f, 864.0f, 864.0f],
    [18.0f, 200.0f, 200.0f],
  ];

  /// <summary>The 2x2 shape's weights, one per nested square.</summary>
  private static readonly float[][] _Dct2Weights = [
    [3840.0f, 2560.0f, 1280.0f, 640.0f, 480.0f, 300.0f],
    [960.0f, 640.0f, 320.0f, 180.0f, 140.0f, 120.0f],
    [640.0f, 320.0f, 128.0f, 64.0f, 32.0f, 16.0f],
  ];

  private static JxlQuantTableSet _BuildFromLayout(float[][] perChannel, Action<float[], float[]> layOut) {
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; ++c) {
      var inverse = new float[64];
      layOut(perChannel[c], inverse);
      var weights = new float[64];
      for (var i = 0; i < 64; ++i)
        weights[i] = 1.0f / inverse[i];
      tables[c] = new JxlQuantTable { Width = 8, Height = 8, Weights = weights };
    }

    return new JxlQuantTableSet { Tables = tables };
  }

  /// <summary>libjxl <c>GetQuantWeightsIdentity</c>.</summary>
  private static void _LayOutHornuss(float[] w, float[] inverse) {
    for (var i = 0; i < 64; ++i)
      inverse[i] = w[0];
    inverse[1] = w[1];
    inverse[8] = w[1];
    inverse[9] = w[2];
  }

  /// <summary>libjxl <c>GetQuantWeightsDCT2</c>: four nested squares.</summary>
  private static void _LayOutDct2(float[] w, float[] inverse) {
    // The corner is never read — the lowest coefficient comes from the DC
    // image — and libjxl parks a marker there. Kept so the two agree entry for
    // entry rather than only where it matters.
    inverse[0] = 0xBAD;
    inverse[1] = w[0];
    inverse[8] = w[0];
    inverse[9] = w[1];
    for (var y = 0; y < 2; ++y)
    for (var x = 0; x < 2; ++x) {
      inverse[y * 8 + x + 2] = w[2];
      inverse[(y + 2) * 8 + x] = w[2];
    }

    for (var y = 0; y < 2; ++y)
    for (var x = 0; x < 2; ++x)
      inverse[(y + 2) * 8 + x + 2] = w[3];

    for (var y = 0; y < 4; ++y)
    for (var x = 0; x < 4; ++x) {
      inverse[y * 8 + x + 4] = w[4];
      inverse[(y + 4) * 8 + x] = w[4];
    }

    for (var y = 0; y < 4; ++y)
    for (var x = 0; x < 4; ++x)
      inverse[(y + 4) * 8 + x + 4] = w[5];
  }

  /// <summary>Return (width, height) in samples for a given AC strategy.</summary>
  private static (int W, int H) _BlockDimsForStrategy(JxlAcStrategyType s) => s switch {
    JxlAcStrategyType.Dct8x8 => (8, 8),
    JxlAcStrategyType.Dct16x16 => (16, 16),
    JxlAcStrategyType.Dct32x32 => (32, 32),
    JxlAcStrategyType.Dct8x16 => (8, 16),
    JxlAcStrategyType.Dct16x8 => (16, 8),
    JxlAcStrategyType.Dct8x32 => (8, 32),
    JxlAcStrategyType.Dct32x8 => (32, 8),
    JxlAcStrategyType.Dct16x32 => (16, 32),
    JxlAcStrategyType.Dct32x16 => (32, 16),
    JxlAcStrategyType.Dct4x4 => (4, 4),
    JxlAcStrategyType.Dct4x8 => (4, 8),
    JxlAcStrategyType.Dct8x4 => (8, 4),
    JxlAcStrategyType.Dct64x64 => (64, 64),
    JxlAcStrategyType.Dct64x32 => (64, 32),
    JxlAcStrategyType.Dct32x64 => (32, 64),
    _ => (8, 8),
  };

  // libjxl distance bands for non-DCT8 strategies (transcribed from
  // quant_weights.cc). Format: [channel][band] where channel 0=X, 1=Y, 2=B.

  private static readonly float[][] _kDct16Bands = [
    [8996.872571181411f, -1.3000777393353804f, -0.49424529824571223f, -0.43909377445710346f, -0.6350101832695744f, -0.9017726405082761f, -1.6162099239887413f],
    [3191.4836629684423f, -0.6742458210419435f, -0.80745813428471f, -0.4492583748484344f, -0.35865440981033403f, -0.313223891118773f, -0.3761502531572548f],
    [1157.504081454872f, -2.053142316580441f, -1.4f, -0.5068713003337839f, -0.42708730624733904f, -1.4856834539296244f, -4.92091428844016f],
  ];

  /// <summary>The 64x64 transform's own weights. Without them its coefficients
  /// were dequantised with the 8x8 table stretched over the block, which is a
  /// different curve entirely — and a picture small enough to be one transform
  /// is coded as exactly one of these.</summary>
  private static readonly float[][] _kDct64Bands = [
    [0.9f * 26629.073922049845f, -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f],
    [0.9f * 9311.3238710010046f, -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f],
    [0.9f * 4992.2486445538634f, -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f],
  ];

  /// <summary>The 64x32 transform and its transpose.</summary>
  private static readonly float[][] _kDct64x32Bands = [
    [0.65f * 23629.073922049845f, -1.025f, -0.78f, -0.65012f, -0.19041574084286472f, -0.20819395464f, -0.421064f, -0.32733845535848671f],
    [0.65f * 8611.3238710010046f, -0.3041958212306401f, -0.3633036457487539f, -0.35660379990111464f, -0.3443074455424403f, -0.33699592683512467f, -0.30180866526242109f, -0.27321683125358037f],
    [0.65f * 4492.2486445538634f, -1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f],
  ];

  private static readonly float[][] _kDct32Bands = [
    [15718.408309825189f, -1.025f, -0.98f, -0.9012f, -0.4f, -0.48819395464f, -0.421064f, -0.27f],
    [7305.7636810695985f, -0.8041958212306402f, -0.7633036457487539f, -0.5566037999011146f, -0.49785304658857626f, -0.43699592683512467f, -0.4018086652624211f, -0.2732168312535804f],
    [3803.5317372121503f, -3.060733579805728f, -2.0413270132490346f, -2.023565015972742f, -0.5495389509954993f, -0.4f, -0.4f, -0.3f],
  ];

  private static readonly float[][] _kDct16x8Bands = [
    [7240.7734393502f, -0.7f, -0.7f, -0.2f, -0.2f, -0.2f, -0.5f],
    [1448.15468787004f, -0.5f, -0.5f, -0.5f, -0.2f, -0.2f, -0.2f],
    [506.854140754517f, -1.4f, -0.2f, -0.5f, -0.5f, -1.5f, -3.6f],
  ];

  private static readonly float[][] _kDct32x8Bands = [
    [16283.24947106489f, -1.7812845336559429f, -1.6309059012653515f, -1.038217903431354f, -0.85f, -0.7f, -0.9f, -1.2360638576849587f],
    [5089.157508849215f, -0.3200493914527869f, -0.35362849922161443f, -0.3034f, -0.61f, -0.5f, -0.5f, -0.6f],
    [3397.7760327530873f, -0.3213273626931534f, -0.34507619223117997f, -0.7034f, -0.9f, -1.0f, -1.0f, -1.175460557626521f],
  ];

  private static readonly float[][] _kDct32x16Bands = [
    [13844.970764423006f, -0.971138f, -0.658f, -0.42026f, -0.22712f, -0.2206f, -0.226f, -0.6f],
    [4798.964084220745f, -0.6112530898276706f, -0.8377078655249136f, -0.7901486207949863f, -0.2692727459704829f, -0.3827276946538855f, -0.22924222653091453f, -0.20719098826199578f],
    [1807.2369467609647f, -1.2f, -1.2f, -0.7f, -0.7f, -0.7f, -0.4f, -0.5f],
  ];

  private static readonly float[][] _kDct4Bands = [
    [2200.0f, 0.0f, 0.0f, 0.0f],
    [392.0f, 0.0f, 0.0f, 0.0f],
    [112.0f, -0.25f, -0.25f, -0.5f],
  ];

  private static readonly float[][] _kDct4x8Bands = [
    [2198.0505560163806f, -0.9626962302074469f, -0.7619425302666678f, -0.6551140670773546f],
    [764.3655248643529f, -0.9263020088836694f, -0.9675229603596517f, -0.27845290869168116f],
    [527.1075735875422f, -1.4594385811273853f, -1.4500820940978716f, -1.5843722511996203f],
  ];

  /// <summary>Dequantize one DCT8 block: pixels[i] = coeffs[i] * weights[i].
  /// Bypasses the multiplier; the global scale factor is applied separately.</summary>
  public static void Dequantize(short[] coeffs, JxlQuantTable table, float[] output) {
    if (coeffs is null)
      throw new ArgumentNullException(nameof(coeffs));
    if (table is null)
      throw new ArgumentNullException(nameof(table));
    if (output is null)
      throw new ArgumentNullException(nameof(output));

    var n = table.Width * table.Height;
    if (coeffs.Length != n)
      throw new ArgumentException($"coeffs length {coeffs.Length} does not match table size {n}.", nameof(coeffs));
    if (output.Length != n)
      throw new ArgumentException($"output length {output.Length} does not match table size {n}.", nameof(output));
    if (table.Weights.Length != n)
      throw new ArgumentException($"table weight length {table.Weights.Length} does not match table size {n}.", nameof(table));

    var weights = table.Weights;
    for (var i = 0; i < n; i++)
      output[i] = coeffs[i] * weights[i];
  }
}
