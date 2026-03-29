using System;
using System.IO;
using FileFormat.SoftImage;
using FileFormat.Core;

namespace FileFormat.SoftImage.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[4 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)((i * 37) % 256);

    var original = new SoftImageFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData,
      Comment = "RGB test",
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Comment, Is.EqualTo("RGB test"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba32() {
    var pixelData = new byte[3 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)((i * 41) % 256);

    var original = new SoftImageFile {
      Width = 3,
      Height = 2,
      PixelData = pixelData,
      HasAlpha = true,
      Comment = "RGBA test",
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Comment, Is.EqualTo("RGBA test"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 6 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SoftImageFile {
      Width = 8,
      Height = 6,
      PixelData = pixelData,
      Comment = "File round-trip",
      Version = 3.71f,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic");
    try {
      var bytes = SoftImageWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SoftImageReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Comment, Is.EqualTo("File round-trip"));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new SoftImageFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var width = 16;
    var height = 8;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 255 / (width - 1));
        pixelData[idx + 1] = (byte)(y * 255 / (height - 1));
        pixelData[idx + 2] = 128;
      }

    var original = new SoftImageFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var raw = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 3 * 3]
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)((i * 13) % 256);

    var file = SoftImageFile.FromRawImage(raw);
    var bytes = SoftImageWriter.ToBytes(file);
    var restored = SoftImageReader.FromBytes(bytes);
    var rawBack = SoftImageFile.ToRawImage(restored);

    Assert.That(rawBack.Width, Is.EqualTo(raw.Width));
    Assert.That(rawBack.Height, Is.EqualTo(raw.Height));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgba32() {
    var raw = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[3 * 2 * 4]
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)((i * 17) % 256);

    var file = SoftImageFile.FromRawImage(raw);
    var bytes = SoftImageWriter.ToBytes(file);
    var restored = SoftImageReader.FromBytes(bytes);
    var rawBack = SoftImageFile.ToRawImage(restored);

    Assert.That(rawBack.Width, Is.EqualTo(raw.Width));
    Assert.That(rawBack.Height, Is.EqualTo(raw.Height));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(rawBack.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VersionPreserved() {
    var original = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [100, 200, 50],
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(3.71f).Within(0.01f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CommentPreserved() {
    var original = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [100, 200, 50],
      Comment = "Softimage PIC Test File",
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Comment, Is.EqualTo("Softimage PIC Test File"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new SoftImageFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Version = 3.71f,
    };

    var bytes = SoftImageWriter.ToBytes(original);
    var restored = SoftImageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
