using System;
using System.IO;
using FileFormat.Gbr;

namespace FileFormat.Gbr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new GbrFile {
      Width = 4,
      Height = 4,
      BytesPerPixel = 1,
      Spacing = 25,
      Name = "Gray Test",
      PixelData = pixelData
    };

    var bytes = GbrWriter.ToBytes(original);
    var restored = GbrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Spacing, Is.EqualTo(original.Spacing));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba() {
    var pixelData = new byte[3 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new GbrFile {
      Width = 3,
      Height = 2,
      BytesPerPixel = 4,
      Spacing = 50,
      Name = "RGBA Test",
      PixelData = pixelData
    };

    var bytes = GbrWriter.ToBytes(original);
    var restored = GbrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
    Assert.That(restored.Spacing, Is.EqualTo(original.Spacing));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithUnicodeName() {
    var original = new GbrFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "Pinsel \u00FC\u00E4\u00F6",
      PixelData = new byte[4]
    };

    var bytes = GbrWriter.ToBytes(original);
    var restored = GbrReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo(original.Name));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyName() {
    var original = new GbrFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      Spacing = 5,
      Name = "",
      PixelData = new byte[1]
    };

    var bytes = GbrWriter.ToBytes(original);
    var restored = GbrReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo(string.Empty));
    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.PixelData.Length, Is.EqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new GbrFile {
      Width = 8,
      Height = 8,
      BytesPerPixel = 4,
      Spacing = 100,
      Name = "File Test",
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gbr");
    try {
      var bytes = GbrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = GbrReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BytesPerPixel, Is.EqualTo(original.BytesPerPixel));
      Assert.That(restored.Spacing, Is.EqualTo(original.Spacing));
      Assert.That(restored.Name, Is.EqualTo(original.Name));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 64;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new GbrFile {
      Width = width,
      Height = height,
      BytesPerPixel = 1,
      Spacing = 10,
      Name = "Large Brush",
      PixelData = pixelData
    };

    var bytes = GbrWriter.ToBytes(original);
    var restored = GbrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
