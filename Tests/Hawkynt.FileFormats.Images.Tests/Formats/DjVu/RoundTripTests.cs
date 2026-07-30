using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DjVu;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class RoundTripTests {

  /// <summary>
  /// Maximum per-channel absolute error tolerated for IW44 lossy wavelet round-trip.
  /// IW44 uses quantized wavelet coefficients, so exact pixel match is not expected.
  /// </summary>
  private const int _IW44_TOLERANCE = 64;

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_SmallImage_DimensionsPreserved() {
    const int width = 4;
    const int height = 3;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var idx = (y * width + x) * 3;
      pixelData[idx] = (byte)(x * 60 + 20);
      pixelData[idx + 1] = (byte)(y * 80 + 40);
      pixelData[idx + 2] = 128;
    }

    var original = new DjVuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData.Length, Is.EqualTo(original.PixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_SmallImage_PixelsApproximate() {
    const int width = 4;
    const int height = 3;
    var pixelData = new byte[width * height * 3];

    // Smooth color gradient: colors change slowly, suitable for 4:2:0 chroma subsampling
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var idx = (y * width + x) * 3;
      pixelData[idx] = (byte)(x * 60 + 20);      // R: 20-200 gradient
      pixelData[idx + 1] = (byte)(y * 80 + 40);   // G: 40-200 gradient
      pixelData[idx + 2] = 128;                    // B: constant
    }

    var original = new DjVuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new DjVuFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[8 * 8 * 3]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var width = 16;
    var height = 16;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 16);     // R gradient
        pixelData[idx + 1] = (byte)(y * 16); // G gradient
        pixelData[idx + 2] = 128;            // B constant
      }

    var original = new DjVuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    const int width = 8;
    const int height = 6;
    var pixelData = new byte[width * height * 3];

    // Photo-like gradient: smooth color transitions within 2x2 blocks
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var idx = (y * width + x) * 3;
      pixelData[idx] = (byte)(x * 30 + 10);       // R: smooth horizontal gradient
      pixelData[idx + 1] = (byte)(y * 40 + 20);   // G: smooth vertical gradient
      pixelData[idx + 2] = (byte)((x + y) * 20);  // B: diagonal gradient
    }

    var original = new DjVuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".djvu");
    try {
      var bytes = DjVuWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DjVuReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData
    };

    var djvuFile = DjVuFile.FromRawImage(rawImage);
    var roundTripped = DjVuFile.ToRawImage(djvuFile);

    Assert.That(roundTripped.Width, Is.EqualTo(4));
    Assert.That(roundTripped.Height, Is.EqualTo(4));
    Assert.That(roundTripped.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(roundTripped.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DpiPreserved() {
    var original = new DjVuFile {
      Width = 2,
      Height = 2,
      Dpi = 600,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Dpi, Is.EqualTo(600));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GammaPreserved() {
    var original = new DjVuFile {
      Width = 2,
      Height = 2,
      Gamma = 33,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Gamma, Is.EqualTo(33));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VersionPreserved() {
    var original = new DjVuFile {
      Width = 2,
      Height = 2,
      VersionMajor = 1,
      VersionMinor = 26,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.VersionMajor, Is.EqualTo(1));
    Assert.That(restored.VersionMinor, Is.EqualTo(26));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FlagsPreserved() {
    var original = new DjVuFile {
      Width = 2,
      Height = 2,
      Flags = 0x01,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Flags, Is.EqualTo(0x01));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawChunksPreserved() {
    var original = new DjVuFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
      RawChunks = [
        new DjVuChunk { ChunkId = "ANTa", Data = new byte[] { 0x41, 0x42, 0x43 } },
        new DjVuChunk { ChunkId = "TXTz", Data = new byte[] { 0x01, 0x02 } }
      ]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.RawChunks, Has.Count.EqualTo(2));
    Assert.That(restored.RawChunks[0].ChunkId, Is.EqualTo("ANTa"));
    Assert.That(restored.RawChunks[0].Data, Is.EqualTo(new byte[] { 0x41, 0x42, 0x43 }));
    Assert.That(restored.RawChunks[1].ChunkId, Is.EqualTo("TXTz"));
    Assert.That(restored.RawChunks[1].Data, Is.EqualTo(new byte[] { 0x01, 0x02 }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new DjVuFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData.Length, Is.EqualTo(3));
    _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    const int width = 64;
    const int height = 48;
    var pixelData = new byte[width * height * 3];

    // Smooth color field resembling a natural image: bilinear gradient with slow transitions
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var idx = (y * width + x) * 3;
      pixelData[idx] = (byte)(x * 4);                        // R: 0-252 horizontal
      pixelData[idx + 1] = (byte)(y * 5 + 8);                // G: 8-243 vertical
      pixelData[idx + 2] = (byte)(128 + (x - 32) + (y - 24)); // B: centered
    }

    var original = new DjVuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = DjVuWriter.ToBytes(original);
    var restored = DjVuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    _AssertPixelsApproximate(original.PixelData, restored.PixelData, _IW44_TOLERANCE);
  }

  /// <summary>
  /// Asserts that two pixel arrays are approximately equal within a per-channel tolerance.
  /// Computes the peak absolute error and asserts it is within the tolerance.
  /// </summary>
  private static void _AssertPixelsApproximate(byte[] expected, byte[] actual, int tolerance) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length), "Pixel data length mismatch");

    var maxError = 0;
    var maxErrorIdx = -1;
    for (var i = 0; i < expected.Length; ++i) {
      var error = Math.Abs(expected[i] - actual[i]);
      if (error > maxError) {
        maxError = error;
        maxErrorIdx = i;
      }
    }

    Assert.That(maxError, Is.LessThanOrEqualTo(tolerance),
      $"Peak per-channel error {maxError} at index {maxErrorIdx} " +
      $"(expected {(maxErrorIdx >= 0 ? expected[maxErrorIdx] : -1)}, " +
      $"got {(maxErrorIdx >= 0 ? actual[maxErrorIdx] : -1)}) exceeds tolerance {tolerance}");
  }
}
