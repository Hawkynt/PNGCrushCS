using System;
using System.IO;
using FileFormat.AppleShr;

namespace FileFormat.AppleShr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var scb = new byte[200];
    for (var i = 0; i < scb.Length; ++i)
      scb[i] = (byte)(i % 16);

    var palette = new byte[512];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 3 % 256);

    var original = new AppleShrFile {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette
    };

    var bytes = AppleShrWriter.ToBytes(original);
    var restored = AppleShrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.ScanlineControl, Is.EqualTo(original.ScanlineControl));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    var bytes = AppleShrWriter.ToBytes(original);
    var restored = AppleShrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.ScanlineControl, Is.EqualTo(original.ScanlineControl));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[32000];
    Array.Fill(pixelData, (byte)0xFF);

    var scb = new byte[200];
    Array.Fill(scb, (byte)0xFF);

    var palette = new byte[512];
    Array.Fill(palette, (byte)0xFF);

    var original = new AppleShrFile {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette
    };

    var bytes = AppleShrWriter.ToBytes(original);
    var restored = AppleShrReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.ScanlineControl, Is.EqualTo(original.ScanlineControl));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var scb = new byte[200];
    for (var i = 0; i < scb.Length; ++i)
      scb[i] = (byte)(i % 16);

    var palette = new byte[512];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 5 % 256);

    var original = new AppleShrFile {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".shr");
    try {
      var bytes = AppleShrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AppleShrReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.ScanlineControl, Is.EqualTo(original.ScanlineControl));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    var bytes = AppleShrWriter.ToBytes(original);
    var restored = AppleShrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PerScanlinePaletteSelection() {
    var pixelData = new byte[32000];
    var scb = new byte[200];
    var palette = new byte[512];

    // Set each scanline to use a different palette (0-15 cycling)
    for (var i = 0; i < scb.Length; ++i)
      scb[i] = (byte)(i % 16);

    // Set up palette entries with distinct values
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i % 256);

    var original = new AppleShrFile {
      PixelData = pixelData,
      ScanlineControl = scb,
      Palette = palette
    };

    var bytes = AppleShrWriter.ToBytes(original);
    var restored = AppleShrReader.FromBytes(bytes);

    Assert.That(restored.ScanlineControl, Is.EqualTo(original.ScanlineControl));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }
}
