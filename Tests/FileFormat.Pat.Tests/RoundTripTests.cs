using System;
using System.IO;
using FileFormat.Pat;

namespace FileFormat.Pat.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);

    var original = new PatFile {
      Width = 4,
      Height = 4,
      BytesPerPixel = 1,
      Name = "gray",
      PixelData = pixels
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayAlpha() {
    var pixels = new byte[3 * 2 * 2];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    var original = new PatFile {
      Width = 3,
      Height = 2,
      BytesPerPixel = 2,
      Name = "ga",
      PixelData = pixels
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var w = 8;
    var h = 6;
    var pixels = new byte[w * h * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new PatFile {
      Width = w,
      Height = h,
      BytesPerPixel = 3,
      Name = "RGB pattern",
      PixelData = pixels
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba() {
    var w = 2;
    var h = 2;
    var pixels = new byte[w * h * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new PatFile {
      Width = w,
      Height = h,
      BytesPerPixel = 4,
      Name = "RGBA test",
      PixelData = pixels
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithLongName() {
    var longName = new string('X', 200);
    var original = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = longName,
      PixelData = [0xAB]
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo(longName));
    Assert.That(restored.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyName() {
    var original = new PatFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Name = string.Empty,
      PixelData = [0xFF]
    };

    var bytes = PatWriter.ToBytes(original);
    var restored = PatReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo(string.Empty));
    Assert.That(restored.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[16 * 16 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    var original = new PatFile {
      Width = 16,
      Height = 16,
      BytesPerPixel = 3,
      Name = "file test",
      PixelData = pixels
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pat");
    try {
      var bytes = PatWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PatReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
