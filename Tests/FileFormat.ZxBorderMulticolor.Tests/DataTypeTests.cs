using System;
using FileFormat.ZxBorderMulticolor;
using FileFormat.Core;

namespace FileFormat.ZxBorderMulticolor.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ZxBorderMulticolorFile_DefaultBitmapData_IsEmpty() {
    var file = new ZxBorderMulticolorFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxBorderMulticolorFile_DefaultAttributeData_IsEmpty() {
    var file = new ZxBorderMulticolorFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxBorderMulticolorFile_DefaultBorderData_IsEmpty() {
    var file = new ZxBorderMulticolorFile();
    Assert.That(file.BorderData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxBorderMulticolorFile_FixedWidth_Is256() {
    var file = new ZxBorderMulticolorFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void ZxBorderMulticolorFile_FixedHeight_Is192() {
    var file = new ZxBorderMulticolorFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is11904() {
    Assert.That(ZxBorderMulticolorReader.FileSize, Is.EqualTo(11904));
  }

  [Test]
  [Category("Unit")]
  public void BitmapSize_Is6144() {
    Assert.That(ZxBorderMulticolorReader.BitmapSize, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void AttributeSize_Is1536() {
    Assert.That(ZxBorderMulticolorReader.AttributeSize, Is.EqualTo(1536));
  }

  [Test]
  [Category("Unit")]
  public void BorderSize_Is4224() {
    Assert.That(ZxBorderMulticolorReader.BorderSize, Is.EqualTo(4224));
  }

  [Test]
  [Category("Unit")]
  public void AttributeColumns_Is32() {
    Assert.That(ZxBorderMulticolorReader.AttributeColumns, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void AttributeRows_Is48() {
    Assert.That(ZxBorderMulticolorReader.AttributeRows, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 192 * 3],
    };
    Assert.Throws<NotSupportedException>(() => ZxBorderMulticolorFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void NormalPalette_Has8Entries() {
    Assert.That(ZxBorderMulticolorFile.NormalPalette.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void BrightPalette_Has8Entries() {
    Assert.That(ZxBorderMulticolorFile.BrightPalette.Length, Is.EqualTo(8));
  }
}
