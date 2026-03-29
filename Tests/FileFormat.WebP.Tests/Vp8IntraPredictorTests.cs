using System;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8IntraPredictorTests {

  [Test]
  [Category("Unit")]
  public void Predict16x16_DcPred_BothNeighbors_AveragesAll() {
    var dst = new byte[16 * 16];
    var above = new byte[16];
    var left = new byte[16];
    Array.Fill(above, (byte)100);
    Array.Fill(left, (byte)200);
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.DC_PRED, dst, 0, 16, above, left, 0);
    var expected = (byte)((100 * 16 + 200 * 16 + 16) / 32);
    Assert.That(dst[0], Is.EqualTo(expected));
    Assert.That(dst[255], Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_DcPred_NoNeighbors_Uses128() {
    var dst = new byte[16 * 16];
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.DC_PRED, dst, 0, 16, null, null, 0);
    for (var i = 0; i < 256; ++i)
      Assert.That(dst[i], Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_VPred_CopiesAboveToAllRows() {
    var dst = new byte[16 * 16];
    var above = new byte[16];
    for (var i = 0; i < 16; ++i)
      above[i] = (byte)(i * 10);
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.V_PRED, dst, 0, 16, above, null, 0);
    for (var row = 0; row < 16; ++row)
      for (var col = 0; col < 16; ++col)
        Assert.That(dst[row * 16 + col], Is.EqualTo(above[col]));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_HPred_FillsRowsFromLeft() {
    var dst = new byte[16 * 16];
    var left = new byte[16];
    for (var i = 0; i < 16; ++i)
      left[i] = (byte)(i * 15);
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.H_PRED, dst, 0, 16, null, left, 0);
    for (var row = 0; row < 16; ++row)
      for (var col = 0; col < 16; ++col)
        Assert.That(dst[row * 16 + col], Is.EqualTo(left[row]));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_TmPred_ClipsNegativeToZero() {
    var dst = new byte[16 * 16];
    var above = new byte[16];
    var left = new byte[16];
    Array.Fill(above, (byte)10);
    Array.Fill(left, (byte)10);
    byte topLeft = 200;
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.TM_PRED, dst, 0, 16, above, left, topLeft);
    Assert.That(dst[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_TmPred_ClipsOverflowTo255() {
    var dst = new byte[16 * 16];
    var above = new byte[16];
    var left = new byte[16];
    Array.Fill(above, (byte)250);
    Array.Fill(left, (byte)250);
    byte topLeft = 10;
    Vp8IntraPredictor.Predict16x16(Vp8IntraPredictor.TM_PRED, dst, 0, 16, above, left, topLeft);
    Assert.That(dst[0], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Predict16x16_InvalidMode_Throws() {
    var dst = new byte[16 * 16];
    Assert.Throws<ArgumentOutOfRangeException>(
      () => Vp8IntraPredictor.Predict16x16(99, dst, 0, 16, null, null, 0)
    );
  }

  [Test]
  [Category("Unit")]
  public void Predict8x8_DcPred_NoNeighbors_Uses128() {
    var dst = new byte[8 * 8];
    Vp8IntraPredictor.Predict8x8(Vp8IntraPredictor.DC_PRED, dst, 0, 8, null, null, 0);
    for (var i = 0; i < 64; ++i)
      Assert.That(dst[i], Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Predict8x8_VPred_CopiesAboveToAllRows() {
    var dst = new byte[8 * 8];
    var above = new byte[8];
    for (var i = 0; i < 8; ++i)
      above[i] = (byte)(i * 30);
    Vp8IntraPredictor.Predict8x8(Vp8IntraPredictor.V_PRED, dst, 0, 8, above, null, 0);
    for (var row = 0; row < 8; ++row)
      for (var col = 0; col < 8; ++col)
        Assert.That(dst[row * 8 + col], Is.EqualTo(above[col]));
  }

  [Test]
  [Category("Unit")]
  public void Predict8x8_InvalidMode_Throws() {
    var dst = new byte[8 * 8];
    Assert.Throws<ArgumentOutOfRangeException>(
      () => Vp8IntraPredictor.Predict8x8(42, dst, 0, 8, null, null, 0)
    );
  }

  [Test]
  [Category("Unit")]
  public void Predict4x4_DcPred_NoNeighbors_Uses128() {
    var dst = new byte[4 * 4];
    Vp8IntraPredictor.Predict4x4(Vp8IntraPredictor.B_DC_PRED, dst, 0, 4, null, 0, null, 0);
    for (var i = 0; i < 16; ++i)
      Assert.That(dst[i], Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Predict4x4_RdPred_MainDiagonalEqual() {
    var dst = new byte[4 * 4];
    var above = new byte[] { 10, 20, 30, 40 };
    var left = new byte[] { 50, 60, 70, 80 };
    byte topLeft = 5;
    Vp8IntraPredictor.Predict4x4(Vp8IntraPredictor.B_RD_PRED, dst, 0, 4, above, 0, left, topLeft);
    Assert.That(dst[0 * 4 + 0], Is.EqualTo(dst[1 * 4 + 1]));
    Assert.That(dst[1 * 4 + 1], Is.EqualTo(dst[2 * 4 + 2]));
    Assert.That(dst[2 * 4 + 2], Is.EqualTo(dst[3 * 4 + 3]));
  }

  [Test]
  [Category("Unit")]
  public void Predict4x4_HuPred_LastRowIsConstant() {
    var dst = new byte[4 * 4];
    var left = new byte[] { 10, 20, 30, 40 };
    Vp8IntraPredictor.Predict4x4(Vp8IntraPredictor.B_HU_PRED, dst, 0, 4, null, 0, left, 0);
    Assert.That(dst[3 * 4 + 0], Is.EqualTo(left[3]));
    Assert.That(dst[3 * 4 + 3], Is.EqualTo(left[3]));
  }

  [Test]
  [Category("Unit")]
  public void Predict4x4_InvalidMode_Throws() {
    var dst = new byte[4 * 4];
    Assert.Throws<ArgumentOutOfRangeException>(
      () => Vp8IntraPredictor.Predict4x4(99, dst, 0, 4, null, 0, null, 0)
    );
  }

  [Test]
  [Category("Unit")]
  public void Predict4x4_AllTenModes_DoNotThrow() {
    var above = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
    var left = new byte[] { 15, 25, 35, 45 };
    byte topLeft = 5;
    for (var mode = 0; mode <= 9; ++mode) {
      var dst = new byte[4 * 4];
      Assert.DoesNotThrow(
        () => Vp8IntraPredictor.Predict4x4(mode, dst, 0, 4, above, 0, left, topLeft)
      );
    }
  }
}
