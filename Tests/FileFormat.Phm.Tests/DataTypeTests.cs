using System;
using FileFormat.Phm;

namespace FileFormat.Phm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PhmColorMode_HasExpectedValues() {
    Assert.Multiple(() => {
      Assert.That((int)PhmColorMode.Grayscale, Is.EqualTo(0));
      Assert.That((int)PhmColorMode.Rgb, Is.EqualTo(1));
    });
    Assert.That(Enum.GetValues<PhmColorMode>(), Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void PhmFile_Defaults() {
    var file = new PhmFile();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.ColorMode, Is.EqualTo(PhmColorMode.Grayscale));
      Assert.That(file.Scale, Is.EqualTo(0f));
      Assert.That(file.IsLittleEndian, Is.False);
      Assert.That(file.PixelData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void PhmFile_InitProperties() {
    var file = new PhmFile {
      Width = 10, Height = 20, ColorMode = PhmColorMode.Rgb,
      Scale = 1.5f, IsLittleEndian = true,
      PixelData = new Half[600]
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(20));
      Assert.That(file.ColorMode, Is.EqualTo(PhmColorMode.Rgb));
      Assert.That(file.Scale, Is.EqualTo(1.5f));
      Assert.That(file.IsLittleEndian, Is.True);
      Assert.That(file.PixelData, Has.Length.EqualTo(600));
    });
  }

  [Test]
  [Category("Unit")]
  public void PhmFile_FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => PhmFile.FromRawImage(null!));
}
