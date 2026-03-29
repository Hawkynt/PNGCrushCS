using System;
using FileFormat.Core;
using FileFormat.MsxVideo;

namespace FileFormat.MsxVideo.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_DefaultWidth_Is256() {
    var file = new MsxVideoFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_DefaultHeight_Is212() {
    var file = new MsxVideoFile();
    Assert.That(file.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_DefaultPixelData_IsEmpty() {
    var file = new MsxVideoFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_FixedWidth_Is256() {
    Assert.That(MsxVideoFile.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_FixedHeight_Is212() {
    Assert.That(MsxVideoFile.FixedHeight, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_ExpectedFileSize_Is54272() {
    Assert.That(MsxVideoFile.ExpectedFileSize, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_InitPixelData_StoresCorrectly() {
    var pixels = new byte[] { 0xAB, 0xCD };
    var file = new MsxVideoFile { PixelData = pixels };
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxVideoFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxVideoFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => MsxVideoFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 212,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[256 * 212 * 4],
    };
    Assert.Throws<ArgumentException>(() => MsxVideoFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_ToRawImage_ReturnsRgb24Format() {
    var file = new MsxVideoFile { PixelData = new byte[54272] };
    var raw = MsxVideoFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_ToRawImage_HasCorrectDimensions() {
    var file = new MsxVideoFile { PixelData = new byte[54272] };
    var raw = MsxVideoFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void MsxVideoFile_ToRawImage_PixelDataSize() {
    var file = new MsxVideoFile { PixelData = new byte[54272] };
    var raw = MsxVideoFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 212 * 3));
  }
}
