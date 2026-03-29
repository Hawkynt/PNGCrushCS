using System;
using FileFormat.Core;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class PdfImageTests {

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_ValidIndex_Returns() {
    var file = new PdfFile {
      Images = [
        new PdfImage { Width = 2, Height = 2, BitsPerComponent = 8, ColorSpace = PdfColorSpace.DeviceRGB, PixelData = new byte[12] },
        new PdfImage { Width = 3, Height = 3, BitsPerComponent = 8, ColorSpace = PdfColorSpace.DeviceGray, PixelData = new byte[9] },
      ],
    };
    var raw = PdfFile.ToRawImage(file, 1);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(3));
      Assert.That(raw.Height, Is.EqualTo(3));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_OutOfRange_Throws() {
    var file = new PdfFile {
      Images = [new PdfImage { Width = 1, Height = 1, PixelData = new byte[3] }],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => PdfFile.ToRawImage(file, 5));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_NegativeIndex_Throws() {
    var file = new PdfFile {
      Images = [new PdfImage { Width = 1, Height = 1, PixelData = new byte[3] }],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => PdfFile.ToRawImage(file, -1));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_CreatesRgbImage() {
    var raw = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[12] };
    var file = PdfFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.Images.Count, Is.EqualTo(1));
      Assert.That(file.Images[0].ColorSpace, Is.EqualTo(PdfColorSpace.DeviceRGB));
      Assert.That(file.Images[0].Width, Is.EqualTo(2));
      Assert.That(file.Images[0].Height, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_CreatesGrayImage() {
    var raw = new RawImage { Width = 3, Height = 2, Format = PixelFormat.Gray8, PixelData = new byte[6] };
    var file = PdfFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.Images.Count, Is.EqualTo(1));
      Assert.That(file.Images[0].ColorSpace, Is.EqualTo(PdfColorSpace.DeviceGray));
      Assert.That(file.Images[0].Width, Is.EqualTo(3));
      Assert.That(file.Images[0].Height, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[12];
    pixels[0] = 99;
    var raw = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = PdfFile.FromRawImage(raw);
    pixels[0] = 0;
    Assert.That(file.Images[0].PixelData[0], Is.EqualTo(99));
    Assert.That(file.Images[0].PixelData, Is.Not.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsPdf() {
    var map = typeof(PdfFile).GetInterfaceMap(typeof(IImageFileFormat<PdfFile>));
    string? ext = null;
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains("PrimaryExtension"))
        ext = (string)method.Invoke(null, null)!;
    Assert.That(ext, Is.EqualTo(".pdf"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsPdf() {
    var map = typeof(PdfFile).GetInterfaceMap(typeof(IImageFileFormat<PdfFile>));
    string[]? exts = null;
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains("FileExtensions"))
        exts = (string[])method.Invoke(null, null)!;
    Assert.That(exts, Does.Contain(".pdf"));
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_ValidPdfHeader_ReturnsTrue() {
    var header = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
    var result = _CallMatchesSignature(header);
    Assert.That(result, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_InvalidHeader_ReturnsFalse() {
    var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    var result = _CallMatchesSignature(header);
    Assert.That(result, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_TooShort_ReturnsNull() {
    var header = new byte[] { 0x25, 0x50 };
    var result = _CallMatchesSignature(header);
    Assert.That(result, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void CmykImage_ToRawImage_ConvertsToRgb24() {
    var cmykPixels = new byte[] { 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0 };
    var file = new PdfFile {
      Images = [new PdfImage {
        Width = 3, Height = 1, BitsPerComponent = 8,
        ColorSpace = PdfColorSpace.DeviceCMYK,
        PixelData = cmykPixels,
      }],
    };
    var raw = PdfFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  private static bool? _CallMatchesSignature(byte[] header) {
    var span = header.AsSpan();
    if (span.Length < 4)
      return null;
    return span[0] == 0x25 && span[1] == 0x50 && span[2] == 0x44 && span[3] == 0x46;
  }
}
