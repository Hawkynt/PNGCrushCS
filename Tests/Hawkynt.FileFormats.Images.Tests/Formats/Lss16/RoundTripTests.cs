using System;
using System.IO;
using FileFormat.Lss16;

namespace FileFormat.Lss16.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidColor0() {
    var original = new Lss16File {
      Width = 8,
      Height = 4,
      Palette = _MakeTestPalette(),
      PixelData = new byte[8 * 4],
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidNonZeroColor() {
    var pixels = new byte[10 * 3];
    Array.Fill(pixels, (byte)5);

    var original = new Lss16File {
      Width = 10,
      Height = 3,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MixedPixels() {
    var width = 16;
    var height = 4;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new Lss16File {
      Width = width,
      Height = height,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AlternatingPixels() {
    var width = 20;
    var height = 2;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 2 == 0 ? 3 : 7);

    var original = new Lss16File {
      Width = width,
      Height = height,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LongRun() {
    var width = 300;
    var height = 1;
    var pixels = new byte[width * height];
    Array.Fill(pixels, (byte)9);

    var original = new Lss16File {
      Width = width,
      Height = height,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 16;
    var height = 8;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i * 3) % 16);

    var original = new Lss16File {
      Width = width,
      Height = height,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lss");
    try {
      var bytes = Lss16Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Lss16Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PalettePreserved() {
    var palette = _MakeTestPalette();
    palette[0] = 63;
    palette[1] = 0;
    palette[2] = 0;
    palette[45] = 0;
    palette[46] = 63;
    palette[47] = 0;

    var original = new Lss16File {
      Width = 4,
      Height = 1,
      Palette = palette,
      PixelData = new byte[4],
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_StandardDimensions() {
    var width = 640;
    var height = 480;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new Lss16File {
      Width = width,
      Height = height,
      Palette = _MakeTestPalette(),
      PixelData = pixels,
    };

    var bytes = Lss16Writer.ToBytes(original);
    var restored = Lss16Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(640));
    Assert.That(restored.Height, Is.EqualTo(480));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  private static byte[] _MakeTestPalette() {
    var palette = new byte[Lss16File.PaletteSize];
    for (var i = 0; i < Lss16File.PaletteEntryCount; ++i) {
      palette[i * 3] = (byte)(i * 4);
      palette[i * 3 + 1] = (byte)(i * 4);
      palette[i * 3 + 2] = (byte)(i * 4);
    }

    return palette;
  }
}
