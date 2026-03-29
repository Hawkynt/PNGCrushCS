using System;
using FileFormat.DaliST;

namespace FileFormat.DaliST.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DaliSTResolution_HasExpectedValues() {
    Assert.Multiple(() => {
      Assert.That((int)DaliSTResolution.Low, Is.EqualTo(0));
      Assert.That((int)DaliSTResolution.Medium, Is.EqualTo(1));
      Assert.That((int)DaliSTResolution.High, Is.EqualTo(2));
    });

    var values = Enum.GetValues<DaliSTResolution>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_ExpectedFileSize_Is32032() {
    Assert.That(DaliSTFile.ExpectedFileSize, Is.EqualTo(32032));
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_PaletteSize_Is32() {
    Assert.That(DaliSTFile.PaletteSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_PlanarDataSize_Is32000() {
    Assert.That(DaliSTFile.PlanarDataSize, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_DefaultPalette_Has16Entries() {
    var file = new DaliSTFile();
    Assert.That(file.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_DefaultPixelData_IsEmpty() {
    var file = new DaliSTFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DaliSTFile_InitProperties_Settable() {
    var file = new DaliSTFile {
      Width = 640,
      Height = 400,
      Resolution = DaliSTResolution.High,
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(400));
      Assert.That(file.Resolution, Is.EqualTo(DaliSTResolution.High));
    });
  }
}
