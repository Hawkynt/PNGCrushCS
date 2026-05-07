using System;
using System.IO;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Bit-position diagnostic for minimal_8x8.jxl. djxl 0.11.2 decodes this
/// fixture to a solid 8x8 image of RGB(128, 0, 0). The tests below exercise
/// the parser pipeline and document where bit alignment matches the reference
/// (libjxl `frame_header.cc`, `dec_modular.cc`).
/// </summary>
[TestFixture]
public sealed class MinimalDecodeVerifyTests {

  [Test]
  public void Minimal_8x8_DecodesToSolidDarkRed() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "minimal_8x8.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var w, out var h, out var rgb);

    Assert.That(ok, Is.True, "ok=True expected");
    Assert.That(w, Is.EqualTo(8));
    Assert.That(h, Is.EqualTo(8));
    Assert.That(rgb, Is.Not.Null);
    Assert.That(rgb!.Length, Is.EqualTo(8 * 8 * 3));

    // djxl 0.11.2 reference: every pixel is RGB(128, 0, 0) — byte-correct.
    for (var i = 0; i < 64; ++i) {
      Assert.That(rgb[i * 3 + 0], Is.EqualTo(128), $"R mismatch at pixel {i}");
      Assert.That(rgb[i * 3 + 1], Is.EqualTo(0), $"G mismatch at pixel {i}");
      Assert.That(rgb[i * 3 + 2], Is.EqualTo(0), $"B mismatch at pixel {i}");
    }
  }

  /// <summary>
  /// 4x4 modular RGB fixture encoded by cjxl 0.11.2 (lossless modular, no
  /// container, no XYB). Non-trivial pixel content so this test detects
  /// mis-decode that would silently pass via the all-zero placeholder
  /// fallback.
  /// </summary>
  [Test]
  public void Test_4x4_Modular_DecodesGradient() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "test_4x4_modular.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var w, out var h, out var rgb);
    Assert.That(ok, Is.True);
    Assert.That(w, Is.EqualTo(4));
    Assert.That(h, Is.EqualTo(4));

    // djxl 0.11.2 reference (verified by hex-dumping the .ppm output of
    // djxl on this fixture). Note pixel 12 is (0, 0, 255) — the R=255 value
    // that appears in earlier rows belongs to pixel 15.
    var expected = new byte[] {
      0,0,0,      64,0,0,    128,0,0,   255,0,0,
      0,64,0,     0,128,0,   0,255,0,   64,64,64,
      0,0,128,    0,64,128,  0,128,255, 0,255,128,
      0,0,255,    64,0,255,  128,0,255, 255,0,255,
    };
    Assert.That(rgb!.Length, Is.EqualTo(expected.Length));
    for (var i = 0; i < expected.Length; ++i)
      Assert.That(rgb[i], Is.EqualTo(expected[i]), $"Byte {i}: expected {expected[i]}, got {rgb[i]}");
  }

  /// <summary>
  /// 16x16 modular RGB fixture. Each pixel is (x*17, y*17, (x+y)*12) — a
  /// pattern with high horizontal/vertical correlation that exercises the
  /// Gradient predictor more than the smaller fixtures. Encoded by cjxl
  /// 0.11.2 with `-d 0 -m 1` (lossless modular).
  /// </summary>
  [Test]
  public void Test_16x16_Modular_DecodesPattern() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "test_16x16_modular.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var w, out var h, out var rgb);
    Assert.That(ok, Is.True);
    Assert.That(w, Is.EqualTo(16));
    Assert.That(h, Is.EqualTo(16));
    Assert.That(rgb!.Length, Is.EqualTo(16 * 16 * 3));

    // Reference: r = (x*17) & 0xFF, g = (y*17) & 0xFF, b = ((x+y)*12) & 0xFF.
    var fail = 0;
    for (var y = 0; y < 16; ++y) {
      for (var x = 0; x < 16; ++x) {
        var i = (y * 16 + x) * 3;
        var er = (x * 17) & 0xFF;
        var eg = (y * 17) & 0xFF;
        var eb = ((x + y) * 12) & 0xFF;
        if (rgb[i + 0] != er || rgb[i + 1] != eg || rgb[i + 2] != eb) {
          if (fail < 3)
            TestContext.Out.WriteLine($"px({x},{y}) expected ({er},{eg},{eb}) got ({rgb[i]},{rgb[i + 1]},{rgb[i + 2]})");
          fail++;
        }
      }
    }
    Assert.That(fail, Is.EqualTo(0), $"{fail}/256 pixels mismatched");
  }

  [Test]
  public void Minimal_8x8_PrintsActualPixels() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "minimal_8x8.jxl");
    var bytes = File.ReadAllBytes(path);
    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var w, out var h, out var rgb);
    TestContext.Out.WriteLine($"ok={ok} dims={w}x{h}");
    if (ok && rgb is not null) {
      for (var i = 0; i < Math.Min(8, w * h); ++i)
        TestContext.Out.WriteLine($"  px[{i}] = ({rgb[i * 3]}, {rgb[i * 3 + 1]}, {rgb[i * 3 + 2]})");
    }
    Assert.Pass();
  }
}
