using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsBlockInfoTests {

  [Test]
  [Category("Unit")]
  public void GetBlockSize_Dxt1_Returns8() {
    Assert.That(DdsBlockInfo.GetBlockSize(DdsFormat.Dxt1), Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void GetBlockSize_Dxt3_Returns16() {
    Assert.That(DdsBlockInfo.GetBlockSize(DdsFormat.Dxt3), Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void GetBlockSize_Dxt5_Returns16() {
    Assert.That(DdsBlockInfo.GetBlockSize(DdsFormat.Dxt5), Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void GetBlockSize_Rgba_Returns0() {
    Assert.That(DdsBlockInfo.GetBlockSize(DdsFormat.Rgba), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Dxt1_4x4_Returns8() {
    var size = DdsBlockInfo.CalculateMipSize(4, 4, DdsFormat.Dxt1);
    Assert.That(size, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Dxt1_8x8_Returns32() {
    // 2x2 blocks x 8 bytes = 32
    var size = DdsBlockInfo.CalculateMipSize(8, 8, DdsFormat.Dxt1);
    Assert.That(size, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Dxt5_4x4_Returns16() {
    var size = DdsBlockInfo.CalculateMipSize(4, 4, DdsFormat.Dxt5);
    Assert.That(size, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Rgba_4x4_Returns64() {
    var size = DdsBlockInfo.CalculateMipSize(4, 4, DdsFormat.Rgba);
    Assert.That(size, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Rgb_4x4_Returns48() {
    var size = DdsBlockInfo.CalculateMipSize(4, 4, DdsFormat.Rgb);
    Assert.That(size, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Dxt1_1x1_Returns8() {
    // Minimum 1 block
    var size = DdsBlockInfo.CalculateMipSize(1, 1, DdsFormat.Dxt1);
    Assert.That(size, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CalculateMipSize_Dxt1_5x5_Returns32() {
    // 2x2 blocks (5 rounds up to 2 blocks per axis) x 8 bytes = 32
    var size = DdsBlockInfo.CalculateMipSize(5, 5, DdsFormat.Dxt1);
    Assert.That(size, Is.EqualTo(32));
  }
}
