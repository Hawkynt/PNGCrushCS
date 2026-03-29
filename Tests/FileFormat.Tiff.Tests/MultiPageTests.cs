using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class MultiPageTests {

  [Test]
  [Category("Unit")]
  public void SinglePageFile_HasImageCountOne() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
    };
    Assert.That(TiffFile.ImageCount(file), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void SinglePageFile_HasEmptyPages() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
    };
    Assert.That(file.Pages, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => TiffFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexedOverload_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => TiffFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void ToRawImage_NegativeIndex_Throws() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => TiffFile.ToRawImage(file, -1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexBeyondPageCount_Throws() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => TiffFile.ToRawImage(file, 1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexZero_ReturnsSameAsUnindexedOverload() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = pixelData, ColorMode = TiffColorMode.Rgb,
    };

    var raw0 = TiffFile.ToRawImage(file, 0);
    var rawDefault = TiffFile.ToRawImage(file);

    Assert.That(raw0.Width, Is.EqualTo(rawDefault.Width));
    Assert.That(raw0.Height, Is.EqualTo(rawDefault.Height));
    Assert.That(raw0.Format, Is.EqualTo(rawDefault.Format));
    Assert.That(raw0.PixelData, Is.EqualTo(rawDefault.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void MultiPageFile_ImageCountIncludesPages() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4], ColorMode = TiffColorMode.Grayscale },
        new TiffPage { Width = 3, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[27], ColorMode = TiffColorMode.Rgb },
      ],
    };
    Assert.That(TiffFile.ImageCount(file), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_SecondPage_ReturnCorrectDimensions() {
    var page1Pixels = new byte[4];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 50);

    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var raw = TiffFile.ToRawImage(file, 1);
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImages_ReturnsAllPages() {
    var file = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4], ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var images = _CallToRawImages(file);
    Assert.That(images, Has.Count.EqualTo(2));
    Assert.That(images[0].Width, Is.EqualTo(4));
    Assert.That(images[1].Width, Is.EqualTo(2));
  }

  private static IReadOnlyList<RawImage> _CallToRawImages<T>(T file) where T : IMultiImageFileFormat<T>
    => T.ToRawImages(file);

  [Test]
  [Category("Unit")]
  public void TiffPage_Defaults() {
    var page = new TiffPage();
    Assert.Multiple(() => {
      Assert.That(page.Width, Is.EqualTo(0));
      Assert.That(page.Height, Is.EqualTo(0));
      Assert.That(page.SamplesPerPixel, Is.EqualTo(0));
      Assert.That(page.BitsPerSample, Is.EqualTo(0));
      Assert.That(page.PixelData, Is.Empty);
      Assert.That(page.ColorMap, Is.Null);
      Assert.That(page.ColorMode, Is.EqualTo(TiffColorMode.Original));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoPages_Rgb() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 7);

    var page1Pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 13);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Rgb },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages, Has.Count.EqualTo(1));
    Assert.That(restored.Pages[0].Width, Is.EqualTo(2));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(2));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThreePages_MixedColorModes() {
    var rgbPixels = new byte[4 * 4 * 3];
    for (var i = 0; i < rgbPixels.Length; ++i)
      rgbPixels[i] = (byte)(i * 5);

    var grayPixels = new byte[3 * 3];
    for (var i = 0; i < grayPixels.Length; ++i)
      grayPixels[i] = (byte)(i * 28);

    var rgb2Pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < rgb2Pixels.Length; ++i)
      rgb2Pixels[i] = (byte)(i * 19);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = rgbPixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 3, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = grayPixels, ColorMode = TiffColorMode.Grayscale },
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = rgb2Pixels, ColorMode = TiffColorMode.Rgb },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(TiffFile.ImageCount(restored), Is.EqualTo(3));
    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(rgbPixels));

    Assert.That(restored.Pages[0].Width, Is.EqualTo(3));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(3));
    Assert.That(restored.Pages[0].SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(grayPixels));

    Assert.That(restored.Pages[1].Width, Is.EqualTo(2));
    Assert.That(restored.Pages[1].Height, Is.EqualTo(2));
    Assert.That(restored.Pages[1].SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.Pages[1].PixelData, Is.EqualTo(rgb2Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_IndexZero_MatchesFirstPage() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 11);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 60);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    var raw0 = TiffFile.ToRawImage(restored, 0);
    Assert.That(raw0.Width, Is.EqualTo(4));
    Assert.That(raw0.Height, Is.EqualTo(4));
    Assert.That(raw0.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw0.PixelData, Is.EqualTo(page0Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_IndexOne_ReturnsSecondPage() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 11);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 60);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    var raw1 = TiffFile.ToRawImage(restored, 1);
    Assert.That(raw1.Width, Is.EqualTo(2));
    Assert.That(raw1.Height, Is.EqualTo(2));
    Assert.That(raw1.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw1.PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_IndexOutOfRange_Throws() {
    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = new byte[4 * 4 * 3], ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4], ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.Throws<ArgumentOutOfRangeException>(() => TiffFile.ToRawImage(restored, 2));
  }

  [Test]
  [Category("Integration")]
  public void SinglePage_BackwardCompatibility_NoPages() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = pixelData, ColorMode = TiffColorMode.Rgb,
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Pages, Is.Empty);
    Assert.That(TiffFile.ImageCount(restored), Is.EqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_ViaFile() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 3);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 40);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Grayscale },
      ],
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tiff");
    try {
      File.WriteAllBytes(tempPath, TiffWriter.ToBytes(original));
      var restored = TiffReader.FromFile(new FileInfo(tempPath));

      Assert.That(TiffFile.ImageCount(restored), Is.EqualTo(2));
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
      Assert.That(restored.Pages[0].Width, Is.EqualTo(2));
      Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_ViaStream() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 9);

    var page1Pixels = new byte[3 * 3 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 11);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 3, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Rgb },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = TiffReader.FromStream(ms);

    Assert.That(TiffFile.ImageCount(restored), Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_PixelDataDistinctBetweenPages() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = 0xAA;

    var page1Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = 0x55;

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Rgb },
      ],
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.Not.EqualTo(restored.Pages[0].PixelData));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_WithCompression() {
    var page0Pixels = new byte[4 * 4 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 7);

    var page1Pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 17);

    var original = new TiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 3, BitsPerSample = 8,
      PixelData = page0Pixels, ColorMode = TiffColorMode.Rgb,
      Pages = [
        new TiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = page1Pixels, ColorMode = TiffColorMode.Rgb },
      ],
    };

    var bytes = TiffWriter.ToBytes(original, TiffCompression.Lzw);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(TiffFile.ImageCount(restored), Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }
}
