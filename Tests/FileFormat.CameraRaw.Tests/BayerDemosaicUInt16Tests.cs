using System;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class BayerDemosaicUInt16Tests {

  [Test]
  [Category("Unit")]
  public void AhdUInt16_OutputLength_Correct() {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = 2048;

    var rgb = BayerDemosaic.AhdUInt16(raw, width, height, BayerPattern.RGGB, 4095);

    Assert.That(rgb.Length, Is.EqualTo(width * height * 3));
  }

  [Test]
  [Category("Unit")]
  public void AhdUInt16_AllZeros_OutputAllZeros() {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];

    var rgb = BayerDemosaic.AhdUInt16(raw, width, height, BayerPattern.RGGB, 4095);

    for (var i = 0; i < rgb.Length; ++i)
      Assert.That(rgb[i], Is.EqualTo(0), $"Byte {i} should be zero");
  }

  [Test]
  [Category("Unit")]
  public void AhdUInt16_MaxValue_ScalesTo255() {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = 4095;

    var rgb = BayerDemosaic.AhdUInt16(raw, width, height, BayerPattern.RGGB, 4095);

    // The center pixels (away from edges) should be close to 255
    // Check a pixel in the interior
    var centerIdx = (2 * width + 2) * 3;
    Assert.That(rgb[centerIdx], Is.GreaterThan(200));
    Assert.That(rgb[centerIdx + 1], Is.GreaterThan(200));
    Assert.That(rgb[centerIdx + 2], Is.GreaterThan(200));
  }

  [Test]
  [Category("Unit")]
  public void AhdUInt16_12BitRange_ValuesInRange() {
    var width = 6;
    var height = 6;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (ushort)(i * 100 % 4096);

    var rgb = BayerDemosaic.AhdUInt16(raw, width, height, BayerPattern.RGGB, 4095);

    for (var i = 0; i < rgb.Length; ++i)
      Assert.That(rgb[i], Is.InRange(0, 255));
  }

  [Test]
  [Category("Unit")]
  [TestCase(BayerPattern.RGGB)]
  [TestCase(BayerPattern.BGGR)]
  [TestCase(BayerPattern.GRBG)]
  [TestCase(BayerPattern.GBRG)]
  public void AhdUInt16_AllPatterns_DoNotThrow(BayerPattern pattern) {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (ushort)(i * 200);

    Assert.DoesNotThrow(() => BayerDemosaic.AhdUInt16(raw, width, height, pattern, 65535));
  }

  [Test]
  [Category("Unit")]
  public void AhdUInt16_14Bit_MaxValueCorrect() {
    var width = 4;
    var height = 4;
    var raw = new ushort[width * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = 16383;

    var rgb = BayerDemosaic.AhdUInt16(raw, width, height, BayerPattern.RGGB, 16383);

    // Interior pixels should be close to 255
    var centerIdx = (2 * width + 2) * 3;
    Assert.That(rgb[centerIdx], Is.GreaterThan(200));
  }
}
