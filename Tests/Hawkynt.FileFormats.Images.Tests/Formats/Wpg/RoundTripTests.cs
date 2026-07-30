using System;
using System.IO;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1bpp_PreservesData() {
    // 8 pixels wide = 1 byte per row, 2 rows
    var pixelData = new byte[] { 0b10101010, 0b01010101 };

    var original = new WpgFile {
      Width = 8,
      Height = 2,
      BitsPerPixel = 1,
      PixelData = pixelData
    };

    var bytes = WpgWriter.ToBytes(original);
    var restored = WpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bpp_PreservesData() {
    var pixelData = new byte[4 * 3]; // 4x3 indexed
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new WpgFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = WpgWriter.ToBytes(original);
    var restored = WpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette_PreservesPalette() {
    var palette = new byte[4 * 3];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;     // red
    palette[3] = 0;   palette[4] = 255; palette[5] = 0;   // green
    palette[6] = 0;   palette[7] = 0;   palette[8] = 255; // blue
    palette[9] = 128; palette[10] = 128; palette[11] = 128; // gray

    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 };

    var original = new WpgFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = WpgWriter.ToBytes(original);
    var restored = WpgReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new WpgFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wpg");
    try {
      var bytes = WpgWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = WpgReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
