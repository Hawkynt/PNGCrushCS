using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PcPaint;

namespace FileFormat.PcPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  /// <summary>
  /// A palette the file can hold exactly. The digital-to-analogue converter takes six bits a
  /// channel, so only values whose bottom two bits repeat their top two survive being written.
  /// </summary>
  private static byte[] _Palette(Func<int, (byte R, byte G, byte B)> colour) {
    var palette = new byte[PcPaintFile.PaletteSize];
    for (var i = 0; i < 256; ++i) {
      var (r, g, b) = colour(i);
      palette[i * 3] = _Representable(r);
      palette[i * 3 + 1] = _Representable(g);
      palette[i * 3 + 2] = _Representable(b);
    }

    return palette;
  }

  private static byte _Representable(byte value) {
    var six = value >> 2;
    return (byte)((six << 2) | (six >> 4));
  }

  private static PcPaintFile _Page(int width, int height, byte[] pixels, byte[]? palette = null) => new() {
    Width = width,
    Height = height,
    BitsPerPixel = 8,
    VideoMode = (byte)'A',
    PaletteType = PcPaintFile.PaletteVga,
    Palette = palette ?? new byte[PcPaintFile.PaletteSize],
    PixelData = pixels,
  };

  [Test]
  [Category("Integration")]
  public void RoundTrip_BasicIndexedImage() {
    var palette = _Palette(i => i switch {
      0 => (255, 0, 0),
      1 => (0, 255, 0),
      2 => (0, 0, 255),
      _ => (0, 0, 0),
    });

    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 3);

    var original = _Page(4, 3, pixelData, palette);
    var restored = PcPaintReader.FromBytes(PcPaintWriter.ToBytes(original));

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = _Palette(i => ((byte)i, (byte)(255 - i), (byte)(i / 2)));

    var pixelData = new byte[10 * 10];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = _Page(10, 10, pixelData, palette);

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic");
    try {
      File.WriteAllBytes(tempPath, PcPaintWriter.ToBytes(original));
      var restored = PcPaintReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage() {
    var palette = _Palette(i => ((byte)i, (byte)(255 - i), (byte)(i / 2)));

    var pixelData = new byte[8 * 6];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = _Page(8, 6, pixelData, palette);

    var raw = PcPaintFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));

    var restored = PcPaintFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = _Page(2, 2, new byte[4]);
    var restored = PcPaintReader.FromBytes(PcPaintWriter.ToBytes(original));

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LongRunsUseTheExtendedCount() {
    var pixelData = new byte[500];
    Array.Fill(pixelData, (byte)99);

    var original = _Page(500, 1, pixelData);
    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));

    // Five hundred of one value is one long run, so the block is far smaller than the picture.
    Assert.That(bytes.Length, Is.LessThan(PcPaintFile.HeaderSize + PcPaintFile.VgaPaletteBytes + 2 + 500));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SeveralBlocksForALargePicture() {
    var pixelData = new byte[400 * 400];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i / 400 % 256);

    var original = _Page(400, 400, pixelData);
    var restored = PcPaintReader.FromBytes(PcPaintWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OffsetsPreserved() {
    var original = _Page(2, 2, new byte[4]) with { XOffset = 100, YOffset = 200 };
    var restored = PcPaintReader.FromBytes(PcPaintWriter.ToBytes(original));

    Assert.That(restored.XOffset, Is.EqualTo(100));
    Assert.That(restored.YOffset, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryByteValueUsed() {
    // No byte is free to be the run marker, so the block has to go out as literals throughout.
    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var original = _Page(16, 16, pixelData);
    var restored = PcPaintReader.FromBytes(PcPaintWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
