using System;
using FileFormat.Core;
using FileFormat.IffHame;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffHameFile_DefaultWidth_Is320() {
    var file = new IffHameFile();
    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_DefaultHeight_Is200() {
    var file = new IffHameFile();
    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_DefaultRawData_IsEmpty() {
    var file = new IffHameFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_InitProperties_StoreCorrectly() {
    var file = new IffHameFile { Width = 640, Height = 400, RawData = new byte[] { 1, 2, 3 } };
    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(400));
    Assert.That(file.RawData.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<NotSupportedException>(() => IffHameFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_ToRawImage_ReturnsRgb24Format() {
    var file = new IffHameFile { RawData = new byte[12] };
    var raw = IffHameFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_ToRawImage_HasCorrectDimensions() {
    var file = new IffHameFile { Width = 320, Height = 200, RawData = new byte[12] };
    var raw = IffHameFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void IffHameFile_ToRawImage_AllBlack_StubImplementation() {
    var file = new IffHameFile { Width = 4, Height = 4, RawData = new byte[12] };
    var raw = IffHameFile.ToRawImage(file);
    for (var i = 0; i < raw.PixelData.Length; ++i)
      Assert.That(raw.PixelData[i], Is.EqualTo(0), $"Pixel byte {i} should be 0 (stub returns all-black)");
  }
}
