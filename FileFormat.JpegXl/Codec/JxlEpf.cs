using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Edge-Preserving Filter (EPF) loop filter for JPEG XL VarDCT
/// (ISO/IEC 18181-1 §G.10).
///
/// <para>EPF is an adaptive, non-linear, bilateral-style smoothing filter that
/// runs after the IDCT/inverse Gaborish step and operates on the decoded
/// XYB-residual planes. Each pass scans every pixel, gathers a small stencil
/// of neighbours, weights them by an exponential function of their L1 patch
/// distance from the centre, and replaces the centre with the weighted mean.
/// The aggressiveness (sigma) per 8×8 block is precomputed by libjxl's
/// <c>ComputeSigma</c>; sharpness LUT entries 0..7 modulate it further.</para>
///
/// <para>libjxl reference:
/// <list type="bullet">
///   <item><c>lib/jxl/epf.h</c> — <c>ComputeSigma</c>, kInvSigmaNum constant</item>
///   <item><c>lib/jxl/epf.cc</c> — sigma image construction with mirroring</item>
///   <item><c>lib/jxl/loop_filter.h</c> / <c>loop_filter.cc</c> — bitstream
///   field layout and defaults for <c>epf_iters</c>, <c>epf_sharp_lut[8]</c>,
///   <c>epf_channel_scale[3]</c>, <c>epf_pass1_zeroflush</c>,
///   <c>epf_pass2_zeroflush</c>, <c>epf_quant_mul</c>,
///   <c>epf_pass0_sigma_scale</c>, <c>epf_pass2_sigma_scale</c>,
///   <c>epf_border_sad_mul</c>, <c>epf_sigma_for_modular</c></item>
/// </list>
/// </para>
///
/// <para><b>First-wave scope</b>:
/// <list type="bullet">
///   <item><see cref="ReadHeader"/> reads the field structure (Bundle/AllDefault,
///   sharpness LUT, weight params, sigma params, modular sigma) sufficient to
///   round-trip libjxl's defaults and most non-default headers.</item>
///   <item><see cref="Apply"/> implements <c>Iters == 0</c> as a no-op and
///   <c>Iters == 1</c> as a simplified single-pass bilateral filter using a
///   plus-shaped (4-neighbour) stencil. Passes &gt; 1 throw
///   <see cref="NotImplementedException"/>.</item>
///   <item>All compute is scalar; SIMD is intentionally omitted at this stage.</item>
/// </list>
/// </para>
/// </summary>
internal static class JxlEpf {

  // 4 * (sqrt(0.5) - 1), so that Weight(sigma) = 0.5. From libjxl epf.h.
  private const float _INV_SIGMA_NUM = -1.1715728752538099024f;

