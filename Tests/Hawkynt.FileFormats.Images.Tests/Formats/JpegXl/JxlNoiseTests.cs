using System;
using System.IO;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The noise layer (ISO/IEC 18181-1; libjxl <c>lib/jxl/dec_noise.cc</c> and
/// <c>render_pipeline/stage_noise.cc</c>).
/// </summary>
/// <remarks>
/// A frame may ask for grain to be put back into it. The encoder throws the
/// grain of a photograph away, because it costs a great many bits and carries
/// almost nothing, and states instead how much of it there was at eight
/// brightness levels; the decoder generates a field and shapes it to that.
///
/// <para>The field is not a decoder's to choose. It comes from a stated
/// generator seeded with the frame's index and the group's corner, so every
/// decoder puts the same grain in the same places. A field of somebody else's
/// random numbers is a different picture, and this test exists to say so: the
/// fixture below is the same picture encoded with <c>--photon_noise_iso=3200</c>.</para>
/// </remarks>
[TestFixture]
public sealed class JxlNoiseTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A noisy frame decodes to what libjxl decodes it to.
  /// </summary>
  /// <remarks>
  /// Within one level, as with every other lossy file, because <c>djxl</c>
  /// dithers what it writes at eight bits. Measured against its float output
  /// instead, every one of the 9,216 samples is the exact rounding of libjxl's
  /// value.
  /// </remarks>
  [Test]
  public void ANoisyFrameDecodesToWhatLibjxlDecodesItTo() {
    var file = JpegXlReader.FromBytes(_Fixture("cjxl_photon_noise.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("cjxl_photon_noise.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });
    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));

    var worst = 0;
    for (var i = 0; i < expected.Length; ++i)
      worst = Math.Max(worst, Math.Abs(file.PixelData[i] - expected[i]));

    Assert.That(worst, Is.LessThanOrEqualTo(1),
      $"a sample is out by {worst} levels, which is more than libjxl's output dither can explain");
  }

  /// <summary>
  /// The generator is libjxl's, and its first numbers are checked against the
  /// algorithm rather than against this decoder's own output.
  /// </summary>
  /// <remarks>
  /// Worked out by hand from <c>Xorshift128Plus</c>'s SplitMix64 seeding and
  /// its first turn, with all four seed words zero. If this drifts, the grain
  /// moves and no amount of it looking like grain will make the picture right.
  /// </remarks>
  [Test]
  public void TheGeneratorProducesTheStatedNumbers() {
    var expected = new[] {
      1.961847f, 1.766622f, 1.994044f, 1.563523f, 1.951339f, 1.406206f, 1.610392f, 1.197987f,
      1.771285f, 1.665451f, 1.371710f, 1.693327f, 1.940394f, 1.657853f, 1.108799f, 1.738033f,
    };

    var actual = JxlNoise.FirstRandomNumbersForTest(0, 0, 0, 0);

    Assert.That(actual, Has.Length.EqualTo(expected.Length));
    for (var i = 0; i < expected.Length; ++i)
      Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-6f), $"number {i}");
  }

  /// <summary>
  /// A noisy picture of more than one group decodes to what libjxl decodes it
  /// to.
  /// </summary>
  /// <remarks>
  /// The field is not one field but one per group, each starting the generator
  /// afresh from that group's corner, and the filter that shapes them runs
  /// across the whole picture — so a group's edge takes the numbers of the
  /// group beside it rather than a reflection of its own. This fixture is 260
  /// square against a group of 256, so it has four groups and the last of them
  /// is four pixels wide and four tall: the case where a group's own width
  /// decides how many times the generator turns per row.
  ///
  /// <para>Measured against libjxl's float output, 13 of its 202,800 samples
  /// differ and every one is on a rounding boundary.</para>
  /// </remarks>
  [Test]
  public void ANoisyPictureOfSeveralGroupsDecodesToWhatLibjxlDecodesItTo() {
    var file = JpegXlReader.FromBytes(_Fixture("cjxl_photon_noise_groups.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("cjxl_photon_noise_groups.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(width, Is.EqualTo(260), "wider than one group, and not by a whole one");
    });
    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));

    var worst = 0;
    for (var i = 0; i < expected.Length; ++i)
      worst = Math.Max(worst, Math.Abs(file.PixelData[i] - expected[i]));

    Assert.That(worst, Is.LessThanOrEqualTo(1),
      $"a sample is out by {worst} levels, which is more than libjxl's output dither can explain");
  }

  /// <summary>
  /// Each group starts the generator from its own corner, so the same picture
  /// read as one group is a different field.
  /// </summary>
  [Test]
  public void EachGroupStartsTheGeneratorFromItsOwnCorner() {
    var first = JxlNoise.FirstRandomNumbersForTest(1, 0, 0, 0);
    var second = JxlNoise.FirstRandomNumbersForTest(1, 0, 256, 0);
    var below = JxlNoise.FirstRandomNumbersForTest(1, 0, 0, 256);

    Assert.Multiple(() => {
      Assert.That(second, Is.Not.EqualTo(first));
      Assert.That(below, Is.Not.EqualTo(first));
      Assert.That(below, Is.Not.EqualTo(second));
    });
  }

  /// <summary>The curve is eight values of ten bits, in a thousand and
  /// twenty-fourths.</summary>
  [Test]
  public void TheCurveIsEightTenBitValues() {
    // Eight values of 512, which is a half each, packed ten bits at a time.
    var bits = new byte[16];
    var at = 0;
    for (var i = 0; i < 8; ++i)
      for (var b = 0; b < 10; ++b) {
        if ((512 >> b & 1) != 0)
          bits[at >> 3] |= (byte)(1 << (at & 7));
        ++at;
      }

    var lut = JxlNoise.Decode(new JxlBitReader(bits, 0));

    Assert.That(lut, Has.Length.EqualTo(8));
    foreach (var value in lut)
      Assert.That(value, Is.EqualTo(0.5f).Within(1e-6f));
  }

  /// <summary>A curve of nothing asks for nothing and leaves the frame alone.</summary>
  [Test]
  public void ACurveOfNothingLeavesTheFrameAlone() {
    var planes = new float[3][];
    for (var c = 0; c < 3; ++c)
      planes[c] = [0.25f, 0.5f, 0.75f, 1.0f];
    var before = (float[])planes[1].Clone();

    Assert.That(JxlNoise.HasAny(new float[8]), Is.False);
    JxlNoise.Apply(planes, 2, 2, new float[8], 0.0f, 1.0f, groupDim: 256);

    Assert.That(planes[1], Is.EqualTo(before));
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
