using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_DefaultBitmaps_IsEmpty() {
    var file = new SymbianMbmFile();

    Assert.That(file.Bitmaps, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_Uid1_Is0x10000037() {
    Assert.That(SymbianMbmFile.Uid1, Is.EqualTo(0x10000037u));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_Uid2_Is0x10000000() {
    Assert.That(SymbianMbmFile.Uid2, Is.EqualTo(0x10000000u));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_Uid3_IsZero() {
    Assert.That(SymbianMbmFile.Uid3, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_HeaderSize_Is20() {
    Assert.That(SymbianMbmFile.HeaderSize, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_BitmapHeaderSize_Is40() {
    Assert.That(SymbianMbmFile.BitmapHeaderSize, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_MinimumFileSize_Is24() {
    Assert.That(SymbianMbmFile.MinimumFileSize, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_PrimaryExtension_IsMbm() {
    // Verify via interface implementation by building and reading back
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 2,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = 4,
          PixelData = new byte[4],
        }
      ]
    };
    var bytes = SymbianMbmWriter.ToBytes(file);
    var restored = SymbianMbmReader.FromBytes(bytes);
    Assert.That(restored.Bitmaps.Length, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_ToRawImage_NoBitmaps_ThrowsInvalidDataException() {
    var file = new SymbianMbmFile { Bitmaps = [] };

    Assert.Throws<InvalidDataException>(() => SymbianMbmFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4 * 4 * 4],
    };

    Assert.Throws<ArgumentException>(() => SymbianMbmFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_ToRawImage_8bpp_ReturnsGray8() {
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = 8,
          PixelData = new byte[8],
        }
      ]
    };

    var raw = SymbianMbmFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(4));
    Assert.That(raw.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_ToRawImage_24bpp_ReturnsRgb24() {
    var bytesPerRow = (3 * 24 + 31) / 32 * 4;
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 3,
          Height = 2,
          BitsPerPixel = 24,
          DataSize = (uint)(bytesPerRow * 2),
          PixelData = new byte[bytesPerRow * 2],
        }
      ]
    };

    var raw = SymbianMbmFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(3));
    Assert.That(raw.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmBitmap_DefaultPixelData_IsEmpty() {
    var bmp = new SymbianMbmBitmap();

    Assert.That(bmp.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmBitmap_DefaultValues() {
    var bmp = new SymbianMbmBitmap();

    Assert.That(bmp.Width, Is.EqualTo(0));
    Assert.That(bmp.Height, Is.EqualTo(0));
    Assert.That(bmp.BitsPerPixel, Is.EqualTo(0));
    Assert.That(bmp.ColorMode, Is.EqualTo(0u));
    Assert.That(bmp.Compression, Is.EqualTo(0u));
    Assert.That(bmp.PaletteSize, Is.EqualTo(0u));
    Assert.That(bmp.DataSize, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmBitmap_InitProperties_StoreCorrectly() {
    var pixelData = new byte[] { 0xAA, 0xBB };
    var bmp = new SymbianMbmBitmap {
      Width = 10,
      Height = 20,
      BitsPerPixel = 24,
      ColorMode = 1,
      Compression = 2,
      PaletteSize = 3,
      DataSize = 4,
      PixelData = pixelData,
    };

    Assert.That(bmp.Width, Is.EqualTo(10));
    Assert.That(bmp.Height, Is.EqualTo(20));
    Assert.That(bmp.BitsPerPixel, Is.EqualTo(24));
    Assert.That(bmp.ColorMode, Is.EqualTo(1u));
    Assert.That(bmp.Compression, Is.EqualTo(2u));
    Assert.That(bmp.PaletteSize, Is.EqualTo(3u));
    Assert.That(bmp.DataSize, Is.EqualTo(4u));
    Assert.That(bmp.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void SymbianMbmFile_ToRawImage_ClonesPixelData() {
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = 8,
          PixelData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 },
        }
      ]
    };

    var raw1 = SymbianMbmFile.ToRawImage(file);
    var raw2 = SymbianMbmFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
