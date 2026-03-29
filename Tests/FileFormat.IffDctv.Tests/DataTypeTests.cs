using System;
using FileFormat.Core;
using FileFormat.IffDctv;

namespace FileFormat.IffDctv.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffDctvFile_DefaultWidth_Is320() {
    var file = new IffDctvFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_DefaultHeight_Is200() {
    var file = new IffDctvFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_DefaultRawData_IsEmpty() {
    var file = new IffDctvFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_InitProperties_StoreCorrectly() {
    var file = new IffDctvFile { Width = 640, Height = 400, RawData = new byte[] { 1, 2, 3 } };
    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(400));
    Assert.That(file.RawData.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<NotSupportedException>(() => IffDctvFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_ToRawImage_ReturnsRgb24Format() {
    var file = new IffDctvFile { RawData = new byte[12] };
    var raw = IffDctvFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void IffDctvFile_ToRawImage_HasCorrectDimensions() {
    var file = new IffDctvFile { Width = 320, Height = 200, RawData = new byte[12] };
    var raw = IffDctvFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }
}
