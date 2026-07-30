using System;
using System.IO;
using System.Linq;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Bit-position diagnostic for 8x8_vardct.jxl. djxl 0.11.2 decodes this
/// fixture to a near-uniform image of RGB(128, 0, 2). Smallest VarDCT
/// fixture available — exercises the full DC/AC/IDCT/Gaborish/EPF pipeline
/// at the minimum complexity (single 8x8 block).
/// </summary>
[TestFixture]
public sealed class VarDct8x8Tests {

  [Test]
  public void Vardct_8x8_DiagnosticDump() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "8x8_vardct.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecImage(bytes, out var meta, out var img);
    TestContext.Out.WriteLine(
      $"ok={ok}, meta=[{meta.Width}x{meta.Height} {meta.BitsPerSample}u xyb={meta.IsXybEncoded} modular={meta.IsModularFrame}], " +
      $"image={(img != null ? img.GetType().Name : "null")}");
    Assert.Pass();
  }

  /// <summary>Print the XYB-channel float values that our pipeline produces.
  /// libjxl decodes 8x8_vardct.jxl to Y_dc≈0.2367, X_dc≈0.0163, B_dc≈0.2280
  /// (uniform across all 64 pixels because every AC coefficient is zero).</summary>
  [Test]
  public void Vardct_8x8_DumpXybFloats() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "8x8_vardct.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecImage(bytes, out _, out var img);
    Assert.That(ok, Is.True);
    Assert.That(img, Is.InstanceOf<Codec.JxlVarDctImage>());
    var vardct = (Codec.JxlVarDctImage)img!;
    TestContext.Out.WriteLine($"channel[0] (X) px[0]={vardct.Channels[0][0]:F6}, libjxl ref X_dc=0.016339");
    TestContext.Out.WriteLine($"channel[1] (Y) px[0]={vardct.Channels[1][0]:F6}, libjxl ref Y_dc=0.236741");
    TestContext.Out.WriteLine($"channel[2] (B) px[0]={vardct.Channels[2][0]:F6}, libjxl ref B_dc=0.228027 (with cfl_y→b=1.0)");
    Assert.Pass();
  }

  /// <summary>libjxl 0.11.2 reference output for 8x8_vardct.jxl is a near-uniform
  /// reddish-grey image, RGB ~(128, 0, ~1-2). Verify our pipeline reaches this
  /// (or at least gets close enough that the AC decode is plausibly correct).</summary>
  [Test]
  public void Vardct_8x8_RgbMatchesLibjxlReference() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "8x8_vardct.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var width, out var height, out var rgb24);
    Assert.That(ok, Is.True, "TryReadSpecRgb24 should not return false for a valid 8x8 fixture.");
    Assert.That(width, Is.EqualTo(8));
    Assert.That(height, Is.EqualTo(8));
    Assert.That(rgb24, Is.Not.Null);
    Assert.That(rgb24!.Length, Is.EqualTo(8 * 8 * 3));

    // Print a per-pixel dump alongside the libjxl reference so any
    // divergence is immediately visible in the test log.
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("idx | ours        | libjxl-ref");
    var libjxlRef = new (byte R, byte G, byte B)[8] {
      (128, 0, 1), (128, 0, 2), (128, 0, 2), (128, 0, 2),
      (128, 0, 1), (128, 0, 2), (128, 0, 2), (128, 0, 2),
    };
    for (var i = 0; i < 8; ++i) {
      var r = rgb24[i * 3 + 0];
      var g = rgb24[i * 3 + 1];
      var b = rgb24[i * 3 + 2];
      sb.AppendLine($"  {i} | ({r,3},{g,3},{b,3}) | ({libjxlRef[i].R,3},{libjxlRef[i].G,3},{libjxlRef[i].B,3})");
    }
    TestContext.Out.WriteLine(sb.ToString());

    // Tight assertion: every pixel must be within ±2 LSB of the libjxl
    // reference. Sub-LSB differences are acceptable (sub-pixel rounding in
    // XYB→sRGB / 8-bit quantization rounding modes); structural divergence
    // (off-by-many or zeroed channels) is not.
    var libjxlReference = new byte[8 * 3] {
      128, 0, 1,  128, 0, 2,  128, 0, 2,  128, 0, 2,
      128, 0, 1,  128, 0, 2,  128, 0, 2,  128, 0, 2,
    };
    for (var i = 0; i < libjxlReference.Length; ++i) {
      var diff = System.Math.Abs(rgb24[i] - libjxlReference[i]);
      Assert.That(diff, Is.LessThanOrEqualTo(2),
        $"Byte {i} (pixel {i / 3}, channel {"RGB"[i % 3]}): " +
        $"ours={rgb24[i]}, libjxl-ref={libjxlReference[i]}, diff={diff}.");
    }
  }

  /// <summary>16x16 grayscale gradient encoded with cjxl -d 2.0 (VarDCT). This
  /// fixture exercises non-trivial AC coefficient decode (libjxl trace shows
  /// up to 34 nzeros per block in the Y channel). libjxl reference for the
  /// first row: pixel0=(1,1,1), pixel7=(109,109,109) — a smooth gradient.</summary>
  [Test]
  public void Vardct_16x16_DecodesPlausibleGradient() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "test_16x16_vardct.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecRgb24(bytes, out var width, out var height, out var rgb24);
    Assert.That(ok, Is.True, "TryReadSpecRgb24 should not return false for a valid 16x16 VarDCT fixture.");
    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(16));
    Assert.That(rgb24, Is.Not.Null);

    // libjxl reference for row 0 (grayscale, R=G=B per pixel)
    var libjxlRow0 = new byte[16] {
      1, 14, 32, 48, 63, 79, 96, 109,
      // We don't have row 0 pixels 8..15; print them and use a tolerance check
      0, 0, 0, 0, 0, 0, 0, 0, // unused
    };
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("idx | ours       | libjxl-ref");
    for (var x = 0; x < 8; ++x) {
      var r = rgb24[x * 3 + 0];
      var g = rgb24[x * 3 + 1];
      var b = rgb24[x * 3 + 2];
      sb.AppendLine($"  {x} | ({r,3},{g,3},{b,3}) | ({libjxlRow0[x],3},{libjxlRow0[x],3},{libjxlRow0[x],3})");
    }
    TestContext.Out.WriteLine(sb.ToString());

    // Tight check: every byte of row 0 within ±2 LSB of libjxl reference.
    var libjxlRow0Rgb = new byte[8 * 3] {
       1,  1,  1,  14, 14, 14,  32, 32, 32,  48, 48, 48,
      63, 63, 63,  79, 79, 79,  96, 96, 96, 109,109,109,
    };
    for (var i = 0; i < libjxlRow0Rgb.Length; ++i) {
      var diff = System.Math.Abs(rgb24[i] - libjxlRow0Rgb[i]);
      Assert.That(diff, Is.LessThanOrEqualTo(2),
        $"Byte {i} (pixel {i / 3}, channel {"RGB"[i % 3]}): " +
        $"ours={rgb24[i]}, libjxl-ref={libjxlRow0Rgb[i]}, diff={diff}.");
    }
  }
}
