using System;
using FileFormat.SuperHiresEditor;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is320() {
    Assert.That(SuperHiresEditorFile.ImageWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(SuperHiresEditorFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_Is18000() {
    Assert.That(SuperHiresEditorFile.MinPayloadSize, Is.EqualTo(18000));
  }

  [Test]
  [Category("Unit")]
  public void DefaultFile_HasEmptyArrays() {
    var file = new SuperHiresEditorFile();

    Assert.That(file.Bitmap1, Is.Empty);
    Assert.That(file.Screen1, Is.Empty);
    Assert.That(file.Bitmap2, Is.Empty);
    Assert.That(file.Screen2, Is.Empty);
    Assert.That(file.TrailingData, Is.Empty);
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 320,
      Height = 200,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => SuperHiresEditorFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SuperHiresEditorFile.ToRawImage(null!));
  }
}
