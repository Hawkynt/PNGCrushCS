using System;
using System.IO;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_16bpp() {
    var pixelData = new byte[4 * 2 * 2]; // 4 wide, 2 tall, 2 bytes per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp16,
      HasClut = false,
      ImageX = 0,
      ImageY = 0,
      PixelData = pixelData
    };

    var bytes = TimWriter.ToBytes(original);
    var restored = TimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bpp, Is.EqualTo(TimBpp.Bpp16));
    Assert.That(restored.HasClut, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bpp() {
    var clutData = new byte[256 * 2];
    for (var i = 0; i < clutData.Length; ++i)
      clutData[i] = (byte)(i % 256);

    // 4 wide, 8bpp: VRAM width = 4/2 = 2, pixel data = 2*2*2 = 8 bytes
    var pixelData = new byte[2 * 2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp8,
      HasClut = true,
      ClutData = clutData,
      ClutX = 0,
      ClutY = 480,
      ClutWidth = 256,
      ClutHeight = 1,
      ImageX = 320,
      ImageY = 0,
      PixelData = pixelData
    };

    var bytes = TimWriter.ToBytes(original);
    var restored = TimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bpp, Is.EqualTo(TimBpp.Bpp8));
    Assert.That(restored.HasClut, Is.True);
    Assert.That(restored.ClutData, Is.EqualTo(original.ClutData));
    Assert.That(restored.ClutX, Is.EqualTo(original.ClutX));
    Assert.That(restored.ClutY, Is.EqualTo(original.ClutY));
    Assert.That(restored.ClutWidth, Is.EqualTo(original.ClutWidth));
    Assert.That(restored.ClutHeight, Is.EqualTo(original.ClutHeight));
    Assert.That(restored.ImageX, Is.EqualTo(original.ImageX));
    Assert.That(restored.ImageY, Is.EqualTo(original.ImageY));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_4bpp() {
    var clutData = new byte[16 * 2];
    for (var i = 0; i < clutData.Length; ++i)
      clutData[i] = (byte)(i * 3 % 256);

    // 8 wide, 4bpp: VRAM width = 8/4 = 2, pixel data = 2*2*2 = 8 bytes
    var pixelData = new byte[2 * 2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new TimFile {
      Width = 8,
      Height = 2,
      Bpp = TimBpp.Bpp4,
      HasClut = true,
      ClutData = clutData,
      ClutX = 0,
      ClutY = 480,
      ClutWidth = 16,
      ClutHeight = 1,
      ImageX = 0,
      ImageY = 0,
      PixelData = pixelData
    };

    var bytes = TimWriter.ToBytes(original);
    var restored = TimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bpp, Is.EqualTo(TimBpp.Bpp4));
    Assert.That(restored.HasClut, Is.True);
    Assert.That(restored.ClutData, Is.EqualTo(original.ClutData));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_24bpp() {
    // 6 wide, 24bpp: VRAM width = 6*3/2 = 9, pixel data = 9*2*2 = 36 bytes
    var pixelData = new byte[9 * 2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new TimFile {
      Width = 6,
      Height = 2,
      Bpp = TimBpp.Bpp24,
      HasClut = false,
      ImageX = 0,
      ImageY = 0,
      PixelData = pixelData
    };

    var bytes = TimWriter.ToBytes(original);
    var restored = TimReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bpp, Is.EqualTo(TimBpp.Bpp24));
    Assert.That(restored.HasClut, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp16,
      HasClut = false,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tim");
    try {
      var bytes = TimWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = TimReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Bpp, Is.EqualTo(TimBpp.Bpp16));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
