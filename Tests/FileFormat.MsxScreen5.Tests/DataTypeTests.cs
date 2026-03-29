using System;
using FileFormat.MsxScreen5;

namespace FileFormat.MsxScreen5.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is256() {
    Assert.That(MsxScreen5File.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FixedHeight_Is212() {
    Assert.That(MsxScreen5File.FixedHeight, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void BsaveMagic_Is0xFE() {
    Assert.That(MsxScreen5File.BsaveMagic, Is.EqualTo(0xFE));
  }

  [Test]
  [Category("Unit")]
  public void BsaveHeaderSize_Is7() {
    Assert.That(MsxScreen5File.BsaveHeaderSize, Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void PixelDataSize_Is27136() {
    Assert.That(MsxScreen5File.PixelDataSize, Is.EqualTo(27136));
  }

  [Test]
  [Category("Unit")]
  public void PaletteSize_Is32() {
    Assert.That(MsxScreen5File.PaletteSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FullDataSize_Is27168() {
    Assert.That(MsxScreen5File.FullDataSize, Is.EqualTo(27168));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmpty() {
    var file = new MsxScreen5File();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_IsNull() {
    var file = new MsxScreen5File();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void DefaultHasBsaveHeader_IsFalse() {
    var file = new MsxScreen5File();
    Assert.That(file.HasBsaveHeader, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void DefaultMsx2Palette_Has32Bytes() {
    Assert.That(MsxScreen5File.DefaultMsx2Palette.Length, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new FileFormat.Core.RawImage {
      Width = 256,
      Height = 212,
      Format = FileFormat.Core.PixelFormat.Indexed8,
      PixelData = new byte[256 * 212],
      PaletteCount = 16,
      Palette = new byte[48]
    };
    Assert.Throws<NotSupportedException>(() => MsxScreen5File.FromRawImage(image));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5File.FromRawImage(null!));
  }
}
