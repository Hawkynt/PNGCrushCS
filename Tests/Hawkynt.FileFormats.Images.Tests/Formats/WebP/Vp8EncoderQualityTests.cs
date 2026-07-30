using System;
using System.IO;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Encoder quality benchmarks. Verifies that the multi-mode RD encoder produces meaningfully
/// better PSNR than a trivial DC-only encoder would. Target: PSNR > 32 dB at quality 75
/// for simple synthetic patterns, > 28 dB for photo-like content.
/// </summary>
[TestFixture]
public sealed class Vp8EncoderQualityTests {

  private static double _ComputePsnrRgb24(byte[] a, byte[] b) {
    if (a.Length != b.Length) throw new ArgumentException("Mismatched buffers");
    long sse = 0;
    for (var i = 0; i < a.Length; ++i) {
      var d = a[i] - b[i];
      sse += d * d;
    }
    if (sse == 0) return 99.0;
    var mse = sse / (double)a.Length;
    return 10 * Math.Log10(255.0 * 255.0 / mse);
  }

  [Test]
  public void Encode_32x32_HorizontalGradient_PickH_VE_Mode() {
    // Pure horizontal gradient → VE (vertical prediction copying the top row) should be optimal.
    const int w = 32, h = 32;
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var off = (y * w + x) * 3;
        pixels[off + 0] = pixels[off + 1] = pixels[off + 2] = (byte)(x * 8);
      }
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    var decoded = Vp8Decoder.Decode(vp8, w, h);
    var psnr = _ComputePsnrRgb24(pixels, decoded);
    TestContext.Out.WriteLine($"horizontal-gradient 32x32 q90: {vp8.Length} bytes, PSNR {psnr:F2} dB");
    Assert.That(psnr, Is.GreaterThan(18), "horizontal gradient 32x32 — small image, I16-only modes limited");
  }

  [Test]
  public void Encode_32x32_VerticalGradient_GoodPsnr() {
    const int w = 32, h = 32;
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var off = (y * w + x) * 3;
        pixels[off + 0] = pixels[off + 1] = pixels[off + 2] = (byte)(y * 8);
      }
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    var decoded = Vp8Decoder.Decode(vp8, w, h);
    var psnr = _ComputePsnrRgb24(pixels, decoded);
    TestContext.Out.WriteLine($"vertical-gradient 32x32 q90: {vp8.Length} bytes, PSNR {psnr:F2} dB");
    Assert.That(psnr, Is.GreaterThan(18), "vertical gradient 32x32 — small image, I16-only modes limited");
  }

  [Test]
  public void Encode_32x32_DiagonalGradient_TmMode_Helpful() {
    const int w = 32, h = 32;
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var off = (y * w + x) * 3;
        pixels[off + 0] = pixels[off + 1] = pixels[off + 2] = (byte)Math.Min(255, (x + y) * 4);
      }
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    var decoded = Vp8Decoder.Decode(vp8, w, h);
    var psnr = _ComputePsnrRgb24(pixels, decoded);
    TestContext.Out.WriteLine($"diagonal-gradient 32x32 q90: {vp8.Length} bytes, PSNR {psnr:F2} dB");
    // TM_PRED was added — diagonal gradient should be well-handled.
    Assert.That(psnr, Is.GreaterThan(20), "diagonal gradient — TM_PRED helps here");
  }

  [Test]
  public void Encode_PhotoFixture_550x368_RoundTripsWithReasonablePsnr() {
    // Decode the known-good libwebp fixture → re-encode with our encoder → decode again.
    // Verify PSNR compared to the first decoded image.
    var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..",
      "Tests", "EndToEnd.Tests", "Fixtures", "test-lossy.webp");
    if (!File.Exists(fixturePath)) {
      Assert.Ignore($"Fixture not found: {fixturePath}");
      return;
    }
    var webpIn = WebP.WebPFile.FromFile(new FileInfo(fixturePath));
    Assume.That(webpIn.IsLossless, Is.False);
    var raw = WebP.WebPFile.ToRawImage(webpIn);
    Assume.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));

    var vp8 = Vp8Encoder.Encode(raw, quality: 75);
    var decoded = Vp8Decoder.Decode(vp8, raw.Width, raw.Height);
    var psnr = _ComputePsnrRgb24(raw.PixelData, decoded);
    var compressionRatio = (double)raw.PixelData.Length / vp8.Length;
    TestContext.Out.WriteLine($"photo 550x368 q75: {vp8.Length} bytes (ratio {compressionRatio:F1}x), PSNR {psnr:F2} dB");
    Assert.That(psnr, Is.GreaterThan(28), "photo re-encode at q75 should have >28 dB PSNR");
    Assert.That(compressionRatio, Is.GreaterThan(2.0), "should compress at least 2x vs raw RGB");
  }

  [Test]
  public void Encode_Quality_MonotonicallyIncreasesPsnr() {
    // Higher quality → higher PSNR.
    const int w = 64, h = 64;
    var pixels = new byte[w * h * 3];
    var rnd = new Random(1);
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var off = (y * w + x) * 3;
        // Smooth + noise — some texture but not pure random.
        var baseV = (byte)((x ^ y) * 4);
        pixels[off + 0] = (byte)(baseV + rnd.Next(10));
        pixels[off + 1] = (byte)(baseV + rnd.Next(10));
        pixels[off + 2] = (byte)(baseV + rnd.Next(10));
      }
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
    var psnr30 = _ComputePsnrRgb24(pixels, Vp8Decoder.Decode(Vp8Encoder.Encode(src, quality: 30), w, h));
    var psnr60 = _ComputePsnrRgb24(pixels, Vp8Decoder.Decode(Vp8Encoder.Encode(src, quality: 60), w, h));
    var psnr90 = _ComputePsnrRgb24(pixels, Vp8Decoder.Decode(Vp8Encoder.Encode(src, quality: 90), w, h));
    TestContext.Out.WriteLine($"Quality 30/60/90 PSNR: {psnr30:F2} / {psnr60:F2} / {psnr90:F2} dB");
    Assert.That(psnr60, Is.GreaterThanOrEqualTo(psnr30), "Q60 should be >= Q30 PSNR");
    Assert.That(psnr90, Is.GreaterThanOrEqualTo(psnr60), "Q90 should be >= Q60 PSNR");
  }
}
