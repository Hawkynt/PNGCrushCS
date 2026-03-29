using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.BigTiff;
using FileFormat.Core;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class MultiPageTests {

  [Test]
  [Category("Unit")]
  public void SinglePageFile_HasImageCountOne() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
    };
    Assert.That(BigTiffFile.ImageCount(file), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void SinglePageFile_HasEmptyPages() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
    };
    Assert.That(file.Pages, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexedOverload_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void ToRawImage_NegativeIndex_Throws() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => BigTiffFile.ToRawImage(file, -1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexBeyondPageCount_Throws() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
    };
    Assert.Throws<ArgumentOutOfRangeException>(() => BigTiffFile.ToRawImage(file, 1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IndexZero_ReturnsSameAsUnindexedOverload() {
    var pixelData = new byte[16];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17);

    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = pixelData,
    };

    var raw0 = BigTiffFile.ToRawImage(file, 0);
    var rawDefault = BigTiffFile.ToRawImage(file);

    Assert.That(raw0.Width, Is.EqualTo(rawDefault.Width));
    Assert.That(raw0.Height, Is.EqualTo(rawDefault.Height));
    Assert.That(raw0.Format, Is.EqualTo(rawDefault.Format));
    Assert.That(raw0.PixelData, Is.EqualTo(rawDefault.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void MultiPageFile_ImageCountIncludesPages() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4] },
        new BigTiffPage { Width = 3, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = new byte[27], PhotometricInterpretation = BigTiffFile.PhotometricRgb },
      ],
    };
    Assert.That(BigTiffFile.ImageCount(file), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_SecondPage_ReturnsCorrectDimensions() {
    var page1Pixels = new byte[4];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 50);

    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = page1Pixels },
      ],
    };

    var raw = BigTiffFile.ToRawImage(file, 1);
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_RgbPage_ReturnsRgb24() {
    var pagePixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pagePixels.Length; ++i)
      pagePixels[i] = (byte)(i * 21);

    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8, PixelData = pagePixels, PhotometricInterpretation = BigTiffFile.PhotometricRgb },
      ],
    };

    var raw = BigTiffFile.ToRawImage(file, 1);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pagePixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImages_ReturnsAllPages() {
    var file = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8, PixelData = new byte[4] },
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
  public void BigTiffPage_Defaults() {
    var page = new BigTiffPage();
    Assert.Multiple(() => {
      Assert.That(page.Width, Is.EqualTo(0));
      Assert.That(page.Height, Is.EqualTo(0));
      Assert.That(page.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(page.BitsPerSample, Is.EqualTo(8));
      Assert.That(page.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricMinIsBlack));
      Assert.That(page.PixelData, Is.Empty);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoPages_Gray8() {
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 17 % 256);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 63);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

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
  public void RoundTrip_TwoPages_Rgb24() {
    var page0Pixels = new byte[4 * 3 * 3];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 7 % 256);

    var page1Pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 19 % 256);

    var original = new BigTiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricRgb));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages, Has.Count.EqualTo(1));
    Assert.That(restored.Pages[0].SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThreePages_MixedModes() {
    var grayPixels = new byte[4 * 4];
    for (var i = 0; i < grayPixels.Length; ++i)
      grayPixels[i] = (byte)(i * 15);

    var rgbPixels = new byte[3 * 3 * 3];
    for (var i = 0; i < rgbPixels.Length; ++i)
      rgbPixels[i] = (byte)(i * 9);

    var gray2Pixels = new byte[2 * 2];
    for (var i = 0; i < gray2Pixels.Length; ++i)
      gray2Pixels[i] = (byte)(i * 60);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = grayPixels,
      Pages = [
        new BigTiffPage { Width = 3, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = rgbPixels },
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = gray2Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.That(BigTiffFile.ImageCount(restored), Is.EqualTo(3));

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(grayPixels));

    Assert.That(restored.Pages[0].Width, Is.EqualTo(3));
    Assert.That(restored.Pages[0].Height, Is.EqualTo(3));
    Assert.That(restored.Pages[0].SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(rgbPixels));

    Assert.That(restored.Pages[1].Width, Is.EqualTo(2));
    Assert.That(restored.Pages[1].Height, Is.EqualTo(2));
    Assert.That(restored.Pages[1].SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.Pages[1].PixelData, Is.EqualTo(gray2Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_IndexZero_MatchesFirstPage() {
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 11);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 60);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    var raw0 = BigTiffFile.ToRawImage(restored, 0);
    Assert.That(raw0.Width, Is.EqualTo(4));
    Assert.That(raw0.Height, Is.EqualTo(4));
    Assert.That(raw0.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw0.PixelData, Is.EqualTo(page0Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_IndexOne_ReturnsSecondPage() {
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 11);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 60);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    var raw1 = BigTiffFile.ToRawImage(restored, 1);
    Assert.That(raw1.Width, Is.EqualTo(2));
    Assert.That(raw1.Height, Is.EqualTo(2));
    Assert.That(raw1.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw1.PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_IndexOutOfRange_Throws() {
    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[4] },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.Throws<ArgumentOutOfRangeException>(() => BigTiffFile.ToRawImage(restored, 2));
  }

  [Test]
  [Category("Integration")]
  public void SinglePage_BackwardCompatibility_NoPages() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = pixelData,
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Pages, Is.Empty);
    Assert.That(BigTiffFile.ImageCount(restored), Is.EqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_ViaFile() {
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 3);

    var page1Pixels = new byte[2 * 2];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 40);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".btf");
    try {
      File.WriteAllBytes(tempPath, BigTiffWriter.ToBytes(original));
      var restored = BigTiffReader.FromFile(new FileInfo(tempPath));

      Assert.That(BigTiffFile.ImageCount(restored), Is.EqualTo(2));
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
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = (byte)(i * 9);

    var page1Pixels = new byte[3 * 3];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = (byte)(i * 28);

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 3, Height = 3, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = BigTiffReader.FromStream(ms);

    Assert.That(BigTiffFile.ImageCount(restored), Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_PixelDataDistinctBetweenPages() {
    var page0Pixels = new byte[4 * 4];
    for (var i = 0; i < page0Pixels.Length; ++i)
      page0Pixels[i] = 0xAA;

    var page1Pixels = new byte[4 * 4];
    for (var i = 0; i < page1Pixels.Length; ++i)
      page1Pixels[i] = 0x55;

    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page0Pixels,
      Pages = [
        new BigTiffPage { Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = page1Pixels },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.Not.EqualTo(restored.Pages[0].PixelData));
    Assert.That(restored.PixelData, Is.EqualTo(page0Pixels));
    Assert.That(restored.Pages[0].PixelData, Is.EqualTo(page1Pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiPage_PhotometricPreserved() {
    var original = new BigTiffFile {
      Width = 4, Height = 4, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack, PixelData = new byte[16],
      Pages = [
        new BigTiffPage { Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
          PhotometricInterpretation = BigTiffFile.PhotometricRgb, PixelData = new byte[12] },
      ],
    };

    var bytes = BigTiffWriter.ToBytes(original);
    var restored = BigTiffReader.FromBytes(bytes);

    Assert.That(restored.PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricMinIsBlack));
    Assert.That(restored.Pages[0].PhotometricInterpretation, Is.EqualTo(BigTiffFile.PhotometricRgb));
  }
}
