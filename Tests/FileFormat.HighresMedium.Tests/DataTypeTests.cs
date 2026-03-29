using FileFormat.HighresMedium;

namespace FileFormat.HighresMedium.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FileSize_Is64064() {
    Assert.That(HighresMediumFile.FileSize, Is.EqualTo(64064));
  }

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is640() {
    Assert.That(HighresMediumFile.ImageWidth, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(HighresMediumFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void NumPlanes_Is2() {
    Assert.That(HighresMediumFile.NumPlanes, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ColorCount_Is4() {
    Assert.That(HighresMediumFile.ColorCount, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette1_Has16Entries() {
    var file = new HighresMediumFile();
    Assert.That(file.Palette1.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette2_Has16Entries() {
    var file = new HighresMediumFile();
    Assert.That(file.Palette2.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData1_Is32000Bytes() {
    var file = new HighresMediumFile();
    Assert.That(file.PixelData1.Length, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData2_Is32000Bytes() {
    var file = new HighresMediumFile();
    Assert.That(file.PixelData2.Length, Is.EqualTo(32000));
  }
}
