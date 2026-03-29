using System;
using FileFormat.C128Multi;

namespace FileFormat.C128Multi.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultWidth_Is160() {
    var file = new C128MultiFile();

    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultHeight_Is200() {
    var file = new C128MultiFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultBitmapData_IsEmpty() {
    var file = new C128MultiFile();

    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultScreenData_IsEmpty() {
    var file = new C128MultiFile();

    Assert.That(file.ScreenData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultColorData_IsEmpty() {
    var file = new C128MultiFile();

    Assert.That(file.ColorData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_DefaultBackgroundColor_IsZero() {
    var file = new C128MultiFile();

    Assert.That(file.BackgroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_InitBitmapData_StoresCorrectly() {
    var bitmap = new byte[] { 0xAB, 0xCD };
    var file = new C128MultiFile { BitmapData = bitmap };

    Assert.That(file.BitmapData, Is.SameAs(bitmap));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_InitScreenData_StoresCorrectly() {
    var screen = new byte[] { 0x12, 0x34 };
    var file = new C128MultiFile { ScreenData = screen };

    Assert.That(file.ScreenData, Is.SameAs(screen));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_InitColorData_StoresCorrectly() {
    var color = new byte[] { 0x56, 0x78 };
    var file = new C128MultiFile { ColorData = color };

    Assert.That(file.ColorData, Is.SameAs(color));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_InitBackgroundColor_StoresCorrectly() {
    var file = new C128MultiFile { BackgroundColor = 14 };

    Assert.That(file.BackgroundColor, Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_ExpectedFileSize_Is10240() {
    Assert.That(C128MultiFile.ExpectedFileSize, Is.EqualTo(10240));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_BitmapDataSize_Is8000() {
    Assert.That(C128MultiFile.BitmapDataSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_ScreenDataSize_Is1000() {
    Assert.That(C128MultiFile.ScreenDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_ColorDataSize_Is1000() {
    Assert.That(C128MultiFile.ColorDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_SpareSize_Is240() {
    Assert.That(C128MultiFile.SpareSize, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_PixelWidth_Is160() {
    Assert.That(C128MultiFile.PixelWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_PixelHeight_Is200() {
    Assert.That(C128MultiFile.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 160,
      Height = 200,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[160 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => C128MultiFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void C128MultiFile_SectionSizesAddUp() {
    var total = C128MultiFile.BitmapDataSize
              + C128MultiFile.ScreenDataSize
              + C128MultiFile.ColorDataSize
              + C128MultiFile.SpareSize;

    Assert.That(total, Is.EqualTo(C128MultiFile.ExpectedFileSize));
  }
}
