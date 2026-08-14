using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  // Symbian's CBitwiseBitmap::ByteWidth. A scanline is a whole number of 32-bit words, and for the
  // 24-bit mode the word count is additionally rounded up to a multiple of three, so a group of four
  // pixels - twelve bytes, three words - never straddles the end of a row. Every other depth is
  // simply word-aligned. The 24-bit case is the one that differs from plain 4-byte alignment: 61
  // pixels are 183 bytes, which word alignment would make 184 and Symbian makes 192.
  [Test]
  [Category("Unit")]
  [TestCase(61, 1, 8)]
  [TestCase(61, 2, 16)]
  [TestCase(61, 4, 32)]
  [TestCase(61, 8, 64)]
  [TestCase(61, 12, 124)]
  [TestCase(61, 16, 124)]
  [TestCase(61, 24, 192)]
  [TestCase(3, 24, 12)]
  [TestCase(4, 24, 12)]
  [TestCase(5, 24, 24)]
  [TestCase(64, 24, 192)]
  public void ScanLineLength_MatchesSymbianByteWidth(int width, int bitsPerPixel, int expected)
    => Assert.That(SymbianMbmFile.ScanLineLength(width, bitsPerPixel), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void ScanLineLength_UnknownDepth_Throws()
    => Assert.Throws<InvalidDataException>(() => SymbianMbmFile.ScanLineLength(10, 7));

  // Every MBM carries the same three UIDs, so every MBM carries the same checksum. Symbian's
  // TCheckedUid runs a CCITT CRC over the even and the odd bytes of the twelve UID bytes and packs
  // the two results into one word. The value below is the one on the files the converter writes.
  [Test]
  [Category("Unit")]
  public void UidChecksum_MatchesTheValueOnRealFiles()
    => Assert.That(
      SymbianMbmFile.UidChecksum(SymbianMbmFile.Uid1, SymbianMbmFile.Uid2, SymbianMbmFile.Uid3),
      Is.EqualTo(0x47396439u)
    );

  [Test]
  [Category("Unit")]
  public void Uid2_IsTheMultiBitmapFileImageUid()
    => Assert.That(SymbianMbmFile.Uid2, Is.EqualTo(0x10000042u));

  [Test]
  [Category("Unit")]
  public void ToRawImage_NoBitmaps_Throws() {
    var file = new SymbianMbmFile { Bitmaps = [] };
    Assert.Throws<InvalidDataException>(() => SymbianMbmFile.ToRawImage(file));
  }

  // 8 bits with the colour flag set is EColor256, an index into Symbian's fixed palette rather than
  // a grey level. Reading it as grey would hand back the indices, so it is refused by name.
  [Test]
  [Category("Unit")]
  public void ToRawImage_PalettedDepth_Throws() {
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4, Height = 1, BitsPerPixel = 8, ColorMode = 1,
          PixelData = new byte[4], DataSize = 4,
        }
      ]
    };

    Assert.Throws<InvalidDataException>(() => SymbianMbmFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_ReadsRowsAtTheScanLineLength() {
    // 5 pixels at 8bpp is a 5-byte row padded to 8.
    var pixelData = new byte[8 * 2];
    for (var x = 0; x < 5; ++x) {
      pixelData[x] = (byte)(x * 10);
      pixelData[8 + x] = (byte)(100 + x * 10);
    }

    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 5, Height = 2, BitsPerPixel = 8, PixelData = pixelData, DataSize = (uint)pixelData.Length,
        }
      ]
    };

    var image = SymbianMbmFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(image.PixelData, Is.EqualTo(new byte[] { 0, 10, 20, 30, 40, 100, 110, 120, 130, 140 }));
  }

  // The row is 24 bytes for 5 pixels, not 16, and the stored order is B,G,R.
  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_ReadsRowsAtTheScanLineLength() {
    var pixelData = new byte[24 * 2];
    pixelData[0] = 0xFF; // row 0, pixel 0: blue
    pixelData[24] = 0x00; // row 1, pixel 0: yellow
    pixelData[25] = 0xFF;
    pixelData[26] = 0xFF;

    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 5, Height = 2, BitsPerPixel = 24, PixelData = pixelData, DataSize = (uint)pixelData.Length,
        }
      ]
    };

    var image = SymbianMbmFile.ToRawImage(file);

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData[0..3], Is.EqualTo(new byte[] { 0x00, 0x00, 0xFF }));
    Assert.That(image.PixelData[15..18], Is.EqualTo(new byte[] { 0xFF, 0xFF, 0x00 }));
  }
}
