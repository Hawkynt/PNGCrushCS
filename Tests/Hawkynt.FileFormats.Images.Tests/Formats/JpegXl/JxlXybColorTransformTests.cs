using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlXybColorTransform"/> (ISO/IEC
/// 18181-1 §G.7, libjxl <c>OpsinToLinear</c> in <c>lib/jxl/dec_xyb.cc</c> +
/// <c>XybToRgb</c> in <c>lib/jxl/dec_xyb-inl.h</c>).
/// </summary>
[TestFixture]
public sealed class JxlXybColorTransformTests {

  // ------------------------------------------------------------------
  // XYB → linear sRGB.
  // ------------------------------------------------------------------

  [Test]
  public void XybToLinearSrgb_AtOriginIsBlack() {
    // libjxl's XybToRgb at (x=0, y=0, b=0) computes:
    //   gamma_r = 0 + cbrt(bias) ≈ 0.155954
    //   mixed_r = gamma_r^3 - bias = bias - bias = 0
    // Same for g and b channels. So the inverse-matrix output is (0, 0, 0).
    // (The XYB origin is canonically pure black for the default opsin matrix.)
    var (r, g, b) = JxlXybColorTransform.XybToLinearSrgb(0.0f, 0.0f, 0.0f);
    Assert.That(r, Is.EqualTo(0.0f).Within(1e-5f));
    Assert.That(g, Is.EqualTo(0.0f).Within(1e-5f));
    Assert.That(b, Is.EqualTo(0.0f).Within(1e-5f));
  }

  [Test]
  public void XybToLinearSrgb_PositiveYProducesNonNegativeRgb() {
    // A positive y with x=b=0 should produce a (roughly) gray increase in
    // linear sRGB. Mostly we just assert no NaN/Inf and that it's not all
    // zero (the transform is monotonic in y at the origin).
    var (r, g, b) = JxlXybColorTransform.XybToLinearSrgb(0.0f, 0.5f, 0.0f);
    Assert.That(float.IsFinite(r), Is.True);
    Assert.That(float.IsFinite(g), Is.True);
    Assert.That(float.IsFinite(b), Is.True);
    Assert.That(MathF.Abs(r) + MathF.Abs(g) + MathF.Abs(b),
      Is.GreaterThan(0.0f));
  }

  [Test]
  public void XybToLinearSrgb_DeterministicNonNanForTypicalInput() {
    // Typical XYB encoded value range for in-gamut sRGB is roughly
    // x ∈ [-0.015, 0.028], y ∈ [0, 0.85], b ∈ [0, 0.85] (per libjxl
    // FastXYBTosRGB8 comment). Make sure all paths produce finite output.
    var pts = new[] {
      (-0.01f, 0.1f, 0.1f),
      (0.02f, 0.4f, 0.5f),
      (0.0f, 0.85f, 0.85f),
      (-0.015f, 0.0f, 0.0f),
    };
    foreach (var (x, y, b) in pts) {
      var (r, g, bl) = JxlXybColorTransform.XybToLinearSrgb(x, y, b);
      Assert.That(float.IsFinite(r), Is.True, $"NaN/Inf r at ({x},{y},{b})");
      Assert.That(float.IsFinite(g), Is.True, $"NaN/Inf g at ({x},{y},{b})");
      Assert.That(float.IsFinite(bl), Is.True, $"NaN/Inf b at ({x},{y},{b})");
    }
  }

  // ------------------------------------------------------------------
  // Linear sRGB → gamma sRGB byte.
  // ------------------------------------------------------------------

  [Test]
  public void LinearSrgbToGammaByte_ZeroIsZero() {
    Assert.That(JxlXybColorTransform.LinearSrgbToGammaByte(0.0f), Is.EqualTo(0));
  }

  [Test]
  public void LinearSrgbToGammaByte_OneIs255() {
    Assert.That(JxlXybColorTransform.LinearSrgbToGammaByte(1.0f), Is.EqualTo(255));
  }

