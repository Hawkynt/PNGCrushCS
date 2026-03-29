using System;
using FileFormat.JpegLs;

namespace FileFormat.JpegLs.Tests;

[TestFixture]
public sealed class JpegLsCodecTests {

  [Test]
  [Category("Unit")]
  public void CreateDefault_MaxVal255_ReturnsValidCodec() {
    var codec = JpegLsCodec.CreateDefault(255, 0);

    Assert.That(codec.MaxVal, Is.EqualTo(255));
    Assert.That(codec.Near, Is.EqualTo(0));
    Assert.That(codec.Range, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CreateDefault_MaxVal255_DefaultThresholds() {
    JpegLsCodec.ComputeDefaultThresholds(255, 0, out var t1, out var t2, out var t3);

    Assert.That(t1, Is.GreaterThanOrEqualTo(2));
    Assert.That(t2, Is.GreaterThan(t1));
    Assert.That(t3, Is.GreaterThan(t2));
  }

  [Test]
  [Category("Unit")]
  public void ComputeDefaultThresholds_SmallMaxVal_ReturnsValidThresholds() {
    JpegLsCodec.ComputeDefaultThresholds(15, 0, out var t1, out var t2, out var t3);

    Assert.That(t1, Is.GreaterThanOrEqualTo(2));
    Assert.That(t2, Is.GreaterThanOrEqualTo(t1));
    Assert.That(t3, Is.GreaterThanOrEqualTo(t2));
  }

  [Test]
  [Category("Unit")]
  public void ComputeDefaultThresholds_NearLossless_IncreaseThresholds() {
    JpegLsCodec.ComputeDefaultThresholds(255, 0, out var t1Lossless, out var t2Lossless, out var t3Lossless);
    JpegLsCodec.ComputeDefaultThresholds(255, 10, out var t1Near, out var t2Near, out var t3Near);

    Assert.That(t1Near, Is.GreaterThanOrEqualTo(t1Lossless));
    Assert.That(t2Near, Is.GreaterThanOrEqualTo(t2Lossless));
    Assert.That(t3Near, Is.GreaterThanOrEqualTo(t3Lossless));
  }

  [Test]
  [Category("Unit")]
  public void MedPredict_Horizontal_ReturnsMin() {
    // c >= max(a, b) => min(a, b)
    var result = JpegLsCodec.MedPredict(100, 50, 200);
    Assert.That(result, Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void MedPredict_Vertical_ReturnsMax() {
    // c <= min(a, b) => max(a, b)
    var result = JpegLsCodec.MedPredict(100, 200, 50);
    Assert.That(result, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MedPredict_NeitherEdge_ReturnsLinear() {
    // Neither edge case => a + b - c
    var result = JpegLsCodec.MedPredict(100, 150, 120);
    Assert.That(result, Is.EqualTo(100 + 150 - 120));
  }

  [Test]
  [Category("Unit")]
  public void MedPredict_AllEqual_ReturnsSameValue() {
    var result = JpegLsCodec.MedPredict(128, 128, 128);
    Assert.That(result, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void QuantizeContext_AllZeroGradients_ReturnsNegativeOne() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var ctx = codec.QuantizeContext(0, 0, 0, out _);
    Assert.That(ctx, Is.EqualTo(-1));
  }

  [Test]
  [Category("Unit")]
  public void QuantizeContext_SmallPositiveGradient_ReturnsValidContext() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var ctx = codec.QuantizeContext(5, 0, 0, out var negative);

    Assert.That(ctx, Is.GreaterThanOrEqualTo(0));
    Assert.That(ctx, Is.LessThan(JpegLsCodec.RegularContextCount));
    Assert.That(negative, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void QuantizeContext_NegativeGradient_SetNegativeFlag() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var ctx = codec.QuantizeContext(-50, 0, 0, out var negative);

    Assert.That(ctx, Is.GreaterThanOrEqualTo(0));
    Assert.That(negative, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void QuantizeContext_SymmetricGradients_SameContext() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var ctx1 = codec.QuantizeContext(30, 0, 0, out _);
    var ctx2 = codec.QuantizeContext(-30, 0, 0, out _);

    Assert.That(ctx1, Is.EqualTo(ctx2));
  }

  [Test]
  [Category("Unit")]
  public void ComputeK_InitialState_ReturnsSmallK() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var k = codec.ComputeK(0);

    Assert.That(k, Is.GreaterThanOrEqualTo(0));
    Assert.That(k, Is.LessThanOrEqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void UpdateContext_IncreasesN() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var nBefore = codec.N[0];
    codec.UpdateContext(0, 5);
    Assert.That(codec.N[0], Is.EqualTo(nBefore + 1));
  }

  [Test]
  [Category("Unit")]
  public void UpdateContext_AccumulatesAbsoluteError() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    var aBefore = codec.A[0];
    codec.UpdateContext(0, -10);
    Assert.That(codec.A[0], Is.EqualTo(aBefore + 10));
  }

  [Test]
  [Category("Unit")]
  public void UpdateContext_ResetHalving() {
    var codec = JpegLsCodec.CreateDefault(255, 0);

    // Drive N to RESET by updating many times
    for (var i = 0; i < JpegLsCodec.DefaultReset - 1; ++i)
      codec.UpdateContext(0, 1);

    // N should have been halved after hitting RESET
    Assert.That(codec.N[0], Is.LessThanOrEqualTo(JpegLsCodec.DefaultReset / 2 + 1));
  }

  [Test]
  [Category("Unit")]
  public void Clamp_InRange_ReturnsSame() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.Clamp(128), Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Clamp_BelowZero_ReturnsZero() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.Clamp(-10), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Clamp_AboveMaxVal_ReturnsMaxVal() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.Clamp(300), Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void CeilLog2_PowersOfTwo() {
    Assert.That(JpegLsCodec.CeilLog2(1), Is.EqualTo(0));
    Assert.That(JpegLsCodec.CeilLog2(2), Is.EqualTo(1));
    Assert.That(JpegLsCodec.CeilLog2(4), Is.EqualTo(2));
    Assert.That(JpegLsCodec.CeilLog2(8), Is.EqualTo(3));
    Assert.That(JpegLsCodec.CeilLog2(256), Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CeilLog2_NonPowersOfTwo() {
    Assert.That(JpegLsCodec.CeilLog2(3), Is.EqualTo(2));
    Assert.That(JpegLsCodec.CeilLog2(5), Is.EqualTo(3));
    Assert.That(JpegLsCodec.CeilLog2(255), Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void GetJ_ValidIndices_ReturnsExpectedValues() {
    Assert.That(JpegLsCodec.GetJ(0), Is.EqualTo(0));
    Assert.That(JpegLsCodec.GetJ(4), Is.EqualTo(1));
    Assert.That(JpegLsCodec.GetJ(8), Is.EqualTo(2));
    Assert.That(JpegLsCodec.GetJ(12), Is.EqualTo(3));
    Assert.That(JpegLsCodec.GetJ(16), Is.EqualTo(4));
    Assert.That(JpegLsCodec.GetJ(31), Is.EqualTo(15));
  }

  [Test]
  [Category("Unit")]
  public void GetJ_BeyondTable_ReturnsLastValue() {
    Assert.That(JpegLsCodec.GetJ(32), Is.EqualTo(15));
    Assert.That(JpegLsCodec.GetJ(100), Is.EqualTo(15));
  }

  [Test]
  [Category("Unit")]
  public void IsMapInverted_KZeroNegativeBias_ReturnsTrue() {
    var codec = JpegLsCodec.CreateDefault(255, 0);

    // Force bias to be negative enough: B[0] <= -N[0]
    // N[0] = 1 initially, so need B[0] <= -1
    codec.B[0] = -1;

    Assert.That(codec.IsMapInverted(0, 0), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IsMapInverted_KPositive_ReturnsFalse() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.IsMapInverted(1, 0), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ContextArrays_InitializedCorrectly() {
    var codec = JpegLsCodec.CreateDefault(255, 0);

    for (var i = 0; i < JpegLsCodec.TotalContextCount; ++i) {
      Assert.That(codec.N[i], Is.EqualTo(1));
      Assert.That(codec.A[i], Is.GreaterThanOrEqualTo(2));
      Assert.That(codec.B[i], Is.EqualTo(0));
      Assert.That(codec.C[i], Is.EqualTo(0));
    }
  }

  [Test]
  [Category("Unit")]
  public void Range_Lossless_EqualsMaxValPlusOne() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.Range, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Range_NearLossless_SmallerThanLossless() {
    var lossless = JpegLsCodec.CreateDefault(255, 0);
    var nearLossless = JpegLsCodec.CreateDefault(255, 2);
    Assert.That(nearLossless.Range, Is.LessThan(lossless.Range));
  }

  [Test]
  [Category("Unit")]
  public void BPP_MaxVal255_Equals8() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.BPP, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void RunIndex_InitiallyZero() {
    var codec = JpegLsCodec.CreateDefault(255, 0);
    Assert.That(codec.RunIndex, Is.EqualTo(0));
  }
}
