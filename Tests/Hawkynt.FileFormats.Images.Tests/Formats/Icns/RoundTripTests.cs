using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Icns;
using FileFormat.Png;

namespace FileFormat.Icns.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePngEntry_EntriesPreserved() {
    var pngBytes = _CreateMinimalPng(128, 128);
    var original = new IcnsFile {
      Entries = [new IcnsEntry("ic07", pngBytes, 128, 128)]
    };

    var bytes = IcnsWriter.ToBytes(original);
    var restored = IcnsReader.FromBytes(bytes);

    Assert.That(restored.Entries, Has.Count.EqualTo(1));
    Assert.That(restored.Entries[0].OsType, Is.EqualTo("ic07"));
    Assert.That(restored.Entries[0].Width, Is.EqualTo(128));
    Assert.That(restored.Entries[0].Height, Is.EqualTo(128));
    Assert.That(restored.Entries[0].Data, Is.EqualTo(pngBytes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleEntries_AllPreserved() {
    var png1 = _CreateMinimalPng(128, 128);
    var png2 = _CreateMinimalPng(256, 256);
    var original = new IcnsFile {
      Entries = [
        new IcnsEntry("ic07", png1, 128, 128),
        new IcnsEntry("ic08", png2, 256, 256),
      ]
    };

    var bytes = IcnsWriter.ToBytes(original);
    var restored = IcnsReader.FromBytes(bytes);

    Assert.That(restored.Entries, Has.Count.EqualTo(2));
    Assert.That(restored.Entries[0].OsType, Is.EqualTo("ic07"));
    Assert.That(restored.Entries[0].Data, Is.EqualTo(png1));
    Assert.That(restored.Entries[1].OsType, Is.EqualTo("ic08"));
    Assert.That(restored.Entries[1].Data, Is.EqualTo(png2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LegacyRgbEntry_DataPreserved() {
    var pixelCount = 16 * 16;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 7 % 256);

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var original = new IcnsFile {
      Entries = [new IcnsEntry("is32", compressed, 16, 16)]
    };

    var bytes = IcnsWriter.ToBytes(original);
    var restored = IcnsReader.FromBytes(bytes);

    Assert.That(restored.Entries, Has.Count.EqualTo(1));
    Assert.That(restored.Entries[0].OsType, Is.EqualTo("is32"));
    Assert.That(restored.Entries[0].Data, Is.EqualTo(compressed));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LegacyRgbWithMask_ToRawImageDecodesCorrectly() {
    var pixelCount = 16 * 16;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = 200;
      rgb[i * 3 + 1] = 100;
      rgb[i * 3 + 2] = 50;
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var mask = new byte[pixelCount];
    for (var i = 0; i < pixelCount; ++i)
      mask[i] = 128;

    var icns = new IcnsFile {
      Entries = [
        new IcnsEntry("is32", compressed, 16, 16),
        new IcnsEntry("s8mk", mask, 16, 16),
      ]
    };

    var raw = IcnsFile.ToRawImage(icns);

    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(16));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));

    // Check first pixel: R=200, G=100, B=50, A=128
    Assert.That(raw.PixelData[0], Is.EqualTo(200));
    Assert.That(raw.PixelData[1], Is.EqualTo(100));
    Assert.That(raw.PixelData[2], Is.EqualTo(50));
    Assert.That(raw.PixelData[3], Is.EqualTo(128));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PngEntry_ToRawImage_PixelDataCorrect() {
    var width = 4;
    var height = 4;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; i += 4) {
      pixelData[i] = 10;      // R
      pixelData[i + 1] = 20;  // G
      pixelData[i + 2] = 30;  // B
      pixelData[i + 3] = 255; // A
    }

    var pngRaw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixelData,
    };

    var pngFile = PngFile.FromRawImage(pngRaw);
    var pngBytes = PngWriter.ToBytes(pngFile);

    var icns = new IcnsFile {
      Entries = [new IcnsEntry("ic11", pngBytes, width, height)]
    };

    var raw = IcnsFile.ToRawImage(icns);

    Assert.That(raw.Width, Is.EqualTo(width));
    Assert.That(raw.Height, Is.EqualTo(height));
    Assert.That(raw.PixelData[0], Is.EqualTo(10));
    Assert.That(raw.PixelData[1], Is.EqualTo(20));
    Assert.That(raw.PixelData[2], Is.EqualTo(30));
    Assert.That(raw.PixelData[3], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_DataPreserved() {
    var pngBytes = _CreateMinimalPng(128, 128);
    var original = new IcnsFile {
      Entries = [new IcnsEntry("ic07", pngBytes, 128, 128)]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icns");
    try {
      var bytes = IcnsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IcnsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Entries, Has.Count.EqualTo(1));
      Assert.That(restored.Entries[0].OsType, Is.EqualTo("ic07"));
      Assert.That(restored.Entries[0].Data, Is.EqualTo(pngBytes));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_ProducesValidIcns() {
    var raw = new RawImage {
      Width = 128,
      Height = 128,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[128 * 128 * 4],
    };
    for (var i = 0; i < raw.PixelData.Length; i += 4) {
      raw.PixelData[i] = 42;
      raw.PixelData[i + 1] = 84;
      raw.PixelData[i + 2] = 126;
      raw.PixelData[i + 3] = 255;
    }

    var icns = IcnsFile.FromRawImage(raw);
    var bytes = IcnsWriter.ToBytes(icns);
    var restored = IcnsReader.FromBytes(bytes);

    Assert.That(restored.Entries, Has.Count.EqualTo(1));
    Assert.That(restored.Entries[0].IsPng, Is.True);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_ToRawImage_PixelsPreserved() {
    var width = 8;
    var height = 8;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixelData[i * 4] = (byte)(i * 3 % 256);
      pixelData[i * 4 + 1] = (byte)(i * 7 % 256);
      pixelData[i * 4 + 2] = (byte)(i * 11 % 256);
      pixelData[i * 4 + 3] = 255;
    }

    var raw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixelData,
    };

    var icns = IcnsFile.FromRawImage(raw);
    var bytes = IcnsWriter.ToBytes(icns);
    var restored = IcnsReader.FromBytes(bytes);
    var restoredRaw = IcnsFile.ToRawImage(restored);

    // The pixel data should survive a PNG round-trip
    var originalRgba = raw.ToRgba32();
    var restoredRgba = restoredRaw.ToRgba32();

    Assert.That(restoredRaw.Width, Is.EqualTo(width));
    Assert.That(restoredRaw.Height, Is.EqualTo(height));
    Assert.That(restoredRgba, Is.EqualTo(originalRgba));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_It32WithZeroHeader_ToRawImageDecodesCorrectly() {
    var pixelCount = 128 * 128;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = 100;
      rgb[i * 3 + 1] = 150;
      rgb[i * 3 + 2] = 200;
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);

    // it32 has a 4-byte zero prefix
    var it32Data = new byte[4 + compressed.Length];
    Array.Copy(compressed, 0, it32Data, 4, compressed.Length);

    var icns = new IcnsFile {
      Entries = [new IcnsEntry("it32", it32Data, 128, 128)]
    };

    var raw = IcnsFile.ToRawImage(icns);

    Assert.That(raw.Width, Is.EqualTo(128));
    Assert.That(raw.Height, Is.EqualTo(128));
    Assert.That(raw.PixelData[0], Is.EqualTo(100));
    Assert.That(raw.PixelData[1], Is.EqualTo(150));
    Assert.That(raw.PixelData[2], Is.EqualTo(200));
    Assert.That(raw.PixelData[3], Is.EqualTo(255)); // no mask = opaque
  }

  private static byte[] _CreateMinimalPng(int width, int height) {
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; i += 4) {
      pixelData[i] = (byte)(i % 256);
      pixelData[i + 1] = (byte)((i + 64) % 256);
      pixelData[i + 2] = (byte)((i + 128) % 256);
      pixelData[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixelData,
    };

    var pngFile = PngFile.FromRawImage(raw);
    return PngWriter.ToBytes(pngFile);
  }
}
