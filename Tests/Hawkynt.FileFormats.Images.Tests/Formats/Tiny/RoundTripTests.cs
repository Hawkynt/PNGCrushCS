using System;
using System.IO;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Low() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new TinyFile {
      Width = 320,
      Height = 200,
      Resolution = TinyResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = TinyWriter.ToBytes(original);
    var restored = TinyReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Medium() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new TinyFile {
      Width = 640,
      Height = 200,
      Resolution = TinyResolution.Medium,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = TinyWriter.ToBytes(original);
    var restored = TinyReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_High() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x777;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var original = new TinyFile {
      Width = 640,
      Height = 400,
      Resolution = TinyResolution.High,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = TinyWriter.ToBytes(original);
    var restored = TinyReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x077);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 16);

    var original = new TinyFile {
      Width = 320,
      Height = 200,
      Resolution = TinyResolution.Low,
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tny");
    try {
      var bytes = TinyWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = TinyReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(200));
        Assert.That(restored.Resolution, Is.EqualTo(TinyResolution.Low));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
