using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// XYB → linear sRGB → gamma sRGB color transform (ISO/IEC 18181-1 §G.7,
// libjxl `lib/jxl/dec_xyb.cc::OpsinToLinear` + `lib/jxl/dec_xyb-inl.h::XybToRgb`).
//
// XYB is JPEG XL's perceptually-tuned working color space. A pixel goes through:
//
//   1. Pre-bias gamma form:   r' = (y + x) + cbrt(bias)
//                             g' = (y - x) + cbrt(bias)
//                             b' =     b   + cbrt(bias)
//
//   2. Inverse cube-root gamma:  m_r = r'^3 - bias
//                                m_g = g'^3 - bias
//                                m_b = b'^3 - bias
//
//   3. Inverse opsin matrix:  linear_rgb = kDefaultInverseOpsinAbsorbanceMatrix · m
//
//   4. Linear→sRGB transfer (per IEC 61966-2-1):
//                  v <= 0.0031308:   v_srgb = 12.92 * v
//                  v >  0.0031308:   v_srgb = 1.055 * v^(1/2.4) - 0.055
//
// All constants below are spec-frozen and reproduced verbatim from libjxl
// `lib/jxl/cms/opsin_params.h` and `lib/jxl/opsin_params.cc`.
// =====================================================================================

internal static class JxlXybColorTransform {

  // -------------------------------------------------------------------------
  // Spec constants — verbatim from `lib/jxl/cms/opsin_params.h`.
  // -------------------------------------------------------------------------

  /// <summary>Opsin absorbance bias (kOpsinAbsorbanceBias0). Same value for all
  /// 3 channels in the default table.</summary>
  private const float _kOpsinAbsorbanceBias = 0.0037930732552754493f;

  /// <summary>Cube root of the bias — pre-computed once. libjxl computes this
  /// at runtime via `cbrtf(opsin_biases[c])`.</summary>
  private static readonly float _kOpsinAbsorbanceBiasCbrt =
    MathF.Cbrt(_kOpsinAbsorbanceBias);

  /// <summary>Default inverse opsin absorbance matrix from
  /// `kDefaultInverseOpsinAbsorbanceMatrix` — exact spec-mandated 3x3, row-major.
  /// Multiplied with `(mixed_r, mixed_g, mixed_b)^T` gives `(linear_r, g, b)^T`.</summary>
  private static readonly float[,] _kInverseOpsinAbsorbanceMatrix = new float[,] {
    { 11.031566901960783f, -9.866943921568629f,  -0.16462299647058826f },
    { -3.254147380392157f,  4.418770392156863f,  -0.16462299647058826f },
    { -3.6588512862745097f, 2.7129230470588235f,  1.9459282392156863f  },
  };

  /// <summary>Convert one XYB pixel to linear sRGB. Per libjxl
  /// `lib/jxl/dec_xyb.cc::OpsinToLinear`, uses the inverse Opsin matrix and
  /// bias subtraction.</summary>
  public static (float R, float G, float B) XybToLinearSrgb(float x, float y, float b) {
    // Step 1: XYB → pre-bias gamma form.
    // libjxl: gamma_r = y + x; gamma_g = y - x; gamma_b = b
    //         then subtract opsin_biases_cbrt (which equals -cbrt(bias) since
    //         opsin_biases stores the negated bias). Net effect: ADD cbrt(bias).
    var gammaR = (y + x) + _kOpsinAbsorbanceBiasCbrt;
    var gammaG = (y - x) + _kOpsinAbsorbanceBiasCbrt;
    var gammaB = b + _kOpsinAbsorbanceBiasCbrt;

    // Step 2: cube + bias subtraction.
    var mixedR = gammaR * gammaR * gammaR - _kOpsinAbsorbanceBias;
    var mixedG = gammaG * gammaG * gammaG - _kOpsinAbsorbanceBias;
    var mixedB = gammaB * gammaB * gammaB - _kOpsinAbsorbanceBias;

    // Step 3: 3x3 inverse-matrix multiply.
    var m = _kInverseOpsinAbsorbanceMatrix;
    var linR = m[0, 0] * mixedR + m[0, 1] * mixedG + m[0, 2] * mixedB;
    var linG = m[1, 0] * mixedR + m[1, 1] * mixedG + m[1, 2] * mixedB;
    var linB = m[2, 0] * mixedR + m[2, 1] * mixedG + m[2, 2] * mixedB;

    return (linR, linG, linB);
  }

  /// <summary>Convert linear sRGB float to gamma-sRGB byte (0..255). Uses the
  /// IEC 61966-2-1 piecewise transfer function.</summary>
  public static byte LinearSrgbToGammaByte(float v) {
    if (v <= 0.0f)
      return 0;
    if (v >= 1.0f)
      return 255;

    float gamma;
    if (v <= 0.0031308f)
      gamma = 12.92f * v;
    else
      gamma = 1.055f * MathF.Pow(v, 1.0f / 2.4f) - 0.055f;

    var scaled = gamma * 255.0f + 0.5f;
    if (scaled <= 0.0f)
      return 0;
    if (scaled >= 255.0f)
      return 255;
    return (byte)scaled;
  }

  /// <summary>Bulk transform: 3 XYB channels (W*H floats each) → packed RGB24
  /// byte buffer (W*H*3).</summary>
  public static byte[] XybPlanesToRgb24(float[] x, float[] y, float[] b, int width, int height) {
    if (x is null) throw new ArgumentNullException(nameof(x));
    if (y is null) throw new ArgumentNullException(nameof(y));
    if (b is null) throw new ArgumentNullException(nameof(b));
    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

    var n = checked(width * height);
    if (x.Length != n) throw new ArgumentException($"x length {x.Length} != {n}.", nameof(x));
    if (y.Length != n) throw new ArgumentException($"y length {y.Length} != {n}.", nameof(y));
    if (b.Length != n) throw new ArgumentException($"b length {b.Length} != {n}.", nameof(b));

    var output = new byte[checked(n * 3)];
    for (var i = 0; i < n; i++) {
      var (r, g, bl) = XybToLinearSrgb(x[i], y[i], b[i]);
      var dst = i * 3;
      output[dst + 0] = LinearSrgbToGammaByte(r);
      output[dst + 1] = LinearSrgbToGammaByte(g);
      output[dst + 2] = LinearSrgbToGammaByte(bl);
    }
    return output;
  }
}
