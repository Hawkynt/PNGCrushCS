using FileFormat.Lss16;

namespace FileFormat.Lss16.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Lss16File_DefaultWidth_IsZero() {
    var file = new Lss16File();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_DefaultHeight_IsZero() {
    var file = new Lss16File();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_DefaultPixelData_IsNull() {
    var file = new Lss16File();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_DefaultPalette_IsNull() {
    var file = new Lss16File();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_InitProperties_RoundTrip() {
    var palette = new byte[48];
    palette[0] = 63;
    var pixelData = new byte[] { 1, 2, 3, 4 };
    var file = new Lss16File {
      Width = 4,
      Height = 1,
      Palette = palette,
      PixelData = pixelData,
    };

    Assert.That(file.Width, Is.EqualTo(4));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Palette, Is.SameAs(palette));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_HeaderSize_Is56() {
    Assert.That(Lss16File.HeaderSize, Is.EqualTo(56));
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_PaletteEntryCount_Is16() {
    Assert.That(Lss16File.PaletteEntryCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void Lss16File_PaletteSize_Is48() {
    Assert.That(Lss16File.PaletteSize, Is.EqualTo(48));
  }
}
