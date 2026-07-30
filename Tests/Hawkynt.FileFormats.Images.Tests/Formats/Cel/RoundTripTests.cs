using System;
using System.IO;
using FileFormat.Cel;

namespace FileFormat.Cel.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba32() {
    var pixelData = new byte[4 * 3 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new CelFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 32,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed8() {
    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new CelFile {
      Width = 8,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed4() {
    var width = 6;
    var height = 3;
    var packedSize = ((width + 1) / 2) * height;
    var pixelData = new byte[packedSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new CelFile {
      Width = width,
      Height = height,
      BitsPerPixel = 4,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OffsetsPreserved() {
    var original = new CelFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 32,
      XOffset = 123,
      YOffset = 456,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.XOffset, Is.EqualTo(123));
    Assert.That(restored.YOffset, Is.EqualTo(456));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0xF0, 0x0F, 0xCC, 0x33 };
    var original = new CelFile {
      Width = 2,
      Height = 1,
      BitsPerPixel = 32,
      XOffset = 7,
      YOffset = 13,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cel");
    try {
      var bytes = CelWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CelReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
      Assert.That(restored.XOffset, Is.EqualTo(original.XOffset));
      Assert.That(restored.YOffset, Is.EqualTo(original.YOffset));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeIndexed8() {
    var width = 128;
    var height = 64;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new CelFile {
      Width = width,
      Height = height,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddWidthIndexed4() {
    var width = 5;
    var height = 2;
    var packedSize = ((width + 1) / 2) * height;
    var pixelData = new byte[packedSize];
    pixelData[0] = 0x12;
    pixelData[1] = 0x34;
    pixelData[2] = 0x50;
    pixelData[3] = 0x67;
    pixelData[4] = 0x89;
    pixelData[5] = 0xA0;

    var original = new CelFile {
      Width = width,
      Height = height,
      BitsPerPixel = 4,
      PixelData = pixelData
    };

    var bytes = CelWriter.ToBytes(original);
    var restored = CelReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
