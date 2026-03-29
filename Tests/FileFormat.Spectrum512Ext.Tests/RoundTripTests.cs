using System;
using System.IO;
using FileFormat.Spectrum512Ext;

namespace FileFormat.Spectrum512Ext.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var palettes = new short[Spectrum512ExtFile.ScanlineCount][];
    for (var i = 0; i < Spectrum512ExtFile.ScanlineCount; ++i)
      palettes[i] = new short[Spectrum512ExtFile.PaletteEntriesPerLine];

    var original = new Spectrum512ExtFile {
      Width = 320,
      Height = 199,
      PixelData = new byte[32000],
      Palettes = palettes
    };

    var bytes = Spectrum512ExtWriter.ToBytes(original);
    var restored = Spectrum512ExtReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line)
        Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]), $"Palette mismatch at scanline {line}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPaletteData() {
    var palettes = new short[Spectrum512ExtFile.ScanlineCount][];
    for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line) {
      palettes[line] = new short[Spectrum512ExtFile.PaletteEntriesPerLine];
      for (var entry = 0; entry < Spectrum512ExtFile.PaletteEntriesPerLine; ++entry)
        palettes[line][entry] = (short)((line * 48 + entry) & 0x0FFF);
    }

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new Spectrum512ExtFile {
      Width = 320,
      Height = 199,
      PixelData = pixelData,
      Palettes = palettes
    };

    var bytes = Spectrum512ExtWriter.ToBytes(original);
    var restored = Spectrum512ExtReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line)
        Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]), $"Palette mismatch at scanline {line}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palettes = new short[Spectrum512ExtFile.ScanlineCount][];
    for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line) {
      palettes[line] = new short[Spectrum512ExtFile.PaletteEntriesPerLine];
      for (var entry = 0; entry < Spectrum512ExtFile.PaletteEntriesPerLine; ++entry)
        palettes[line][entry] = (short)(entry & 0x0F);
    }

    var original = new Spectrum512ExtFile {
      Width = 320,
      Height = 199,
      PixelData = new byte[32000],
      Palettes = palettes
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spx");
    try {
      var bytes = Spectrum512ExtWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Spectrum512ExtReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(199));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
        for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line)
          Assert.That(restored.Palettes[line], Is.EqualTo(original.Palettes[line]));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
