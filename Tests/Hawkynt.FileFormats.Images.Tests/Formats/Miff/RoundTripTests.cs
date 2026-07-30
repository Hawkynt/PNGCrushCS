using System;
using System.IO;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_Uncompressed() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, 3 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MiffFile {
      Width = 4,
      Height = 3,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.None,
      Type = "TrueColor",
      Colorspace = "sRGB",
      PixelData = pixelData
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.Type, Is.EqualTo("TrueColor"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_Uncompressed() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2, 4 channels (RGBA)
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MiffFile {
      Width = 2,
      Height = 2,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.None,
      Type = "TrueColorAlpha",
      Colorspace = "sRGB",
      PixelData = pixelData
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Type, Is.EqualTo("TrueColorAlpha"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette_Uncompressed() {
    var palette = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128 }; // 4 colors
    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 }; // 4x2 indexed

    var original = new MiffFile {
      Width = 4,
      Height = 2,
      Depth = 8,
      ColorClass = MiffColorClass.PseudoClass,
      Compression = MiffCompression.None,
      Type = "Palette",
      Colorspace = "sRGB",
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ColorClass, Is.EqualTo(MiffColorClass.PseudoClass));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_Uncompressed() {
    var pixelData = new byte[3 * 3]; // 3x3, 1 channel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 28 % 256);

    var original = new MiffFile {
      Width = 3,
      Height = 3,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.None,
      Type = "Grayscale",
      Colorspace = "Gray",
      PixelData = pixelData
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.Type, Is.EqualTo("Grayscale"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_Rle() {
    // Repeating pixel data for good RLE
    var pixelData = new byte[10 * 3]; // 10 pixels
    for (var i = 0; i < pixelData.Length; i += 3) {
      pixelData[i] = 0xFF;
      pixelData[i + 1] = 0x80;
      pixelData[i + 2] = 0x40;
    }

    var original = new MiffFile {
      Width = 10,
      Height = 1,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.Rle,
      Type = "TrueColor",
      Colorspace = "sRGB",
      PixelData = pixelData
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(10));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.Compression, Is.EqualTo(MiffCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_Zip() {
    var pixelData = new byte[4 * 4 * 3]; // 4x4 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MiffFile {
      Width = 4,
      Height = 4,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.Zip,
      Type = "TrueColor",
      Colorspace = "sRGB",
      PixelData = pixelData
    };

    var bytes = MiffWriter.ToBytes(original);
    var restored = MiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Compression, Is.EqualTo(MiffCompression.Zip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[3 * 2 * 3]; // 3x2 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new MiffFile {
      Width = 3,
      Height = 2,
      Depth = 8,
      ColorClass = MiffColorClass.DirectClass,
      Compression = MiffCompression.None,
      Type = "TrueColor",
      Colorspace = "sRGB",
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".miff");
    try {
      var bytes = MiffWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MiffReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