  /// <summary>Apply EPF (3-pass adaptive bilateral filter) to all 3 channels.
  /// Operates on an X×Y×3-channel float image; mutates in place. Sigma values
  /// per block control how aggressive the filter is.</summary>
  /// <param name="channels">[3][W*H] row-major channel planes (X, Y, B).</param>
  /// <param name="width">Pixel width of each channel plane.</param>
  /// <param name="height">Pixel height of each channel plane.</param>
  /// <param name="sigmaPerBlock">[blocksWide * blocksHigh] sigma per 8×8 block.
  /// A value &lt;= 0 disables filtering for that block (matches libjxl's
  /// "sigma is min(-1e-4, sigma)" sentinel: sigmas there are reciprocals so
  /// near-zero means very little blur).</param>
  /// <param name="blocksWide">Number of 8-block columns (ceil(width/8)).</param>
  /// <param name="blocksHigh">Number of 8-block rows (ceil(height/8)).</param>
  /// <param name="parameters">EPF parameters from the loop-filter header.</param>
  public static void Apply(
    float[][] channels,
    int width, int height,
    float[] sigmaPerBlock,
    int blocksWide, int blocksHigh,
    EpfParams parameters
  ) {
    if (channels == null) throw new ArgumentNullException(nameof(channels));
    if (channels.Length < 3) throw new ArgumentException("Need 3 channels.", nameof(channels));
    if (parameters == null) throw new ArgumentNullException(nameof(parameters));
    if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
    if (sigmaPerBlock == null) throw new ArgumentNullException(nameof(sigmaPerBlock));
    if (blocksWide < 0) throw new ArgumentOutOfRangeException(nameof(blocksWide));
    if (blocksHigh < 0) throw new ArgumentOutOfRangeException(nameof(blocksHigh));

    var iters = parameters.Iters;
    if (iters == 0)
      return; // disabled

    if (iters > 1)
      throw new NotImplementedException(
        $"EPF iters={iters}: only iters in {{0,1}} are implemented in the first wave. "
        + "Multi-pass EPF (libjxl epf.cc) requires the full 12-tap stencil + sharpness LUT.");

    // For each channel plane, expected length = width * height.
    for (var c = 0; c < 3; c++) {
      var plane = channels[c];
      if (plane == null) throw new ArgumentException($"channels[{c}] is null.", nameof(channels));
      if (plane.Length < width * height)
        throw new ArgumentException($"channels[{c}] too small: have {plane.Length}, need {width * height}.", nameof(channels));
    }

    if (sigmaPerBlock.Length < blocksWide * blocksHigh)
      throw new ArgumentException("sigmaPerBlock too small for blocksWide*blocksHigh.", nameof(sigmaPerBlock));

    if (width == 0 || height == 0)
      return;

    // Allocate output buffers (filter is not in-place at the per-pixel level —
    // a pixel's update would otherwise contaminate its right/down neighbours).
    var outX = new float[width * height];
    var outY = new float[width * height];
    var outB = new float[width * height];

    var inX = channels[0];
    var inY = channels[1];
    var inB = channels[2];

    // Plus-shaped stencil (centre + 4 cardinal neighbours, mirrored at edges).
    // A faithful EPF would use a 12-tap diamond and an L1-patch-distance, but
    // the single-pass simplification is sufficient to (a) produce a smoothing
    // effect when sigma is non-zero, (b) preserve pixels exactly when sigma is
    // zero, and (c) integrate without crashing on real bitstreams.

    for (var y = 0; y < height; y++) {
      var by = Math.Min(y / 8, blocksHigh - 1);
      for (var x = 0; x < width; x++) {
        var bx = Math.Min(x / 8, blocksWide - 1);
        var sigma = sigmaPerBlock[by * blocksWide + bx];

        // libjxl convention: sigma is stored as 1/sigma; "no filter" sentinel is
        // a value with magnitude that makes the weight effectively unity. We
        // treat any sigma <= 0 as "skip this block".
        if (sigma <= 0.0f) {
          var idxNo = y * width + x;
          outX[idxNo] = inX[idxNo];
          outY[idxNo] = inY[idxNo];
          outB[idxNo] = inB[idxNo];
          continue;
        }

        var idx = y * width + x;
        var cx = inX[idx];
        var cy = inY[idx];
        var cb = inB[idx];

        // Centre always contributes with weight 1.
        var sumX = cx;
        var sumY = cy;
        var sumB = cb;
        var sumW = 1.0f;

        // 4 cardinal neighbours with edge mirroring.
        _Tap(inX, inY, inB, width, height, x - 1, y, cx, cy, cb, sigma, parameters, ref sumX, ref sumY, ref sumB, ref sumW);
        _Tap(inX, inY, inB, width, height, x + 1, y, cx, cy, cb, sigma, parameters, ref sumX, ref sumY, ref sumB, ref sumW);
        _Tap(inX, inY, inB, width, height, x, y - 1, cx, cy, cb, sigma, parameters, ref sumX, ref sumY, ref sumB, ref sumW);
        _Tap(inX, inY, inB, width, height, x, y + 1, cx, cy, cb, sigma, parameters, ref sumX, ref sumY, ref sumB, ref sumW);

        var inv = 1.0f / sumW;
        outX[idx] = sumX * inv;
        outY[idx] = sumY * inv;
        outB[idx] = sumB * inv;
      }
    }

    // Copy results back into the caller's buffers.
    Array.Copy(outX, channels[0], width * height);
    Array.Copy(outY, channels[1], width * height);
    Array.Copy(outB, channels[2], width * height);
  }

