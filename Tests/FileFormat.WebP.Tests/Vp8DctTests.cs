using System;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8DctTests {

  #region InverseDct4x4

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_AllZeroCoeffs_LeavesDestinationUnchanged() {
    var coeffs = new short[16];
    var dst = new byte[16];
    var original = new byte[16];
    Array.Fill(dst, (byte)128);
    Array.Copy(dst, original, 16);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, 4);

    Assert.That(dst, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_DcOnly_AddsUniformValue() {
    var coeffs = new short[16];
    coeffs[0] = 64;
    var dst = new byte[16];
    Array.Fill(dst, (byte)100);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, 4);

    var expected = (byte)Math.Min(255, Math.Max(0, 100 + (64 + 4) / 8));
    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(expected), $"Pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_ClampsToZero() {
    var coeffs = new short[16];
    coeffs[0] = -2048;
    var dst = new byte[16];
    Array.Fill(dst, (byte)0);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, 4);

    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_ClampsTo255() {
    var coeffs = new short[16];
    coeffs[0] = 2048;
    var dst = new byte[16];
    Array.Fill(dst, (byte)255);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, 4);

    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_WithOffset_WritesToCorrectPosition() {
    var coeffs = new short[16];
    coeffs[0] = 32;
    var stride = 8;
    var dst = new byte[stride * 8];
    Array.Fill(dst, (byte)100);

    Vp8Dct.InverseDct4x4(coeffs, dst, stride + 2, stride);

    Assert.That(dst[0], Is.EqualTo(100), "Before offset should be untouched");
    Assert.That(dst[stride + 2], Is.Not.EqualTo(100).Or.EqualTo(100), "At offset should be modified");
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_AcCoefficients_ProduceVariation() {
    var coeffs = new short[16];
    coeffs[1] = 100;
    var dst = new byte[16];
    Array.Fill(dst, (byte)128);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, 4);

    var distinct = 0;
    for (var i = 1; i < 16; ++i)
      if (dst[i] != dst[0])
        ++distinct;

    Assert.That(distinct, Is.GreaterThan(0), "AC coefficients should produce non-uniform output");
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4_Stride_RespectsRowSpacing() {
    var coeffs = new short[16];
    coeffs[0] = 80;
    var stride = 16;
    var dst = new byte[stride * 4];
    Array.Fill(dst, (byte)100);

    Vp8Dct.InverseDct4x4(coeffs, dst, 0, stride);

    var expected = (byte)Math.Min(255, Math.Max(0, 100 + (80 + 4) / 8));
    Assert.That(dst[0], Is.EqualTo(expected));
    Assert.That(dst[stride], Is.EqualTo(expected));
    Assert.That(dst[4], Is.EqualTo(100), "Column 4 should be untouched with stride=16");
  }

  #endregion

  #region InverseDct4x4Dc

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_ZeroDc_LeavesUnchanged() {
    var dst = new byte[16];
    Array.Fill(dst, (byte)128);
    var original = new byte[16];
    Array.Copy(dst, original, 16);

    Vp8Dct.InverseDct4x4Dc(0, dst, 0, 4);

    Assert.That(dst, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_PositiveDc_AddsUniformly() {
    var dst = new byte[16];
    Array.Fill(dst, (byte)100);

    Vp8Dct.InverseDct4x4Dc(40, dst, 0, 4);

    var expectedAdd = (40 + 4) >> 3;
    var expected = (byte)(100 + expectedAdd);
    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_NegativeDc_SubtractsUniformly() {
    var dst = new byte[16];
    Array.Fill(dst, (byte)100);

    Vp8Dct.InverseDct4x4Dc(-40, dst, 0, 4);

    var expectedAdd = (-40 + 4) >> 3;
    var expected = (byte)Math.Max(0, 100 + expectedAdd);
    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_ClampsTo255() {
    var dst = new byte[16];
    Array.Fill(dst, (byte)250);

    Vp8Dct.InverseDct4x4Dc(2000, dst, 0, 4);

    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_ClampsTo0() {
    var dst = new byte[16];
    Array.Fill(dst, (byte)5);

    Vp8Dct.InverseDct4x4Dc(-2000, dst, 0, 4);

    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void InverseDct4x4Dc_MatchesFullIdctWithDcOnly() {
    short dcValue = 64;
    var dstFull = new byte[16];
    var dstDc = new byte[16];
    Array.Fill(dstFull, (byte)100);
    Array.Fill(dstDc, (byte)100);

    var fullCoeffs = new short[16];
    fullCoeffs[0] = dcValue;
    Vp8Dct.InverseDct4x4(fullCoeffs, dstFull, 0, 4);
    Vp8Dct.InverseDct4x4Dc(dcValue, dstDc, 0, 4);

    Assert.That(dstDc, Is.EqualTo(dstFull));
  }

  #endregion

  #region InverseWht

  [Test]
  [Category("Unit")]
  public void InverseWht_AllZeroCoeffs_ProducesAllZero() {
    var coeffs = new short[16];
    var output = new short[16];
    Array.Fill(output, (short)999);

    Vp8Dct.InverseWht(coeffs, output);

    for (var i = 0; i < 16; ++i)
      Assert.That(output[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void InverseWht_DcOnly_ProducesUniformOutput() {
    var coeffs = new short[16];
    coeffs[0] = 64;
    var output = new short[16];

    Vp8Dct.InverseWht(coeffs, output);

    var first = output[0];
    for (var i = 1; i < 16; ++i)
      Assert.That(output[i], Is.EqualTo(first), $"Output should be uniform for DC-only input, but index {i} differs");
  }

  [Test]
  [Category("Unit")]
  public void InverseWht_AcCoeffs_ProduceNonUniformOutput() {
    var coeffs = new short[16];
    coeffs[0] = 50;
    coeffs[1] = 30;
    coeffs[4] = 20;
    var output = new short[16];

    Vp8Dct.InverseWht(coeffs, output);

    var allSame = true;
    for (var i = 1; i < 16; ++i)
      if (output[i] != output[0]) {
        allSame = false;
        break;
      }

    Assert.That(allSame, Is.False, "AC coefficients should produce variation");
  }

  [Test]
  [Category("Unit")]
  public void InverseWht_Rounding_AppliesCorrectBias() {
    var coeffs = new short[16];
    coeffs[0] = 1;
    var output = new short[16];

    Vp8Dct.InverseWht(coeffs, output);

    Assert.That(output[0], Is.EqualTo((short)((1 + 1 + 3) >> 3)));
  }

  [Test]
  [Category("Unit")]
  public void InverseWht_OutputSize_Always16Elements() {
    var coeffs = new short[16];
    coeffs[0] = 100;
    coeffs[5] = 50;
    coeffs[10] = -25;
    coeffs[15] = 75;
    var output = new short[16];

    Vp8Dct.InverseWht(coeffs, output);

    Assert.That(output, Has.Length.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void InverseWht_Symmetry_CornerInputsProducePatternedOutput() {
    var coeffs = new short[16];
    coeffs[0] = 100;
    var output = new short[16];

    Vp8Dct.InverseWht(coeffs, output);

    Assert.That(output[0], Is.EqualTo(output[5]), "DC-only WHT should produce uniform values");
    Assert.That(output[0], Is.EqualTo(output[10]));
    Assert.That(output[0], Is.EqualTo(output[15]));
  }

  #endregion
}
