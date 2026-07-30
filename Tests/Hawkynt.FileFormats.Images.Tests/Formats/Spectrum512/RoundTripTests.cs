using System;
using System.IO;
using FileFormat.Spectrum512;

namespace FileFormat.Spectrum512.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var palettes = new short[199][];
    for (var i = 0; i < 199; ++i)
      palettes[i] = new short[48];

    var original = new Spectrum512File {
      Width = 320,
      Height = 199,
      Variant = Spectrum512Variant.Uncompressed,
      PixelData = new byte[32000],
      Palettes = palettes
    };

    var bytes = Spectrum512Writer.ToBytes(original);
    var restored = Spectrum512Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Variant, Is.EqualTo(original.Variant));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      for (var line = 0; line < 199; ++line)
        Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]), $"Palette mismatch at scanline {line}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPaletteData() {
    var palettes = new short[199][];
    for (var line = 0; line < 199; ++line) {
      palettes[line] = new short[48];
      for (var entry = 0; entry < 48; ++entry)
        palettes[line][entry] = (short)((line * 48 + entry) & 0x777);
    }

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new Spectrum512File {
      Width = 320,
      Height = 199,
      Variant = Spectrum512Variant.Uncompressed,
      PixelData = pixelData,
      Palettes = palettes
    };

    var bytes = Spectrum512Writer.ToBytes(original);
    var restored = Spectrum512Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      for (var line = 0; line < 199; ++line)
        Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]), $"Palette mismatch at scanline {line}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palettes = new short[199][];
    for (var line = 0; line < 199; ++line) {
      palettes[line] = new short[48];
      for (var entry = 0; entry < 48; ++entry)
        palettes[line][entry] = (short)(entry & 0x0F);
    }

    var original = new Spectrum512File {
      Width = 320,
      Height = 199,
      Variant = Spectrum512Variant.Uncompressed,
      PixelData = new byte[32000],
      Palettes = palettes
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spu");
    try {
      var bytes = Spectrum512Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Spectrum512Reader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(199));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
        for (var line = 0; line < 199; ++line)
          Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