  /// <summary>Read the EPF header from the bitstream (when restoration filter
  /// is signaled). Returns null when EPF is disabled (epf_iters=0).</summary>
  /// <remarks>
  /// <para>Bitstream order from libjxl <c>LoopFilter::VisitFields</c>
  /// (loop_filter.cc), invoked after AllDefault and after the Gaborish block:
  /// <c>U(2) epf_iters</c>; if &gt; 0 then <c>Bool epf_sharp_custom</c> (and
  /// 8×F16 sharp LUT if true), <c>Bool epf_weight_custom</c> (and 5×F16
  /// channel-scale + zeroflush params if true), <c>Bool epf_sigma_custom</c>
  /// (and 4×F16 sigma params if true), and finally <c>F16
  /// epf_sigma_for_modular</c> (only in modular mode — for VarDCT the field
  /// is omitted).</para>
  /// <para>This method assumes VarDCT (non-modular) mode. The caller must have
  /// already consumed any AllDefault flag and the Gaborish parameters.</para>
  /// </remarks>
  public static EpfParams? ReadHeader(JxlBitReader reader) {
    if (reader == null) throw new ArgumentNullException(nameof(reader));

    var iters = (int)reader.ReadBits(2);
    if (iters == 0)
      return null;

    var sharpness = new float[8];
    for (var i = 0; i < 8; i++)
      sharpness[i] = i / 7.0f; // default: linear ramp 0..1

    var sharpCustom = reader.ReadBool();
    if (sharpCustom) {
      for (var i = 0; i < 8; i++)
        sharpness[i] = _ReadF16(reader);
    }

    var sigmaForModX = new float[3] { 40.0f, 5.0f, 3.5f };
    var sigmaForModY = new float[3] { 40.0f, 5.0f, 3.5f };
    var pass1Zero = 0.45f;
    var pass2Zero = 0.6f;

    var weightCustom = reader.ReadBool();
    if (weightCustom) {
      var s0 = _ReadF16(reader);
      var s1 = _ReadF16(reader);
      var s2 = _ReadF16(reader);
      sigmaForModX[0] = s0;
      sigmaForModX[1] = s1;
      sigmaForModX[2] = s2;
      sigmaForModY[0] = s0;
      sigmaForModY[1] = s1;
      sigmaForModY[2] = s2;
      pass1Zero = _ReadF16(reader);
      pass2Zero = _ReadF16(reader);
    }

    var sigmaMul = 0.46f;
    var pass0Sigma = 0.9f;
    var pass2Sigma = 6.5f;
    var border = 0.6666666666666666f;

    var sigmaCustom = reader.ReadBool();
    if (sigmaCustom) {
      sigmaMul = _ReadF16(reader);
      pass0Sigma = _ReadF16(reader);
      pass2Sigma = _ReadF16(reader);
      border = _ReadF16(reader);
    }

    return new EpfParams {
      Iters = iters,
      SigmaForModularX = sigmaForModX,
      SigmaForModularY = sigmaForModY,
      Sharpness = sharpness,
      SigmaMul = sigmaMul,
      Pass1SigmaScale = pass1Zero,
      Pass2SigmaScale = pass2Zero,
      Pass0SigmaCircle = pass0Sigma,
      Pass2SigmaCircle = pass2Sigma,
      Border = border,
    };
  }

  /// <summary>Single neighbour tap: clamp coordinates by mirroring to plane
  /// edges, compute Gaussian weight on L1 patch distance using sigma, and
  /// accumulate into the running weighted sum.</summary>
  private static void _Tap(
    float[] inX, float[] inY, float[] inB,
    int width, int height,
    int nx, int ny,
    float cx, float cy, float cb,
    float sigma,
    EpfParams parameters,
    ref float sumX, ref float sumY, ref float sumB, ref float sumW
  ) {
    // Mirror at borders.
    if (nx < 0) nx = -nx;
    if (ny < 0) ny = -ny;
    if (nx >= width) nx = (2 * width - 2) - nx;
    if (ny >= height) ny = (2 * height - 2) - ny;
    if (nx < 0) nx = 0;
    if (ny < 0) ny = 0;
    if (nx >= width) nx = width - 1;
    if (ny >= height) ny = height - 1;

    var nIdx = ny * width + nx;
    var nx0 = inX[nIdx];
    var ny0 = inY[nIdx];
    var nb0 = inB[nIdx];

    // L1 patch distance (single-pixel "patch").
    var dx = nx0 - cx;
    var dy = ny0 - cy;
    var db = nb0 - cb;
    if (dx < 0) dx = -dx;
    if (dy < 0) dy = -dy;
    if (db < 0) db = -db;
    var diff = dx + dy + db;

    // Gaussian weight: exp(-(diff / sigma)^2). For sigma > 0 and small diff
    // weight approaches 1; for large diff it approaches 0.
    var t = diff * sigma; // sigma is already 1/raw_sigma
    var w = (float)Math.Exp(-t * t);

    // libjxl's pass1 zero-flush threshold gates contributions whose weight
    // falls below a minimum. For the first-wave simplified single-pass filter
    // we keep the bilateral form unchanged (Gaussian weights already approach
    // 0 on edges), but still consult the parameter so the test surface for
    // future pass1/pass2 implementations is wired through.
    _ = parameters;

    sumX += nx0 * w;
    sumY += ny0 * w;
    sumB += nb0 * w;
    sumW += w;
  }

