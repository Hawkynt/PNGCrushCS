using System;
using FileFormat.Core;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffShamFile_DefaultWidth_Is320() {
    var file = new IffShamFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_DefaultHeight_Is200() {
    var file = new IffShamFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_DefaultRawData_IsEmpty() {
    var file = new IffShamFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_InitProperties_StoreCorrectly() {
    var file = new IffShamFile { Width = 640, Height = 400, RawData = new byte[] { 1, 2, 3 } };
    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(400));
    Assert.That(file.RawData.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffShamFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<NotSupportedException>(() => IffShamFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_ToRawImage_ReturnsRgb24Format() {
    var file = new IffShamFile { RawData = new byte[12] };
    var raw = IffShamFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_ToRawImage_HasCorrectDimensions() {
    var file = new IffShamFile { Width = 320, Height = 200, RawData = new byte[12] };
    var raw = IffShamFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void IffShamFile_ToRawImage_AllBlack_StubImplementation() {
    var file = new IffShamFile { Width = 4, Height = 4, RawData = new byte[12] };
    var raw = IffShamFile.ToRawImage(file);
    for (var i = 0; i < raw.PixelData.Length; ++i)
      Assert.That(raw.PixelData[i], Is.EqualTo(0), $"Pixel byte {i} should be 0 (stub returns all-black)");
  }
}
