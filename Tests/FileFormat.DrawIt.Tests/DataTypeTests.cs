using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void HeaderSize_Is4() {
    Assert.That(DrawItFile.HeaderSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void PaletteEntries_Is256() {
    Assert.That(DrawItFile.PaletteEntries, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void PaletteDataSize_Is768() {
    Assert.That(DrawItFile.PaletteDataSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_IsEmpty() {
    var file = new DrawItFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty() {
    var file = new DrawItFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultWidth_IsZero() {
    var file = new DrawItFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DefaultHeight_IsZero() {
    var file = new DrawItFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void InitProperties_Settable() {
    var file = new DrawItFile {
      Width = 640,
      Height = 480,
      Palette = new byte[768],
      PixelData = new byte[640 * 480]
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(480));
      Assert.That(file.Palette, Has.Length.EqualTo(768));
      Assert.That(file.PixelData, Has.Length.EqualTo(640 * 480));
    });
  }
}
