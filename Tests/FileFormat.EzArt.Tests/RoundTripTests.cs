using System;
using System.IO;
using FileFormat.EzArt;

namespace FileFormat.EzArt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new EzArtFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = EzArtWriter.ToBytes(original);
    var restored = EzArtReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPaletteData() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x0111);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new EzArtFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = EzArtWriter.ToBytes(original);
    var restored = EzArtReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomData() {
    var rng = new Random(42);
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(rng.Next(0, 0x0800) & 0x0777);

    var pixelData = new byte[32000];
    rng.NextBytes(pixelData);

    var original = new EzArtFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = EzArtWriter.ToBytes(original);
    var restored = EzArtReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[15] = 0x0007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new EzArtFile {
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eza");
    try {
      var bytes = EzArtWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = EzArtReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(200));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new short[16];
    palette[0] = 0x0000;
    palette[1] = 0x0777;
    palette[2] = 0x0700;

    var original = new EzArtFile {
      Palette = palette,
      PixelData = new byte[32000]
    };

    var raw = EzArtFile.ToRawImage(original);
    var restored = EzArtFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.PixelData, Has.Length.EqualTo(32000));
    });
  }
}
