using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void GunPaintFile_FixedWidth_Is160() {
    Assert.That(GunPaintFile.FixedWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_FixedHeight_Is200() {
    Assert.That(GunPaintFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_ExpectedFileSize_Is33603() {
    Assert.That(GunPaintFile.ExpectedFileSize, Is.EqualTo(33603));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_DefaultLoadAddress_IsZero() {
    var file = new GunPaintFile();
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_DefaultRawData_IsEmpty() {
    var file = new GunPaintFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_Width_AlwaysReturnsFixedWidth() {
    var file = new GunPaintFile();
    Assert.That(file.Width, Is.EqualTo(GunPaintFile.FixedWidth));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_Height_AlwaysReturnsFixedHeight() {
    var file = new GunPaintFile();
    Assert.That(file.Height, Is.EqualTo(GunPaintFile.FixedHeight));
  }

  [Test]
  [Category("Unit")]
  public void GunPaintFile_RawDataSize_IsFileSizeMinusLoadAddress() {
    Assert.That(GunPaintFile.RawDataSize, Is.EqualTo(GunPaintFile.ExpectedFileSize - GunPaintFile.LoadAddressSize));
  }
}
