using System;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

/// <summary>End-to-end round-trip: encode a RawImage → raw VP8 bytes → decode back → compare.
/// The encoder is minimal (method-0, DC prediction only, no RD), so we test structural integrity
/// and gross pixel fidelity (PSNR > 20 dB), not bit-exact round-trip.</summary>
[TestFixture]
public sealed class Vp8EncoderRoundTripTests {

  [Test]
  public void Encode_Decode_16x16_Solid_ProducesValidOutput() {
    // Solid gray should round-trip exactly (DC prediction is perfect for uniform regions).
    var pixels = new byte[16 * 16 * 3];
    Array.Fill(pixels, (byte)128);
    var src = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Rgb24, PixelData = pixels };

    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    TestContext.Out.WriteLine($"Encoded {16 * 16 * 3} bytes of RGB as {vp8.Length} bytes of VP8");
    Assert.That(vp8, Is.Not.Null);
    Assert.That(vp8.Length, Is.GreaterThan(10)); // at least the 10-byte header

    var rgb = Vp8Decoder.Decode(vp8, 16, 16);
    Assert.That(rgb.Length, Is.EqualTo(16 * 16 * 3));
    // Allow some rounding error for solid gray (YUV conversion is lossy at studio-range boundaries).
    var maxDiff = 0;
    for (var i = 0; i < rgb.Length; ++i) {
      var d = Math.Abs(rgb[i] - 128);
      if (d > maxDiff) maxDiff = d;
    }
    Assert.That(maxDiff, Is.LessThan(10), $"solid gray should round-trip within ±10 (saw {maxDiff})");
  }

  [Test]
  public void Encode_Decode_32x32_Gradient_PsnrAbove20() {
    // Linear gradient. Test that decoder doesn't throw AND output is recognizably similar.
    const int w = 32, h = 32;
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x) {
        var off = (y * w + x) * 3;
        pixels[off + 0] = (byte)(x * 8);
        pixels[off + 1] = (byte)(y * 8);
        pixels[off + 2] = 128;
      }
    }
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };

    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    TestContext.Out.WriteLine($"Gradient {w}x{h} encoded to {vp8.Length} bytes");

    var rgb = Vp8Decoder.Decode(vp8, w, h);
    Assert.That(rgb.Length, Is.EqualTo(w * h * 3));

    long sse = 0;
    for (var i = 0; i < pixels.Length; ++i) {
      var d = rgb[i] - pixels[i];
      sse += d * d;
    }
    var mse = sse / (double)pixels.Length;
    var psnr = mse > 0 ? 10 * Math.Log10(255.0 * 255.0 / mse) : 99;
    TestContext.Out.WriteLine($"PSNR: {psnr:F2} dB (MSE={mse:F2})");
    Assert.That(psnr, Is.GreaterThan(20), "Minimum acceptable PSNR for lossy round-trip");
  }

  [Test]
  public void Encode_Decode_64x64_Random_DecodesWithoutError() {
    // Noisy input — just verify no crash, decoder consumes all bytes correctly.
    const int w = 64, h = 64;
    var pixels = new byte[w * h * 3];
    var rnd = new Random(42);
    rnd.NextBytes(pixels);
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };

    var vp8 = Vp8Encoder.Encode(src, quality: 75);
    TestContext.Out.WriteLine($"Random {w}x{h} encoded to {vp8.Length} bytes");
    Assert.That(() => Vp8Decoder.Decode(vp8, w, h), Throws.Nothing);
  }

  [Test]
  public void Encode_OddDimensions_Pads_And_DecodesCorrectSize() {
    // 13x7 is an awkward size — must pad to 16x16 MB grid internally, but output must be 13x7.
    const int w = 13, h = 7;
    var pixels = new byte[w * h * 3];
    for (var i = 0; i < pixels.Length; ++i) pixels[i] = (byte)(i & 0xff);
    var src = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };

    var vp8 = Vp8Encoder.Encode(src, quality: 90);
    var rgb = Vp8Decoder.Decode(vp8, w, h);
    Assert.That(rgb.Length, Is.EqualTo(w * h * 3));
  }
}