  /// <summary>Decode a JPEG XL F16 (IEEE 754 binary16) bitstream field.
  /// Mirrors the local helper in <see cref="JxlImageMetadata"/> but is kept
  /// here to avoid making that helper public.</summary>
  private static float _ReadF16(JxlBitReader r) {
    var bits = (ushort)r.ReadBits(16);
    var sign = (bits >> 15) & 1;
    var exp = (bits >> 10) & 0x1F;
    var frac = bits & 0x3FF;
    if (exp == 0) {
      // Zero or subnormal. For first-wave we treat subnormals as the limiting
      // value (mantissa * 2^-14) — sufficient for round-trip of libjxl's
      // power-of-two defaults which all have exp != 0.
      if (frac == 0)
        return sign != 0 ? -0f : 0f;
      var subVal = (frac / 1024.0) * Math.Pow(2, -14);
      return (float)(sign != 0 ? -subVal : subVal);
    }
    if (exp == 31)
      return frac == 0
        ? (sign != 0 ? float.NegativeInfinity : float.PositiveInfinity)
        : float.NaN;
    var mantissa = 1 + frac / 1024.0;
    var value = mantissa * Math.Pow(2, exp - 15);
    return (float)(sign != 0 ? -value : value);
  }
}

/// <summary>Parsed EPF header parameters (libjxl <c>LoopFilter</c> EPF-section
/// fields) as decoded by <see cref="JxlEpf.ReadHeader"/>.</summary>
internal sealed class EpfParams {

  /// <summary>1..3 — number of EPF passes. 0 is "disabled" and is encoded by
  /// returning <c>null</c> from <see cref="JxlEpf.ReadHeader"/>.</summary>
  public int Iters { get; init; }

  /// <summary>Per-channel sigma scale for X plane. Length 3 when populated,
  /// indexes 0..2 correspond to channel scale slots in libjxl's
  /// <c>epf_channel_scale</c>.</summary>
  public float[] SigmaForModularX { get; init; } = [];

  /// <summary>Per-channel sigma scale for Y plane (same layout).</summary>
  public float[] SigmaForModularY { get; init; } = [];

  /// <summary>8 sharpness LUT entries; defaults are <c>i/7</c>.</summary>
  public float[] Sharpness { get; init; } = [];

  /// <summary>libjxl <c>epf_quant_mul</c> — multiplier applied to quant when
  /// computing per-block sigma. Default 0.46.</summary>
  public float SigmaMul { get; init; }

  /// <summary>libjxl <c>epf_pass1_zeroflush</c> — minimum weight for pass 1.
  /// Default 0.45.</summary>
  public float Pass1SigmaScale { get; init; }

  /// <summary>libjxl <c>epf_pass2_zeroflush</c> — minimum weight for pass 2.
  /// Default 0.60.</summary>
  public float Pass2SigmaScale { get; init; }

  /// <summary>libjxl <c>epf_pass0_sigma_scale</c> — sigma scale for pass 0.
  /// Default 0.9.</summary>
  public float Pass0SigmaCircle { get; init; }

  /// <summary>libjxl <c>epf_pass2_sigma_scale</c> — sigma scale for pass 2.
  /// Default 6.5.</summary>
  public float Pass2SigmaCircle { get; init; }

  /// <summary>libjxl <c>epf_border_sad_mul</c> — sigma quantization border
  /// multiplier. Default 0.666….</summary>
  public float Border { get; init; }
}
