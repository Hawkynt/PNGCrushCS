using System;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class BmpWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgb24_StartsWithBmSignature() {
    var file = new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3],
      ColorMode = BmpColorMode.Rgb24,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'B'));
    Assert.That(bytes[1], Is.EqualTo((byte)'M'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Palette8_IncludesPalette() {
    var palette = new byte[4 * 3]; // 4 colors, RGB triplets
    palette[0] = 255; // color 0: R=255
    palette[3] = 0;   // color 1: R=0
    palette[4] = 255; // color 1: G=255

    var file = new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = new byte[2 * 2],
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = BmpColorMode.Palette8,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(file);

    // Palette starts at offset 54 (14 + 40), each entry is 4 bytes (BGRA)
    // First color: R=255 stored as B=0, G=0, R=255, A=0 in BGRA order
    Assert.That(bytes.Length, Is.GreaterThan(54 + 4 * 4));
    // Verify palette is written (first color's red channel at offset 56)
    Assert.That(bytes[56], Is.EqualTo(255)); // Red component of color 0
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb16_565_UsesBitfields() {
    var file = new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 16,
      PixelData = new byte[2 * 2 * 2],
      ColorMode = BmpColorMode.Rgb16_565,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(file);

    // Compression field at offset 30 (14 + 16) should be 3 (BI_BITFIELDS)
    var compressionValue = BitConverter.ToInt32(bytes, 30);
    Assert.That(compressionValue, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RowPadding_AlignedTo4Bytes() {
    // 3px wide Rgb24 = 9 bytes/row, should be padded to 12
    var file = new BmpFile {
      Width = 3,
      Height = 1,
      BitsPerPixel = 24,
      PixelData = new byte[3 * 3], // 9 bytes
      ColorMode = BmpColorMode.Rgb24,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(file);

    // File should be header (54) + padded row (12) = 66 bytes
    Assert.That(bytes.Length, Is.EqualTo(54 + 12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TopDown_NegativeHeight() {
    var file = new BmpFile {
      Width = 1,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[1 * 2 * 3],
      ColorMode = BmpColorMode.Rgb24,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(file);

    // Height at offset 22 should be -2 for top-down
    var heightValue = BitConverter.ToInt32(bytes, 22);
    Assert.That(heightValue, Is.EqualTo(-2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_MatchesActualLength() {
    var file = new BmpFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 24,
      PixelData = new byte[4 * 3 * 3],
      ColorMode = BmpColorMode.Rgb24,
      RowOrder = BmpRowOrder.BottomUp
    };

    var bytes = BmpWriter.ToBytes(file);

    // File size at offset 2
    var fileSizeField = BitConverter.ToInt32(bytes, 2);
    Assert.That(fileSizeField, Is.EqualTo(bytes.Length));
  }
}
