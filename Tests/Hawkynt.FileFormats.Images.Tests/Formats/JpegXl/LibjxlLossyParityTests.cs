using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What this reader returns for lossy files libjxl wrote, checked against what
/// libjxl returns for the same files.
/// </summary>
/// <remarks>
/// The lossless fixtures next door are compared byte for byte, and the lossy
/// ones could not be, which was read for a long time as the decoder still being
/// wrong. It was not. <c>djxl</c> does not write the value it decoded: every
/// eight-bit sample it writes has a blue-noise value added to it first, from a
/// fixed 32x32 pattern offset per channel, so that quantisation shows as noise
/// rather than as banding. It is in <c>stage_write.cc</c>, it applies to all
/// eight-bit output, and there is no switch for it.
///
/// <para>Comparing our undithered bytes against those dithered ones measures the
/// pattern and not the decoder — on these three fixtures it reports 1,701, 4,889
/// and 9,218 samples wrong. Adding the same pattern to our own decode first
/// takes those to 0, 3 and 2. That is the difference between a decoder that is
/// wrong everywhere by a little and one that is right, and only the second
/// story survives being measured this way.</para>
///
/// <para>Lossless files were never affected, which is why this went unnoticed:
/// their samples land on whole numbers, and a shift of less than half a step
/// rounds back to where it started.</para>
///
/// <para>What is left after the pattern is removed is a handful of samples per
/// picture that sit nearer a rounding boundary than the two decoders agree —
/// across the wider corpus the largest disagreement anywhere is a hundredth of
/// one eight-bit level. The reader does not dither, because the nearest value is
/// the honest answer to a decode; the pattern is applied here only to compare
/// like with like.</para>
/// </remarks>
[TestFixture]
public sealed class LibjxlLossyParityTests {

  private const int _DitherSize = 32;
  private const int _DitherStride = 48;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>libjxl's <c>kDither</c>, kept beside the fixtures rather than
  /// written out here because it is somebody else's table and reads as data.</summary>
  private static float[] _Dither() {
    var text = System.Text.Encoding.ASCII.GetString(_Fixture("libjxl_dither.txt"));
    var values = text
      .Split('\n')
      .Where(line => !line.StartsWith('#'))
      .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      .Select(token => float.Parse(token, CultureInfo.InvariantCulture))
      .ToArray();

    Assert.That(values, Has.Length.EqualTo(_DitherSize * _DitherStride));
    return values;
  }

  /// <param name="name">
  /// <c>cjxl_dct64_gradient</c> is one 64x64 transform from end to end,
  /// <c>cjxl_afv_corner</c> carries the cornered shape and an alpha plane, and
  /// <c>cjxl_two_groups_coefficients</c> is coded in two groups so a block's
  /// position within the picture differs from its position within its group.
  /// </param>
  /// <param name="allowed">How many samples may still differ. Each is a value
  /// closer to a rounding boundary than two independent float pipelines agree,
  /// and each is out by one level exactly.</param>
  [TestCase("cjxl_dct64_gradient", 0)]
  [TestCase("cjxl_afv_corner", 3)]
  [TestCase("cjxl_two_groups_coefficients", 2)]
  public void ALossyFileDecodesToWhatLibjxlDecodesItTo(string name, int allowed) {
    Assert.That(JpegXlReader.TryReadSpecImage(_Fixture(name + ".jxl"), out _, out var raw), Is.True);
    Assert.That(raw, Is.InstanceOf<JxlVarDctImage>(), $"{name} is meant to be a lossy frame.");
    var image = (JxlVarDctImage)raw!;

    var (width, height, expected) = _ReadPpm(_Fixture(name + ".ppm"));
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
    });

    var dither = _Dither();
    var differing = 0;
    var worst = 0;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = y * width + x;
      var (r, g, b) = JxlXybColorTransform.XybToLinearSrgb(
        image.Channels[0][at], image.Channels[1][at], image.Channels[2][at]);
      var gamma = new[] {
        JxlXybColorTransform.LinearSrgbToGamma(r),
        JxlXybColorTransform.LinearSrgbToGamma(g),
        JxlXybColorTransform.LinearSrgbToGamma(b),
      };

      for (var c = 0; c < 3; ++c) {
        // The pattern is offset per channel, which is why a grey picture does
        // not come out with all three channels moved the same way.
        var dx = (x + c * 23) % _DitherSize;
        var dy = (y + c * 13) % _DitherSize;
        var value = gamma[c] * 255.0f + dither[dy * _DitherStride + dx];
        var got = (byte)Math.Clamp((int)(Math.Clamp(value, 0.0f, 255.0f) + 0.5f), 0, 255);

        var want = expected[at * 3 + c];
        if (got == want)
          continue;

        ++differing;
        worst = Math.Max(worst, Math.Abs(got - want));
      }
    }

    Assert.Multiple(() => {
      Assert.That(worst, Is.LessThanOrEqualTo(1),
        $"{name}: a sample is out by {worst} levels, which is more than rounding.");
      Assert.That(differing, Is.LessThanOrEqualTo(allowed),
        $"{name}: {differing} samples differ from libjxl, and {allowed} is what rounding accounts for.");
    });
  }

  /// <summary>
  /// The comparison above is only worth anything if the pattern is really there,
  /// so this states what it costs to leave it out.
  /// </summary>
  [Test]
  public void ComparingWithoutTheDitherMeasuresThePatternRatherThanTheDecoder() {
    var file = JpegXlReader.FromBytes(_Fixture("cjxl_dct64_gradient.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("cjxl_dct64_gradient.ppm"));
    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));

    var differing = 0;
    var worst = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var apart = Math.Abs(file.PixelData[i] - expected[i]);
      if (apart == 0)
        continue;
      ++differing;
      worst = Math.Max(worst, apart);
    }

    Assert.Multiple(() => {
      // Never more than one level: it is a pattern of less than half a step.
      Assert.That(worst, Is.EqualTo(1));
      // And on a great many samples, which is what made it look like an error
      // spread over the whole picture rather than something added afterwards.
      Assert.That(differing, Is.GreaterThan(width * height / 4),
        "if this fell away, libjxl stopped dithering and the fixtures need rebuilding");
    });
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
