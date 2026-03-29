using System;
using System.IO;
using FileFormat.Clp;

namespace FileFormat.Clp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_PreservesData() {
    var bytesPerRow = ((4 * 24 + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new ClpFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = ClpWriter.ToBytes(original);
    var restored = ClpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed8_PreservesData() {
    // 4 bytes per row (4 pixels at 8bpp, already 4-byte aligned), 2 rows
    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 };

    // RGBQUAD palette: 4 bytes per entry (B, G, R, Reserved)
    var palette = new byte[4 * 4];
    palette[0] = 255; palette[1] = 0; palette[2] = 0; palette[3] = 0;       // blue
    palette[4] = 0;   palette[5] = 255; palette[6] = 0; palette[7] = 0;     // green
    palette[8] = 0;   palette[9] = 0;   palette[10] = 255; palette[11] = 0; // red
    palette[12] = 128; palette[13] = 128; palette[14] = 128; palette[15] = 0; // gray

    var original = new ClpFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = ClpWriter.ToBytes(original);
    var restored = ClpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var bytesPerRow = ((4 * 24 + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new ClpFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".clp");
    try {
      var bytes = ClpWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = ClpReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
