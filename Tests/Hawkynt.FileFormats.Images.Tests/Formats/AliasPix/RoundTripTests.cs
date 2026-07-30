using System;
using System.IO;
using FileFormat.AliasPix;

namespace FileFormat.AliasPix.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_24bpp() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new AliasPixFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = AliasPixWriter.ToBytes(original);
    var restored = AliasPixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_32bpp() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new AliasPixFile {
      Width = width,
      Height = height,
      BitsPerPixel = 32,
      PixelData = pixelData
    };

    var bytes = AliasPixWriter.ToBytes(original);
    var restored = AliasPixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithOffsets() {
    var original = new AliasPixFile {
      Width = 2,
      Height = 2,
      XOffset = 42,
      YOffset = 99,
      BitsPerPixel = 24,
      PixelData = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0 }
    };

    var bytes = AliasPixWriter.ToBytes(original);
    var restored = AliasPixReader.FromBytes(bytes);

    Assert.That(restored.XOffset, Is.EqualTo(42));
    Assert.That(restored.YOffset, Is.EqualTo(99));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new AliasPixFile {
      Width = 2,
      Height = 1,
      BitsPerPixel = 24,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xCA, 0xFE, 0xBA }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pix");
    try {
      var bytes = AliasPixWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AliasPixReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
