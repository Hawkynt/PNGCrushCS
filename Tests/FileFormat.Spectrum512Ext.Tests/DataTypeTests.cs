using FileFormat.Spectrum512Ext;

namespace FileFormat.Spectrum512Ext.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_FileSize_Is51104() {
    Assert.That(Spectrum512ExtFile.FileSize, Is.EqualTo(51104));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_ScanlineCount_Is199() {
    Assert.That(Spectrum512ExtFile.ScanlineCount, Is.EqualTo(199));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_PaletteEntriesPerLine_Is48() {
    Assert.That(Spectrum512ExtFile.PaletteEntriesPerLine, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_DefaultWidth_Is320() {
    var file = new Spectrum512ExtFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_DefaultHeight_Is199() {
    var file = new Spectrum512ExtFile();
    Assert.That(file.Height, Is.EqualTo(199));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_DefaultPixelData_Has32000Bytes() {
    var file = new Spectrum512ExtFile();
    Assert.That(file.PixelData, Has.Length.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void Spectrum512ExtFile_DefaultPalettes_Has199Entries() {
    var file = new Spectrum512ExtFile();
    Assert.That(file.Palettes, Has.Length.EqualTo(199));
  }
}
