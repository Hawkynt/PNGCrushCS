using System;
using FileFormat.MsxScreen2;

namespace FileFormat.MsxScreen2.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is256() {
    Assert.That(MsxScreen2File.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is192() {
    Assert.That(MsxScreen2File.FixedHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMagic_Is0xFE() {
    Assert.That(MsxScreen2File.BsaveMagic, Is.EqualTo(0xFE));
  }

  [Test]
  [Category("Unit")]
  public void BsaveHeaderSize_Is7() {
    Assert.That(MsxScreen2File.BsaveHeaderSize, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void VramDataSize_Is13056() {
    Assert.That(MsxScreen2File.VramDataSize, Is.EqualTo(13056));
  }

  [Test]
  [Category("Unit")]
  public void FileWithHeaderSize_Is13063() {
    Assert.That(MsxScreen2File.FileWithHeaderSize, Is.EqualTo(13063));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPatternGenerator_IsEmpty() {
    var file = new MsxScreen2File();
    Assert.That(file.PatternGenerator, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultColorTable_IsEmpty() {
    var file = new MsxScreen2File();
    Assert.That(file.ColorTable, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPatternNameTable_IsEmpty() {
    var file = new MsxScreen2File();
    Assert.That(file.PatternNameTable, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultHasBsaveHeader_IsFalse() {
    var file = new MsxScreen2File();
    Assert.That(file.HasBsaveHeader, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new FileFormat.Core.RawImage {
      Width = 256,
      Height = 192,
      Format = FileFormat.Core.PixelFormat.Indexed8,
      PixelData = new byte[256 * 192],
      PaletteCount = 16,
      Palette = new byte[48]
    };
    Assert.Throws<NotSupportedException>(() => MsxScreen2File.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2File.FromRawImage(null!));
  }
}
