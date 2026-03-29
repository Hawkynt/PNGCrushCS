using System;
using FileFormat.ChampionsInterlace;

namespace FileFormat.ChampionsInterlace.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is160() {
    Assert.That(ChampionsInterlaceFile.ImageWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(ChampionsInterlaceFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is19003() {
    Assert.That(ChampionsInterlaceFile.FileSize, Is.EqualTo(19003));
  }

  [Test]
  [Category("Unit")]
  public void DefaultFile_HasEmptyArrays() {
    var file = new ChampionsInterlaceFile();

    Assert.That(file.Bitmap1, Is.Empty);
    Assert.That(file.Screen1, Is.Empty);
    Assert.That(file.ColorData, Is.Empty);
    Assert.That(file.Bitmap2, Is.Empty);
    Assert.That(file.Screen2, Is.Empty);
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

    Assert.Throws<NotSupportedException>(() => ChampionsInterlaceFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ChampionsInterlaceFile.ToRawImage(null!));
  }
}
