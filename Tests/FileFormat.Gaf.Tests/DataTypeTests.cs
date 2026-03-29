using FileFormat.Gaf;

namespace FileFormat.Gaf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultWidth_IsZero() {
    var file = new GafFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultHeight_IsZero() {
    var file = new GafFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultName_IsEmpty() {
    var file = new GafFile();
    Assert.That(file.Name, Is.EqualTo(string.Empty));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultTransparencyIndex_Is9() {
    var file = new GafFile();
    Assert.That(file.TransparencyIndex, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultPixelData_IsEmpty() {
    var file = new GafFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultPalette_IsNull() {
    var file = new GafFile();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultXOffset_IsZero() {
    var file = new GafFile();
    Assert.That(file.XOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_DefaultYOffset_IsZero() {
    var file = new GafFile();
    Assert.That(file.YOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_ToRawImage_UsesGrayscalePaletteWhenNoPalette() {
    var file = new GafFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 127, 200, 255],
    };

    var raw = GafFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette![0], Is.EqualTo(0));     // R of entry 0
    Assert.That(raw.Palette[1], Is.EqualTo(0));      // G of entry 0
    Assert.That(raw.Palette[2], Is.EqualTo(0));      // B of entry 0
    Assert.That(raw.Palette[255 * 3], Is.EqualTo(255));
    Assert.That(raw.Palette[255 * 3 + 1], Is.EqualTo(255));
    Assert.That(raw.Palette[255 * 3 + 2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_ToRawImage_UsesProvidedPalette() {
    var palette = new byte[256 * 3];
    palette[0] = 0xFF;
    palette[1] = 0x00;
    palette[2] = 0x00;

    var file = new GafFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = palette,
    };

    var raw = GafFile.ToRawImage(file);

    Assert.That(raw.Palette![0], Is.EqualTo(0xFF));
    Assert.That(raw.Palette[1], Is.EqualTo(0x00));
    Assert.That(raw.Palette[2], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_FromRawImage_RequiresIndexed8() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[12],
    };

    Assert.Throws<System.ArgumentException>(() => GafFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void GafFile_FromRawImage_PreservesPixelData() {
    var raw = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Indexed8,
      PixelData = [10, 20, 30, 40],
      PaletteCount = 256,
    };

    var file = GafFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }
}
