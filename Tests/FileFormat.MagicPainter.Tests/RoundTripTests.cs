using System;
using System.IO;
using FileFormat.MagicPainter;

namespace FileFormat.MagicPainter.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var pixels = new byte[] { 0, 1, 1, 0 };

    var original = new MagicPainterFile {
      Width = 2,
      Height = 2,
      PaletteCount = 2,
      Palette = palette,
      PixelData = pixels,
    };

    var bytes = MagicPainterWriter.ToBytes(original);
    var restored = MagicPainterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PaletteCount, Is.EqualTo(original.PaletteCount));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_256Palette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 7 % 256);
    }

    var pixels = new byte[16 * 16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new MagicPainterFile {
      Width = 16,
      Height = 16,
      PaletteCount = 256,
      Palette = palette,
      PixelData = pixels,
    };

    var bytes = MagicPainterWriter.ToBytes(original);
    var restored = MagicPainterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.PaletteCount, Is.EqualTo(256));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new MagicPainterFile {
      Width = 1,
      Height = 1,
      PaletteCount = 1,
      Palette = new byte[] { 128, 64, 32 },
      PixelData = new byte[] { 0 },
    };

    var bytes = MagicPainterWriter.ToBytes(original);
    var restored = MagicPainterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var palette = new byte[48]; // 16 colors
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 16);
      palette[i * 3 + 1] = (byte)(i * 8);
      palette[i * 3 + 2] = (byte)(i * 4);
    }

    var pixels = new byte[320 * 200];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new MagicPainterFile {
      Width = 320,
      Height = 200,
      PaletteCount = 16,
      Palette = palette,
      PixelData = pixels,
    };

    var bytes = MagicPainterWriter.ToBytes(original);
    var restored = MagicPainterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new MagicPainterFile {
      Width = 4,
      Height = 4,
      PaletteCount = 4,
      Palette = palette,
      PixelData = pixels,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgp");
    try {
      var bytes = MagicPainterWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = MagicPainterReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var pixels = new byte[] { 0, 1, 0, 1 };

    var original = new MagicPainterFile {
      Width = 2,
      Height = 2,
      PaletteCount = 2,
      Palette = palette,
      PixelData = pixels,
    };

    var raw = MagicPainterFile.ToRawImage(original);
    var restored = MagicPainterFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
