using FileFormat.BennetYeeFace;
using FileFormat.Core;

namespace FileFormat.BennetYeeFace.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BennetYeeFaceFile_DefaultPixelData_IsNull() {
    var file = new BennetYeeFaceFile();
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void BennetYeeFaceFile_InitProperties_RoundTrip() {
    var pixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55 };
    var file = new BennetYeeFaceFile {
      Width = 16,
      Height = 2,
      PixelData = pixelData
    };

    Assert.That(file.Width, Is.EqualTo(16));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ComputeStride_ByteAligned_ReturnsWordPadded() {
    // 8 pixels: ((8+15)/16)*2 = 2
    Assert.That(BennetYeeFaceFile.ComputeStride(8), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ComputeStride_ExactWord_ReturnsExact() {
    // 16 pixels: ((16+15)/16)*2 = 2
    Assert.That(BennetYeeFaceFile.ComputeStride(16), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ComputeStride_NonAligned_PadsToWord() {
    // 17 pixels: ((17+15)/16)*2 = 4
    Assert.That(BennetYeeFaceFile.ComputeStride(17), Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ComputeStride_SinglePixel_ReturnsTwo() {
    // 1 pixel: ((1+15)/16)*2 = 2
    Assert.That(BennetYeeFaceFile.ComputeStride(1), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteIsBlackWhite() {
    var file = new BennetYeeFaceFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[] { 0xFF, 0x00 }
    };

    var raw = BennetYeeFaceFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette![0], Is.EqualTo(0)); // Black R
    Assert.That(raw.Palette![1], Is.EqualTo(0)); // Black G
    Assert.That(raw.Palette![2], Is.EqualTo(0)); // Black B
    Assert.That(raw.Palette![3], Is.EqualTo(255)); // White R
    Assert.That(raw.Palette![4], Is.EqualTo(255)); // White G
    Assert.That(raw.Palette![5], Is.EqualTo(255)); // White B
  }
}