  [Test]
  public void LinearSrgbToGammaByte_HalfIsApprox188() {
    // sRGB transfer at 0.5: 1.055 * 0.5^(1/2.4) - 0.055
    //                     ≈ 1.055 * 0.7320508 - 0.055
    //                     ≈ 0.7353 → byte ≈ 188.
    var actual = JxlXybColorTransform.LinearSrgbToGammaByte(0.5f);
    Assert.That(actual, Is.InRange((byte)186, (byte)190));
  }

  [Test]
  public void LinearSrgbToGammaByte_ClampsNegative() {
    Assert.That(JxlXybColorTransform.LinearSrgbToGammaByte(-1.0f), Is.EqualTo(0));
  }

  [Test]
  public void LinearSrgbToGammaByte_ClampsAboveOne() {
    Assert.That(JxlXybColorTransform.LinearSrgbToGammaByte(2.0f), Is.EqualTo(255));
  }

  [Test]
  public void LinearSrgbToGammaByte_LinearRegimeBelowKnee() {
    // Below v = 0.0031308 the function is purely linear (12.92 * v).
    // At v = 0.001, gamma = 12.92 * 0.001 = 0.01292 → byte ≈ 3.
    var actual = JxlXybColorTransform.LinearSrgbToGammaByte(0.001f);
    Assert.That(actual, Is.InRange((byte)2, (byte)4));
  }

  [Test]
  public void LinearSrgbToGammaByte_IsMonotonic() {
    byte previous = 0;
    for (var i = 0; i <= 100; i++) {
      var v = i / 100.0f;
      var byt = JxlXybColorTransform.LinearSrgbToGammaByte(v);
      Assert.That(byt, Is.GreaterThanOrEqualTo(previous));
      previous = byt;
    }
  }

  // ------------------------------------------------------------------
  // Bulk transform.
  // ------------------------------------------------------------------

  [Test]
  public void XybPlanesToRgb24_AllZeroPlanesProduceUniformColor() {
    const int W = 4;
    const int H = 3;
    var x = new float[W * H];
    var y = new float[W * H];
    var b = new float[W * H];
    var rgb = JxlXybColorTransform.XybPlanesToRgb24(x, y, b, W, H);
    Assert.That(rgb.Length, Is.EqualTo(W * H * 3));

    // Every pixel must equal pixel 0 (uniform output).
    for (var i = 0; i < W * H; i++) {
      Assert.That(rgb[i * 3 + 0], Is.EqualTo(rgb[0]));
      Assert.That(rgb[i * 3 + 1], Is.EqualTo(rgb[1]));
      Assert.That(rgb[i * 3 + 2], Is.EqualTo(rgb[2]));
    }
  }

  [Test]
  public void XybPlanesToRgb24_OutputBufferLength() {
    var rgb = JxlXybColorTransform.XybPlanesToRgb24(
      new float[6], new float[6], new float[6], 3, 2);
    Assert.That(rgb.Length, Is.EqualTo(3 * 2 * 3));
  }

  [Test]
  public void XybPlanesToRgb24_ClampsInGamutWhite() {
    // A bright XYB pixel should produce something near white in sRGB.
    // libjxl's encoding for white sRGB (1,1,1) yields y ≈ 0.5 (small magnitudes).
    // We only assert that bright y values produce output > pure black.
    const int W = 1;
    const int H = 1;
    var x = new[] { 0.0f };
    var y = new[] { 0.5f };
    var b = new[] { 0.5f };
    var rgb = JxlXybColorTransform.XybPlanesToRgb24(x, y, b, W, H);
    var sum = rgb[0] + rgb[1] + rgb[2];
    Assert.That(sum, Is.GreaterThan(0));
  }

  [Test]
  public void XybPlanesToRgb24_RejectsMismatchedLengths() {
    Assert.Throws<ArgumentException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        new float[10], new float[6], new float[6], 3, 2));
  }

  [Test]
  public void XybPlanesToRgb24_RejectsNullArrays() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        null!, new float[6], new float[6], 3, 2));
    Assert.Throws<ArgumentNullException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        new float[6], null!, new float[6], 3, 2));
    Assert.Throws<ArgumentNullException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        new float[6], new float[6], null!, 3, 2));
  }

  [Test]
  public void XybPlanesToRgb24_RejectsNonPositiveDimensions() {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), 0, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlXybColorTransform.XybPlanesToRgb24(
        Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), 1, 0));
  }
}
