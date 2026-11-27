using System;
using System.Globalization;
using FileFormat.Core;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Single lossy frames that carry an alpha plane, against what libjxl decodes them to.
/// </summary>
/// <remarks>
/// A lossy frame keeps its alpha plane as an extra channel of its modular side-stream. Where the
/// plane fits in one group it is coded in the frame's global stream; where it does not, the global
/// stream states no channel at all and each group carries its own piece after its coefficients.
/// Both files here were written by <c>cjxl</c> 0.13 at distance 2 — the second two groups wide and
/// with the edge-preserving filter forced on — and <c>djxl</c>'s decode of each is kept beside it.
/// The alpha plane is whole numbers and is compared exactly; the colour is compared through
/// libjxl's output dither, as <see cref="LibjxlLossyParityTests"/> explains.
/// </remarks>
[TestFixture]
public sealed class JxlLossyAlphaTests {

  [TestCase("cjxl_lossy_alpha_one_group")]
  [TestCase("cjxl_lossy_alpha_two_groups")]
  public void ALossyFrameKeepsTheAlphaPlaneLibjxlDecodes(string name) {
    var (width, height, depth, expected) = _ReadPam(TestHelper.Fixture(name + ".pam"));
    Assert.That(depth, Is.EqualTo(4));

    var raster = JpegXlFile.ToRawImage(JpegXlReader.FromBytes(TestHelper.Fixture(name + ".jxl")));
    Assert.Multiple(() => {
      Assert.That(raster.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That((raster.Width, raster.Height), Is.EqualTo((width, height)));
    });

    var wrongAlpha = 0;
    for (var i = 0; i < width * height; ++i)
      if (raster.PixelData[i * 4 + 3] != expected[i * 4 + 3])
        ++wrongAlpha;
    Assert.That(wrongAlpha, Is.Zero, $"{name}: {wrongAlpha} alpha samples differ from libjxl.");

    Assert.That(JpegXlReader.TryReadSpecImage(TestHelper.Fixture(name + ".jxl"), out _, out var raw), Is.True);
    var image = (JxlVarDctImage)raw!;
    var dither = TestHelper.Dither();
    var differing = 0;
    var worst = 0;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = y * width + x;
      var (r, g, b) = JxlXybColorTransform.XybToLinearSrgb(image.Channels[0][at], image.Channels[1][at], image.Channels[2][at]);
      var gamma = new[] { JxlXybColorTransform.LinearSrgbToGamma(r), JxlXybColorTransform.LinearSrgbToGamma(g), JxlXybColorTransform.LinearSrgbToGamma(b) };
      for (var c = 0; c < 3; ++c) {
        var value = gamma[c] * 255.0f + TestHelper.DitherAt(dither, x, y, c);
        var got = (byte)Math.Clamp((int)(Math.Clamp(value, 0.0f, 255.0f) + 0.5f), 0, 255);
        var want = expected[at * 4 + c];
        if (got == want)
          continue;
        ++differing;
        worst = Math.Max(worst, Math.Abs(got - want));
      }
    }

    var allowed = Math.Max(1, width * height * 3 / 2000);
    Assert.Multiple(() => {
      Assert.That(worst, Is.LessThanOrEqualTo(1), $"{name}: a colour sample is out by {worst} levels.");
      Assert.That(differing, Is.LessThanOrEqualTo(allowed), $"{name}: {differing} colour samples differ, rounding accounts for {allowed}.");
    });
  }

  private static (int Width, int Height, int Depth, byte[] Samples) _ReadPam(byte[] data) {
    var end = 0;
    var header = string.Empty;
    while (!header.EndsWith("ENDHDR\n", StringComparison.Ordinal))
      header += (char)data[end++];
    int width = 0, height = 0, depth = 0;
    foreach (var line in header.Split('\n')) {
      var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length != 2)
        continue;
      var value = parts[0] is "WIDTH" or "HEIGHT" or "DEPTH" ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
      switch (parts[0]) {
        case "WIDTH": width = value; break;
        case "HEIGHT": height = value; break;
        case "DEPTH": depth = value; break;
      }
    }

    return (width, height, depth, data.AsSpan(end, width * height * depth).ToArray());
  }
}
