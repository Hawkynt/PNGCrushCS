using System;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CrackArtResolution_HasExpectedValues() {
    Assert.That((int)CrackArtResolution.Low, Is.EqualTo(0));
    Assert.That((int)CrackArtResolution.Medium, Is.EqualTo(1));
    Assert.That((int)CrackArtResolution.High, Is.EqualTo(2));

    var values = Enum.GetValues<CrackArtResolution>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void CrackArtFile_DefaultPalette_HasLength16() {
    var file = new CrackArtFile();
    Assert.That(file.Palette, Has.Length.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CrackArtFile_DefaultPixelData_IsEmpty() {
    var file = new CrackArtFile();
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }
}
