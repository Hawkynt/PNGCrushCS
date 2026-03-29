using System;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class RawPreprocessorUInt16Tests {

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_OutputLength_Correct() {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = 2048;

    var result = RawPreprocessor.ProcessToUInt16(raw, width, height, BayerPattern.RGGB, [0], 4095, null, 65535);

    Assert.That(result.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_BlackLevelSubtraction() {
    var raw = new ushort[] { 100, 100, 100, 100 };
    var result = RawPreprocessor.ProcessToUInt16(raw, 2, 2, BayerPattern.RGGB, [50], 4095, null, 4095);

    // After subtracting black level of 50 from value 100: normalized = 50/(4095-50) * 4095
    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_WhiteBalance_Applied() {
    // Create a simple 2x2 RGGB image where all values are the same
    var raw = new ushort[] { 1000, 1000, 1000, 1000 };
    // Red multiplier = 2.0, Green = 1.0, Blue = 0.5
    float[] wb = [2.0f, 1.0f, 0.5f];

    var result = RawPreprocessor.ProcessToUInt16(raw, 2, 2, BayerPattern.RGGB, [0], 4095, wb, 4095);

    // (0,0)=R: 1000 * 2.0 / 4095 * 4095 = 2000 -> clamped to 4095 if exceeded
    // (1,0)=G: 1000 * 1.0 / 4095 * 4095 = 1000
    // (0,1)=G: 1000 * 1.0 / 4095 * 4095 = 1000
    // (1,1)=B: 1000 * 0.5 / 4095 * 4095 = 500
    Assert.That(result[0], Is.GreaterThan(result[1])); // R > G
    Assert.That(result[1], Is.GreaterThan(result[3])); // G > B
  }

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_MaxValueRespected() {
    var raw = new ushort[] { 4095, 4095, 4095, 4095 };
    var result = RawPreprocessor.ProcessToUInt16(raw, 2, 2, BayerPattern.RGGB, [0], 4095, null, 65535);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.EqualTo(65535));
  }

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_AllZeros_OutputAllZeros() {
    var raw = new ushort[4];
    var result = RawPreprocessor.ProcessToUInt16(raw, 2, 2, BayerPattern.RGGB, [0], 4095, null, 65535);

    for (var i = 0; i < result.Length; ++i)
      Assert.That(result[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ProcessToUInt16_PerCfaBlackLevel() {
    // 4 separate black levels for the 4 CFA positions
    var raw = new ushort[] { 200, 200, 200, 200 };
    int[] blackLevel = [100, 50, 50, 100];

    var result = RawPreprocessor.ProcessToUInt16(raw, 2, 2, BayerPattern.RGGB, blackLevel, 4095, null, 4095);

    // (0,0) has black=100, (1,0) has black=50, (0,1) has black=50, (1,1) has black=100
    // So (1,0) and (0,1) should be larger than (0,0) and (1,1)
    Assert.That(result[1], Is.GreaterThan(result[0]));
    Assert.That(result[2], Is.GreaterThan(result[3]));
  }
}
