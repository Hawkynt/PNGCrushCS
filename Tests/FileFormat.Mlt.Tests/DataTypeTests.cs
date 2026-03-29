using System;
using FileFormat.Mlt;

namespace FileFormat.Mlt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is160() {
    Assert.That(MltFile.ImageWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(MltFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinFileSize_Is10003() {
    Assert.That(MltFile.MinFileSize, Is.EqualTo(10003));
  }

  [Test]
  [Category("Unit")]
  public void DefaultFile_HasEmptyArrays() {
    var file = new MltFile();

    Assert.That(file.BitmapData, Is.Empty);
    Assert.That(file.ScreenData, Is.Empty);
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

    Assert.Throws<NotSupportedException>(() => MltFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MltFile.ToRawImage(null!));
  }
}
