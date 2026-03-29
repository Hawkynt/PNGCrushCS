using System;
using System.IO;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bppGrayscale_DataPreserved() {
    var width = 4;
    var height = 3;
    var bytesPerRow = (width * 8 + 31) / 32 * 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)((i * 17) % 256);

    var original = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = width,
          Height = height,
          BitsPerPixel = 8,
          DataSize = (uint)pixelData.Length,
          PixelData = pixelData,
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(original);
    var restored = SymbianMbmReader.FromBytes(bytes);

    Assert.That(restored.Bitmaps.Length, Is.EqualTo(1));
    Assert.That(restored.Bitmaps[0].Width, Is.EqualTo(width));
    Assert.That(restored.Bitmaps[0].Height, Is.EqualTo(height));
    Assert.That(restored.Bitmaps[0].BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_24bppRgb_DataPreserved() {
    var width = 3;
    var height = 2;
    var bytesPerRow = (width * 24 + 31) / 32 * 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)((i * 41) % 256);

    var original = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = width,
          Height = height,
          BitsPerPixel = 24,
          DataSize = (uint)pixelData.Length,
          PixelData = pixelData,
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(original);
    var restored = SymbianMbmReader.FromBytes(bytes);

    Assert.That(restored.Bitmaps.Length, Is.EqualTo(1));
    Assert.That(restored.Bitmaps[0].Width, Is.EqualTo(width));
    Assert.That(restored.Bitmaps[0].Height, Is.EqualTo(height));
    Assert.That(restored.Bitmaps[0].BitsPerPixel, Is.EqualTo(24));
    Assert.That(restored.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleBitmaps_AllPreserved() {
    var bitmap1Pixels = new byte[8]; // 4x2 at 8bpp, 4-byte aligned
    var bitmap2Pixels = new byte[24]; // 3x2 at 24bpp, 12-byte aligned -> padded to 12
    bitmap1Pixels[0] = 0xAA;
    bitmap2Pixels[0] = 0xBB;

    var original = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = (uint)bitmap1Pixels.Length,
          PixelData = bitmap1Pixels,
        },
        new SymbianMbmBitmap {
          Width = 3,
          Height = 2,
          BitsPerPixel = 24,
          DataSize = (uint)bitmap2Pixels.Length,
          PixelData = bitmap2Pixels,
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(original);
    var restored = SymbianMbmReader.FromBytes(bytes);

    Assert.That(restored.Bitmaps.Length, Is.EqualTo(2));
    Assert.That(restored.Bitmaps[0].Width, Is.EqualTo(4));
    Assert.That(restored.Bitmaps[0].PixelData[0], Is.EqualTo(0xAA));
    Assert.That(restored.Bitmaps[1].Width, Is.EqualTo(3));
    Assert.That(restored.Bitmaps[1].PixelData[0], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[16]; // 4x4 at 8bpp
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4,
          Height = 4,
          BitsPerPixel = 8,
          DataSize = (uint)pixelData.Length,
          PixelData = pixelData,
        }
      ]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mbm");
    try {
      var bytes = SymbianMbmWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SymbianMbmReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Bitmaps.Length, Is.EqualTo(1));
      Assert.That(restored.Bitmaps[0].Width, Is.EqualTo(4));
      Assert.That(restored.Bitmaps[0].Height, Is.EqualTo(4));
      Assert.That(restored.Bitmaps[0].PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var width = 4;
    var height = 3;
    var rawPixels = new byte[width * height];
    for (var i = 0; i < rawPixels.Length; ++i)
      rawPixels[i] = (byte)(i * 21 % 256);

    var rawImage = new FileFormat.Core.RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = rawPixels,
    };

    var mbmFile = SymbianMbmFile.FromRawImage(rawImage);
    var bytes = SymbianMbmWriter.ToBytes(mbmFile);
    var restored = SymbianMbmReader.FromBytes(bytes);
    var restoredRaw = SymbianMbmFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(width));
    Assert.That(restoredRaw.Height, Is.EqualTo(height));
    Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawPixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var width = 3;
    var height = 2;
    var rawPixels = new byte[width * height * 3];
    for (var i = 0; i < rawPixels.Length; ++i)
      rawPixels[i] = (byte)((i * 13) % 256);

    var rawImage = new FileFormat.Core.RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = rawPixels,
    };

    var mbmFile = SymbianMbmFile.FromRawImage(rawImage);
    var bytes = SymbianMbmWriter.ToBytes(mbmFile);
    var restored = SymbianMbmReader.FromBytes(bytes);
    var restoredRaw = SymbianMbmFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(width));
    Assert.That(restoredRaw.Height, Is.EqualTo(height));
    Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawPixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_8bpp() {
    var original = new SymbianMbmFile {
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

    var bytes = SymbianMbmWriter.ToBytes(original);
    var restored = SymbianMbmReader.FromBytes(bytes);

    Assert.That(restored.Bitmaps[0].PixelData, Is.EqualTo(original.Bitmaps[0].PixelData));
  }
}
