using System;
using FileFormat.ColoRix;

namespace FileFormat.ColoRix.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ColoRixCompression_HasExpectedValues() {
    Assert.That((int)ColoRixCompression.None, Is.EqualTo(0));
    Assert.That((int)ColoRixCompression.Rle, Is.EqualTo(1));

    var values = Enum.GetValues<ColoRixCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_DefaultWidth_IsZero() {
    var file = new ColoRixFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_DefaultHeight_IsZero() {
    var file = new ColoRixFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_DefaultPalette_IsEmpty() {
    var file = new ColoRixFile();
    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_DefaultPixelData_IsEmpty() {
    var file = new ColoRixFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_DefaultStorageType_IsNone() {
    var file = new ColoRixFile();
    Assert.That(file.StorageType, Is.EqualTo(ColoRixCompression.None));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_VgaPaletteType_Is0xAF() {
    Assert.That(ColoRixFile.VgaPaletteType, Is.EqualTo(0xAF));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_PaletteSize_Is768() {
    Assert.That(ColoRixFile.PaletteSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ColoRixFile_HeaderSize_Is10() {
    Assert.That(ColoRixFile.HeaderSize, Is.EqualTo(10));
  }
}
