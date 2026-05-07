using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlVarDctQuant"/> (ISO/IEC 18181-1
/// §G.6, libjxl <c>kDefaultQuantWeights</c> in
/// <c>lib/jxl/quant_weights.cc</c> + <c>GetQuantWeights</c> generator).
/// </summary>
[TestFixture]
public sealed class JxlVarDctQuantTests {

  [Test]
  public void DefaultDct8x8_HasExpectedShape() {
    for (var c = 0; c < 3; c++) {
      var t = JxlVarDctQuant.DefaultDct8x8(c);
      Assert.That(t.Width, Is.EqualTo(8));
      Assert.That(t.Height, Is.EqualTo(8));
      Assert.That(t.Weights.Length, Is.EqualTo(64));
    }
  }

  [Test]
  public void DefaultDct8x8_RejectsBadChannel() {
    Assert.Throws<ArgumentOutOfRangeException>(() => JxlVarDctQuant.DefaultDct8x8(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => JxlVarDctQuant.DefaultDct8x8(3));
  }

  // --- Spot checks of (0,0) entry for each channel.
  //
  // libjxl's `GetQuantWeights` at scaled_distance=0 returns bands[0] verbatim;
  // the public dequant multiplier is 1/bands[0]. The DC-corner weight at (0,0)
  // is therefore the inverse of the channel's first distance-band parameter
  // (3150 for X, 560 for Y, 512 for B; see DequantMatricesLibraryDef::DCT()).

  [Test]
  public void DefaultDct8x8_X_DCCornerEqualsInverseOf3150() {
    var t = JxlVarDctQuant.DefaultDct8x8(0);
    Assert.That(t.Weights[0], Is.EqualTo(1.0f / 3150.0f).Within(1e-5f));
  }

  [Test]
  public void DefaultDct8x8_Y_DCCornerEqualsInverseOf560() {
    var t = JxlVarDctQuant.DefaultDct8x8(1);
    Assert.That(t.Weights[0], Is.EqualTo(1.0f / 560.0f).Within(1e-5f));
  }

  [Test]
  public void DefaultDct8x8_B_DCCornerEqualsInverseOf512() {
    var t = JxlVarDctQuant.DefaultDct8x8(2);
    Assert.That(t.Weights[0], Is.EqualTo(1.0f / 512.0f).Within(1e-5f));
  }

  // --- Spot check the (7,7) high-frequency corner of channel Y.
  //
  // At (7,7) the libjxl scaled_distance ≈ kSqrt2 (the maximum), so the lookup
  // hits bands[5]. For Y, distance bands are {560, 0, -0.3, -0.3, -0.3, -0.3}
  // and Mult(0)=1.0, Mult(-0.3)=1/1.3. Therefore:
  //   bands[5] = 560 * 1 * (1/1.3)^4 ≈ 196.07159
  // and the dequant multiplier is 1/196.07159 ≈ 0.005100237.
  [Test]
  public void DefaultDct8x8_Y_HighFrequencyCornerMatchesBand5() {
    var t = JxlVarDctQuant.DefaultDct8x8(1);
    var expected = 1.0f / (560.0f * (float)Math.Pow(1.0 / 1.3, 4));
    Assert.That(t.Weights[7 * 8 + 7], Is.EqualTo(expected).Within(5e-4f));
  }

  [Test]
  public void DefaultDct8x8_Weights_AreAllPositive() {
    for (var c = 0; c < 3; c++) {
      var t = JxlVarDctQuant.DefaultDct8x8(c);
      foreach (var w in t.Weights)
        Assert.That(w, Is.GreaterThan(0.0f));
    }
  }

  [Test]
  public void DefaultDct8x8_DCCornerIsSmallestAcrossBlock() {
    // For X and Y channels the distance bands are non-increasing (bands[0]
    // is the largest, hence its inverse — the DC dequant weight — is the
    // smallest). The B channel briefly increases at band[3] (Mult(0)=1) so
    // we restrict this property to X and Y.
    for (var c = 0; c < 2; c++) {
      var t = JxlVarDctQuant.DefaultDct8x8(c);
      var dc = t.Weights[0];
      for (var i = 1; i < 64; i++)
        Assert.That(t.Weights[i], Is.GreaterThanOrEqualTo(dc - 1e-6f),
          $"Channel {c} weight[{i}] should be >= DC weight.");
    }
  }

  [Test]
  public void DefaultTableSetXyb_HasThreeChannels() {
    var set = JxlVarDctQuant.DefaultTableSetXyb();
    Assert.That(set.Tables.Length, Is.EqualTo(3));
    for (var c = 0; c < 3; c++) {
      Assert.That(set.Tables[c].Width, Is.EqualTo(8));
      Assert.That(set.Tables[c].Height, Is.EqualTo(8));
      Assert.That(set.Tables[c].Weights.Length, Is.EqualTo(64));
    }
  }

  [Test]
  public void DefaultTableSetXyb_TablesMatchIndividualCalls() {
    var set = JxlVarDctQuant.DefaultTableSetXyb();
    for (var c = 0; c < 3; c++) {
      var ind = JxlVarDctQuant.DefaultDct8x8(c);
      Assert.That(set.Tables[c].Weights, Is.EqualTo(ind.Weights).AsCollection);
    }
  }

  [Test]
  public void Dequantize_ZeroCoefficientsProduceAllZeros() {
    var table = JxlVarDctQuant.DefaultDct8x8(1);
    var coeffs = new short[64];
    var output = new float[64];
    JxlVarDctQuant.Dequantize(coeffs, table, output);
    foreach (var v in output)
      Assert.That(v, Is.EqualTo(0.0f));
  }

  [Test]
  public void Dequantize_AppliesPerCoefficientWeight() {
    var table = JxlVarDctQuant.DefaultDct8x8(1);
    var coeffs = new short[64];
    coeffs[0] = 100;
    coeffs[63] = -50;
    var output = new float[64];
    JxlVarDctQuant.Dequantize(coeffs, table, output);

    Assert.That(output[0], Is.EqualTo(100.0f * table.Weights[0]).Within(1e-6f));
    Assert.That(output[63], Is.EqualTo(-50.0f * table.Weights[63]).Within(1e-6f));
    for (var i = 1; i < 63; i++)
      Assert.That(output[i], Is.EqualTo(0.0f));
  }

  [Test]
  public void Dequantize_RejectsLengthMismatch() {
    var table = JxlVarDctQuant.DefaultDct8x8(1);
    Assert.Throws<ArgumentException>(() =>
      JxlVarDctQuant.Dequantize(new short[10], table, new float[64]));
    Assert.Throws<ArgumentException>(() =>
      JxlVarDctQuant.Dequantize(new short[64], table, new float[10]));
  }

  [Test]
  public void Dequantize_RejectsNullArguments() {
    var table = JxlVarDctQuant.DefaultDct8x8(0);
    Assert.Throws<ArgumentNullException>(() =>
      JxlVarDctQuant.Dequantize(null!, table, new float[64]));
    Assert.Throws<ArgumentNullException>(() =>
      JxlVarDctQuant.Dequantize(new short[64], null!, new float[64]));
    Assert.Throws<ArgumentNullException>(() =>
      JxlVarDctQuant.Dequantize(new short[64], table, null!));
  }
}
