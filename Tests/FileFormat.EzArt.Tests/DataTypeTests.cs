using FileFormat.EzArt;

namespace FileFormat.EzArt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void EzArtFile_FileSize_Is32032() {
    Assert.That(EzArtFile.FileSize, Is.EqualTo(32032));
  }

  [Test]
  [Category("Unit")]
  public void EzArtFile_DefaultWidth_Is320() {
    var file = new EzArtFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void EzArtFile_DefaultHeight_Is200() {
    var file = new EzArtFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void EzArtFile_DefaultPalette_Has16Entries() {
    var file = new EzArtFile();
    Assert.That(file.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void EzArtFile_DefaultPixelData_Has32000Bytes() {
    var file = new EzArtFile();
    Assert.That(file.PixelData, Has.Length.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void EzArtFile_InitProperties_Work() {
    var palette = new short[16];
    palette[0] = 0x0777;
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;

    var file = new EzArtFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData
    };

    Assert.Multiple(() => {
      Assert.That(file.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(file.PixelData[0], Is.EqualTo(0xAA));
    });
  }
}
