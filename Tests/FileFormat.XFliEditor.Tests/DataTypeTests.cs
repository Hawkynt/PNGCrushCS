using System;
using FileFormat.XFliEditor;

namespace FileFormat.XFliEditor.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is160() {
    Assert.That(XFliEditorFile.ImageWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(XFliEditorFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_Is17000() {
    Assert.That(XFliEditorFile.MinPayloadSize, Is.EqualTo(17000));
  }

  [Test]
  [Category("Unit")]
  public void ScreenBankCount_Is8() {
    Assert.That(XFliEditorFile.ScreenBankCount, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void DefaultFile_HasEmptyArrays() {
    var file = new XFliEditorFile();

    Assert.That(file.BitmapData, Is.Empty);
    Assert.That(file.ScreenBanks, Is.Empty);
    Assert.That(file.ColorData, Is.Empty);
    Assert.That(file.TrailingData, Is.Empty);
    Assert.That(file.LoadAddress, Is.EqualTo(0));
    Assert.That(file.BackgroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 160,
      Height = 200,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[160 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => XFliEditorFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XFliEditorFile.ToRawImage(null!));
  }
}
