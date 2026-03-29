using System;
using FileFormat.Core;
using FileFormat.YJKImage;

namespace FileFormat.YJKImage.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void YJKImageFile_DefaultWidth_Is256() {
    var file = new YJKImageFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_DefaultHeight_Is212() {
    var file = new YJKImageFile();
    Assert.That(file.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_DefaultPixelData_IsEmpty() {
    var file = new YJKImageFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_FixedWidth_Is256() {
    Assert.That(YJKImageFile.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_FixedHeight_Is212() {
    Assert.That(YJKImageFile.FixedHeight, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_ExpectedFileSize_Is54272() {
    Assert.That(YJKImageFile.ExpectedFileSize, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_InitPixelData_StoresCorrectly() {
    var pixels = new byte[] { 0xAB, 0xCD };
    var file = new YJKImageFile { PixelData = pixels };
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YJKImageFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => YJKImageFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 256,
      Height = 212,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 212 * 3],
    };
    Assert.Throws<NotSupportedException>(() => YJKImageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_ToRawImage_ReturnsRgb24Format() {
    var file = new YJKImageFile { PixelData = new byte[54272] };
    var raw = YJKImageFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_ToRawImage_HasCorrectDimensions() {
    var file = new YJKImageFile { PixelData = new byte[54272] };
    var raw = YJKImageFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void YJKImageFile_ToRawImage_PixelDataSize() {
    var file = new YJKImageFile { PixelData = new byte[54272] };
    var raw = YJKImageFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 212 * 3));
  }
}
