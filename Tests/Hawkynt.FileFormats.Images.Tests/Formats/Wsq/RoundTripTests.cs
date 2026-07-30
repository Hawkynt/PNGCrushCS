using System;
using System.IO;
using System.Linq;
using FileFormat.Wsq;
using FileFormat.Core;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UniformGray_DimensionsPreserved() {
    var pixels = new byte[64 * 64];
    Array.Fill(pixels, (byte)128);

    var original = new WsqFile {
      Width = 64,
      Height = 64,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(original);
    var restored = WsqReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(64));
    Assert.That(restored.Height, Is.EqualTo(64));
    Assert.That(restored.PixelData.Length, Is.EqualTo(64 * 64));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_UniformGray_PixelValuesClose() {
    var pixels = new byte[32 * 32];
    Array.Fill(pixels, (byte)128);

    var original = new WsqFile {
      Width = 32,
      Height = 32,
      CompressionRatio = 0.95,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(original);
    var restored = WsqReader.FromBytes(bytes);

    // WSQ is lossy; verify pixels are close
    var maxDiff = 0;
    for (var i = 0; i < pixels.Length; ++i) {
      var diff = Math.Abs(restored.PixelData[i] - pixels[i]);
      if (diff > maxDiff)
        maxDiff = diff;
    }

    Assert.That(maxDiff, Is.LessThanOrEqualTo(10), $"Max pixel difference was {maxDiff}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_DimensionsMatch() {
    const int w = 64;
    const int h = 48;
    var pixels = new byte[w * h];
    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x)
      pixels[y * w + x] = (byte)(x * 255 / (w - 1));

    var original = new WsqFile {
      Width = w,
      Height = h,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(original);
    var restored = WsqReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(w));
    Assert.That(restored.Height, Is.EqualTo(h));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_PixelsMostlyClose() {
    const int w = 64;
    const int h = 64;
    var pixels = new byte[w * h];
    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x)
      pixels[y * w + x] = (byte)(x * 255 / (w - 1));

    var original = new WsqFile {
      Width = w,
      Height = h,
      CompressionRatio = 0.9,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(original);
    var restored = WsqReader.FromBytes(bytes);

    // Compute PSNR or mean absolute error
    var totalError = 0L;
    for (var i = 0; i < pixels.Length; ++i)
      totalError += Math.Abs(restored.PixelData[i] - pixels[i]);

    var mae = (double)totalError / pixels.Length;
    Assert.That(mae, Is.LessThan(30), $"Mean absolute error was {mae:F2}");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Pattern_DimensionsMatch() {
    const int w = 32;
    const int h = 32;
    var pixels = new byte[w * h];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)((i % 2 == 0) ? 200 : 50);

    var original = new WsqFile {
      Width = w,
      Height = h,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(original);
    var restored = WsqReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(w));
    Assert.That(restored.Height, Is.EqualTo(h));
    Assert.That(restored.PixelData.Length, Is.EqualTo(w * h));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    const int w = 32;
    const int h = 32;
    var pixels = new byte[w * h];
    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x)
      pixels[y * w + x] = (byte)((x + y) * 4);

    var original = new WsqFile {
      Width = w,
      Height = h,
      PixelData = pixels
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wsq");
    try {
      var bytes = WsqWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = WsqReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(w));
      Assert.That(restored.Height, Is.EqualTo(h));
      Assert.That(restored.PixelData.Length, Is.EqualTo(w * h));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    const int w = 32;
    const int h = 32;
    var pixels = new byte[w * h];
    Array.Fill(pixels, (byte)100);

    var rawImage = new RawImage {
      Width = w,
      Height = h,
      Format = PixelFormat.Gray8,
      PixelData = pixels
    };

    var wsqFile = WsqFile.FromRawImage(rawImage);
    var bytes = WsqWriter.ToBytes(wsqFile);
    var restored = WsqReader.FromBytes(bytes);
    var restoredRaw = WsqFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(w));
    Assert.That(restoredRaw.Height, Is.EqualTo(h));
    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(restoredRaw.PixelData.Length, Is.EqualTo(w * h));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Compression_SmallerThanRaw() {
    const int w = 128;
    const int h = 128;
    var pixels = new byte[w * h];
    // Fingerprint-like content: smooth with some detail
    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x)
      pixels[y * w + x] = (byte)(128 + 50 * Math.Sin(x * 0.3) * Math.Cos(y * 0.2));

    var file = new WsqFile {
      Width = w,
      Height = h,
      CompressionRatio = 0.5,
      PixelData = pixels
    };

    var bytes = WsqWriter.ToBytes(file);

    // WSQ compressed should be significantly smaller than raw
    Assert.That(bytes.Length, Is.LessThan(w * h));
  }
}
