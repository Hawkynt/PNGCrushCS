using FileFormat.MultiPalettePicture;

namespace FileFormat.MultiPalettePicture.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ExpectedFileSize_Is38400() {
    Assert.That(MultiPalettePictureFile.ExpectedFileSize, Is.EqualTo(38400));
  }

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is320() {
    Assert.That(MultiPalettePictureFile.ImageWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(MultiPalettePictureFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void BytesPerScanline_Is160() {
    Assert.That(MultiPalettePictureFile.BytesPerScanline, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void PaletteBytesPerScanline_Is32() {
    Assert.That(MultiPalettePictureFile.PaletteBytesPerScanline, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void RecordSize_Is192() {
    Assert.That(MultiPalettePictureFile.RecordSize, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void NumPlanes_Is4() {
    Assert.That(MultiPalettePictureFile.NumPlanes, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void DefaultWidth_Is320() {
    var file = new MultiPalettePictureFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void DefaultHeight_Is200() {
    var file = new MultiPalettePictureFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty() {
    var file = new MultiPalettePictureFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalettes_Has200Entries() {
    var file = new MultiPalettePictureFile();
    Assert.That(file.Palettes, Has.Length.EqualTo(200));
  }
}
