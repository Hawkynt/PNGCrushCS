using System;
using FileFormat.Zoomatic;

namespace FileFormat.Zoomatic.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is160() {
    Assert.That(ZoomaticFile.ImageWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(ZoomaticFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_Is10000() {
    Assert.That(ZoomaticFile.MinPayloadSize, Is.EqualTo(10000));
  }

  [Test]
  [Category("Unit")]
  public void DefaultFile_HasEmptyArrays() {
    var file = new ZoomaticFile();

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

    Assert.Throws<NotSupportedException>(() => ZoomaticFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticFile.ToRawImage(null!));
  }
}
