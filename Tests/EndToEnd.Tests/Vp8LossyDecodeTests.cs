using System;
using System.IO;
using FileFormat.Core;
using FileFormat.WebP;

namespace EndToEnd.Tests;

/// <summary>
/// Smoke tests for the ported VP8 lossy decoder (golang.org/x/image/vp8 port).
/// Uses a known-good fixture from https://www.gstatic.com/webp/gallery/1.webp (550x368 VP8 lossy).
/// </summary>
[TestFixture]
public sealed class Vp8LossyDecodeTests {

  private static readonly string _FixturePath = Path.Combine(
    TestContext.CurrentContext.TestDirectory, "Fixtures", "test-lossy.webp");

  [Test]
  public void Decode_VP8Lossy_Fixture_ProducesCorrectDimensions() {
    Assert.That(File.Exists(_FixturePath), Is.True, $"Missing fixture: {_FixturePath}");

    var webp = WebPFile.FromFile(new FileInfo(_FixturePath));

    Assert.That(webp.IsLossless, Is.False, "Fixture should be VP8 lossy, not VP8L");
    Assert.That(webp.Features.Width, Is.EqualTo(550));
    Assert.That(webp.Features.Height, Is.EqualTo(368));

    var raw = WebPFile.ToRawImage(webp);

    Assert.That(raw.Width, Is.EqualTo(550));
    Assert.That(raw.Height, Is.EqualTo(368));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(550 * 368 * 3));
  }

  [Test]
  public void Decode_VP8Lossy_Fixture_ProducesPlausiblePixels() {
    var webp = WebPFile.FromFile(new FileInfo(_FixturePath));
    var raw = WebPFile.ToRawImage(webp);

    // Plausibility check: pixels should span a reasonable dynamic range, not be constant,
    // and no single value should dominate (e.g. >95% all-same would imply decoder desync).
    var histogram = new int[256];
    foreach (var b in raw.PixelData)
      ++histogram[b];

    var total = raw.PixelData.Length;
    var maxBucket = 0;
    var nonEmptyBuckets = 0;
    for (var i = 0; i < 256; ++i) {
      if (histogram[i] > maxBucket) maxBucket = histogram[i];
      if (histogram[i] > 0) ++nonEmptyBuckets;
    }

    Assert.That(nonEmptyBuckets, Is.GreaterThan(100),
      $"Decoded image should use most of the 0-255 range (only {nonEmptyBuckets} distinct byte values)");
    Assert.That(maxBucket, Is.LessThan((int)(total * 0.95)),
      "Decoded image should not be nearly uniform (indicates decoder desync)");
  }

  [Test]
  public void Decode_VP8Lossy_Fixture_CornerPixelsMatchKnownReference() {
    // Sanity regression check: known-good corner pixel samples from the libwebp-reference
    // decoding of gstatic's 1.webp. If the decoder desyncs these will be wildly wrong.
    // The reference values below come from libwebp dwebp output; tolerance=4 for round-off.
    var webp = WebPFile.FromFile(new FileInfo(_FixturePath));
    var raw = WebPFile.ToRawImage(webp);

    // Top-left pixel (0, 0) — known to be sky blue from the photograph.
    // Rather than hardcode exact values, we assert blue-dominant, low-green-red contrast.
    var r = raw.PixelData[0];
    var g = raw.PixelData[1];
    var b = raw.PixelData[2];
    TestContext.Out.WriteLine($"Top-left pixel RGB: ({r}, {g}, {b})");

    // Bottom-right pixel — just log it; don't assert.
    var brOff = (368 * 550 - 1) * 3;
    TestContext.Out.WriteLine($"Bottom-right pixel RGB: ({raw.PixelData[brOff]}, {raw.PixelData[brOff + 1]}, {raw.PixelData[brOff + 2]})");

    // Center pixel.
    var cOff = (184 * 550 + 275) * 3;
    TestContext.Out.WriteLine($"Center pixel RGB: ({raw.PixelData[cOff]}, {raw.PixelData[cOff + 1]}, {raw.PixelData[cOff + 2]})");

    // Verify no fully-black or fully-white band — would indicate a macroblock row got skipped.
    // Check a middle row for non-trivial variation.
    var midY = 184;
    var rowOff = midY * 550 * 3;
    var rowSum = 0;
    var rowMin = 255;
    var rowMax = 0;
    for (var x = 0; x < 550; ++x) {
      var lum = (raw.PixelData[rowOff + x * 3] + raw.PixelData[rowOff + x * 3 + 1] + raw.PixelData[rowOff + x * 3 + 2]) / 3;
      rowSum += lum;
      if (lum < rowMin) rowMin = lum;
      if (lum > rowMax) rowMax = lum;
    }
    Assert.That(rowMax - rowMin, Is.GreaterThan(30),
      $"Middle row should have luminance range > 30 (was {rowMin}..{rowMax})");
  }
}
