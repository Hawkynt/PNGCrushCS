using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Gaborish loop filter (ISO/IEC 18181-1 §G.10, libjxl
// `lib/jxl/render_pipeline/stage_gaborish.cc::GaborishStage::ProcessRow` and
// `lib/jxl/loop_filter.cc::LoopFilter::VisitFields`).
//
// Gaborish is a 3x3 separable convolution that runs on each VarDCT channel after
// inverse-DCT and before XYB → linear. The encoder applies the *forward* Gabor-like
// filter (sharpening), so the decoder applies its *inverse* (a low-pass). The
// kernel is centered with weight `w0`, the 4-neighbours (W, E, N, S) get `w1` and
// the 4-diagonals (NW, NE, SW, SE) get `w2`. After normalization
// (`div = w0 + 4*(w1 + w2)`) the kernel has unit DC gain, so a constant input
// produces a constant output (subject to float roundoff).
//
// Spec-default weights per channel are `1.1 * {0.104699568f, 0.055680538f}`,
// i.e. `(0.11516952..., 0.06124859...)` for X, Y and B channels (libjxl uses
// the same defaults for all three).
//
// Bitstream layout (libjxl `LoopFilter::VisitFields`):
//   Bool all_default       (default true)         — if true, use all defaults
//   Bool gab               (default true)         — gates Gaborish on/off
//   if (gab)
//     Bool gab_custom      (default false)        — gates 6 F16 weights below
//     if (gab_custom)
//       F16 gab_x_weight1, gab_x_weight2,
//           gab_y_weight1, gab_y_weight2,
//           gab_b_weight1, gab_b_weight2
//
// EPF parameters follow but are out of scope for this file.
// =====================================================================================

/// <summary>Gaborish header parameters as recovered from the bitstream
/// (LoopFilter sub-fields scoped to Gaborish only).</summary>
internal sealed class GaborishParams {
  public bool Enabled { get; init; }

  /// <summary>Per-channel `[a, b]` weights — `a` = 4-neighbour weight,
  /// `b` = diagonal weight. The center weight is always `1` pre-normalization.</summary>
  public float[] WeightsX { get; init; } = [];
  public float[] WeightsY { get; init; } = [];
  public float[] WeightsB { get; init; } = [];
}

internal static class JxlGaborish {

  // ------------------------------------------------------------------------
  // Spec constants — verbatim from libjxl `lib/jxl/loop_filter.cc`.
  //
  //   visitor->F16(1.1 * 0.104699568f, &gab_X_weight1)
  //   visitor->F16(1.1 * 0.055680538f, &gab_X_weight2)
  //
  // (same for Y and B channels in the default header). The 1.1 multiplier
  // is part of the spec — the bare 0.104699568 / 0.055680538 are *not* the
  // serialized defaults.
  // ------------------------------------------------------------------------

  private const float _kDefaultA = 1.1f * 0.104699568f; // ≈ 0.115169525
  private const float _kDefaultB = 1.1f * 0.055680538f; // ≈ 0.061248592

  // ------------------------------------------------------------------------
  // Public API.
  // ------------------------------------------------------------------------

  /// <summary>Spec-default Gaborish weights `(a, b)` for channel `c` ∈ {0=X, 1=Y, 2=B}.
  /// The defaults are identical for all three channels per
  /// `lib/jxl/loop_filter.cc::LoopFilter::VisitFields`.</summary>
  public static (float A, float B) DefaultWeights(int channel) {
    if (channel < 0 || channel > 2)
      throw new ArgumentOutOfRangeException(nameof(channel), "Must be 0 (X), 1 (Y) or 2 (B).");
    return (_kDefaultA, _kDefaultB);
  }

  /// <summary>Apply the inverse Gaborish 3x3 filter to a single channel plane,
  /// in place. Boundary pixels are handled by edge-replication (out-of-bounds
  /// reads use the nearest in-bounds pixel — equivalent to libjxl's mirror-pad
  /// for the 1-pixel border in this kernel).</summary>
  /// <remarks>The kernel is normalized: <c>w0 + 4*(w1 + w2) = 1</c> so a
  /// constant input is preserved exactly (modulo float rounding).</remarks>
  public static void ApplyInPlace(float[] pixels, int width, int height, float[]? weights = null) {
    if (pixels is null) throw new ArgumentNullException(nameof(pixels));
    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    if (pixels.Length != checked(width * height))
      throw new ArgumentException($"pixels length {pixels.Length} != {width}*{height}.", nameof(pixels));

    // Resolve and normalize weights.
    float a, b;
    if (weights is null) {
      a = _kDefaultA;
      b = _kDefaultB;
    } else if (weights.Length == 2) {
      a = weights[0];
      b = weights[1];
    } else
      throw new ArgumentException("weights must be null or length 2 [a, b].", nameof(weights));

    // libjxl: div = w0 + 4*(w1 + w2), with w0 = 1 pre-normalization.
    var div = 1.0f + 4.0f * (a + b);
    if (MathF.Abs(div) < 1e-8f)
      throw new ArgumentException("Gaborish weights yield a near-zero unnormalized kernel.", nameof(weights));

    var inv = 1.0f / div;
    var w0 = 1.0f * inv;
    var w1 = a * inv;
    var w2 = b * inv;

    // Snapshot input — kernel reads neighbours, so we cannot mutate while reading.
    var src = new float[pixels.Length];
    Array.Copy(pixels, src, pixels.Length);

    for (var y = 0; y < height; y++) {
      var yT = y > 0 ? y - 1 : 0;
      var yB = y < height - 1 ? y + 1 : height - 1;
      var rowM = y * width;
      var rowT = yT * width;
      var rowB = yB * width;

      for (var x = 0; x < width; x++) {
        var xL = x > 0 ? x - 1 : 0;
        var xR = x < width - 1 ? x + 1 : width - 1;

        var center = src[rowM + x];
        var west = src[rowM + xL];
        var east = src[rowM + xR];
        var north = src[rowT + x];
        var south = src[rowB + x];
        var nw = src[rowT + xL];
        var ne = src[rowT + xR];
        var sw = src[rowB + xL];
        var se = src[rowB + xR];

        var sum1 = west + east + north + south;
        var sum2 = nw + ne + sw + se;
        pixels[rowM + x] = w0 * center + w1 * sum1 + w2 * sum2;
      }
    }
  }

  /// <summary>Read the LoopFilter Gaborish sub-block from the bitstream.
  /// Returns <c>null</c> when Gaborish is disabled (`gab` flag = 0). The
  /// reader is positioned just past the loop-filter `all_default` flag's
  /// associated fields when this method returns. Mirrors
  /// <c>LoopFilter::VisitFields</c> for the Gaborish branch only.</summary>
  /// <remarks>This implementation handles the `all_default` and the `gab`
  /// + `gab_custom` flags, but stops before the EPF section. Callers that
  /// also need EPF parameters must read them separately.</remarks>
  public static GaborishParams? ReadHeader(JxlBitReader reader) {
    if (reader is null) throw new ArgumentNullException(nameof(reader));

    // Bool all_default — if set, full LoopFilter defaults apply.
    var allDefault = reader.ReadBool();
    if (allDefault) {
      // Defaults: gab = true, gab_custom = false, all weights at spec defaults.
      return new GaborishParams {
        Enabled = true,
        WeightsX = [_kDefaultA, _kDefaultB],
        WeightsY = [_kDefaultA, _kDefaultB],
        WeightsB = [_kDefaultA, _kDefaultB],
      };
    }

    // Bool gab (default true) — gates Gaborish on/off.
    var gab = reader.ReadBool();
    if (!gab)
      return null;

    // Bool gab_custom (default false) — gates 6 F16 custom weights.
    var gabCustom = reader.ReadBool();
    if (!gabCustom) {
      return new GaborishParams {
        Enabled = true,
        WeightsX = [_kDefaultA, _kDefaultB],
        WeightsY = [_kDefaultA, _kDefaultB],
        WeightsB = [_kDefaultA, _kDefaultB],
      };
    }

    var xw1 = _ReadF16(reader);
    var xw2 = _ReadF16(reader);
    if (MathF.Abs(1.0f + (xw1 + xw2) * 4.0f) < 1e-8f)
      throw new InvalidOperationException("Gaborish X weights lead to near-zero unnormalized kernel.");

    var yw1 = _ReadF16(reader);
    var yw2 = _ReadF16(reader);
    if (MathF.Abs(1.0f + (yw1 + yw2) * 4.0f) < 1e-8f)
      throw new InvalidOperationException("Gaborish Y weights lead to near-zero unnormalized kernel.");

    var bw1 = _ReadF16(reader);
    var bw2 = _ReadF16(reader);
    if (MathF.Abs(1.0f + (bw1 + bw2) * 4.0f) < 1e-8f)
      throw new InvalidOperationException("Gaborish B weights lead to near-zero unnormalized kernel.");

    return new GaborishParams {
      Enabled = true,
      WeightsX = [xw1, xw2],
      WeightsY = [yw1, yw2],
      WeightsB = [bw1, bw2],
    };
  }

  // ------------------------------------------------------------------------
  // Helpers.
  // ------------------------------------------------------------------------

  /// <summary>Decode a 16-bit IEEE 754 half-precision float from the bit
  /// stream (LSB-first 16 bits). Mirrors the F16 decoder used elsewhere in
  /// the codec (see <c>JxlImageMetadata._ReadF16</c>).</summary>
  private static float _ReadF16(JxlBitReader r) {
    var bits = (ushort)r.ReadBits(16);
    var sign = (bits >> 15) & 1;
    var exp = (bits >> 10) & 0x1F;
    var frac = bits & 0x3FF;
    if (exp == 0) {
      // Subnormal: value = (-1)^sign * 2^-14 * (frac/1024). Zero when frac=0.
      if (frac == 0)
        return sign != 0 ? -0f : 0f;
      var sub = frac / 1024.0 * Math.Pow(2, -14);
      return (float)(sign != 0 ? -sub : sub);
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
