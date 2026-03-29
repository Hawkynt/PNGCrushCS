using System;
using FileFormat.MsxScreen8;

namespace FileFormat.MsxScreen8.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is256() {
    Assert.That(MsxScreen8File.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is212() {
    Assert.That(MsxScreen8File.FixedHeight, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMagic_Is0xFE() {
    Assert.That(MsxScreen8File.BsaveMagic, Is.EqualTo(0xFE));
  }

  [Test]
  [Category("Unit")]
  public void BsaveHeaderSize_Is7() {
    Assert.That(MsxScreen8File.BsaveHeaderSize, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void PixelDataSize_Is54272() {
    Assert.That(MsxScreen8File.PixelDataSize, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void FileWithHeaderSize_Is54279() {
    Assert.That(MsxScreen8File.FileWithHeaderSize, Is.EqualTo(54279));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty() {
    var file = new MsxScreen8File();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultHasBsaveHeader_IsFalse() {
    var file = new MsxScreen8File();
    Assert.That(file.HasBsaveHeader, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var image = new FileFormat.Core.RawImage {
      Width = 256,
      Height = 212,
      Format = FileFormat.Core.PixelFormat.Indexed8,
      PixelData = new byte[256 * 212],
      PaletteCount = 256,
      Palette = new byte[768]
    };
    Assert.Throws<ArgumentException>(() => MsxScreen8File.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var image = new FileFormat.Core.RawImage {
      Width = 128,
      Height = 128,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[128 * 128 * 3]
    };
    Assert.Throws<ArgumentException>(() => MsxScreen8File.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void Width_Always256() {
    var file = new MsxScreen8File();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Height_Always212() {
    var file = new MsxScreen8File();
    Assert.That(file.Height, Is.EqualTo(212));
  }
}
