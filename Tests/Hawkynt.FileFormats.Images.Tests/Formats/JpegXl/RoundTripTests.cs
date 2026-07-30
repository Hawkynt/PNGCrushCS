using System;
using System.IO;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_1x1() {
    var pixelData = new byte[] { 255, 128, 0 };

    var original = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ComponentCount, Is.EqualTo(original.ComponentCount));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8_2x2() {
    var pixelData = new byte[] { 10, 20, 30, 40 };

    var original = new JpegXlFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.ComponentCount, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_4x3() {
    var pixelData = new byte[4 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new JpegXlFile {
      Width = 4,
      Height = 3,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[3 * 2 * 3]; // all zeros

    var original = new JpegXlFile {
      Width = 3,
      Height = 2,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(3));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    const int width = 8;
    const int height = 8;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 32);
        pixelData[idx + 1] = (byte)(y * 32);
        pixelData[idx + 2] = (byte)((x + y) * 16);
      }

    var original = new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    const int width = 100;
    const int height = 75;
    var pixelData = new byte[width * height * 3];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new JpegXlFile {
      Width = width,
      Height = height,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 100, 200, 50, 25, 75, 150 };

    var original = new JpegXlFile {
      Width = 2,
      Height = 1,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxl");
    try {
      var bytes = JpegXlWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = JpegXlReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(2));
        Assert.That(restored.Height, Is.EqualTo(1));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]
    };

    var jxlFile = JpegXlFile.FromRawImage(rawImage);
    var bytes = JpegXlWriter.ToBytes(jxlFile);
    var restored = JpegXlReader.FromBytes(bytes);
    var restoredRaw = JpegXlFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restoredRaw.Width, Is.EqualTo(rawImage.Width));
      Assert.That(restoredRaw.Height, Is.EqualTo(rawImage.Height));
      Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
      Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 3,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60]
    };

    var jxlFile = JpegXlFile.FromRawImage(rawImage);
    var bytes = JpegXlWriter.ToBytes(jxlFile);
    var restored = JpegXlReader.FromBytes(bytes);
    var restoredRaw = JpegXlFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restoredRaw.Width, Is.EqualTo(rawImage.Width));
      Assert.That(restoredRaw.Height, Is.EqualTo(rawImage.Height));
      Assert.That(restoredRaw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
      Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BrandPreserved() {
    var original = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[3],
      Brand = "jxl "
    };

    var bytes = JpegXlWriter.ToBytes(original);
    var restored = JpegXlReader.FromBytes(bytes);

    Assert.That(restored.Brand, Is.EqualTo("jxl "));
  }
}
