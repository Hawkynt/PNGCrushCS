using System;
using System.IO;
using FileFormat.Core;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Files whose samples are deeper than a byte, checked against <c>djxl</c>.
/// </summary>
/// <remarks>
/// These used to decode and then be dropped on the way out: the decoder read
/// them correctly and the packing step refused anything over eight bits,
/// because the picture handed back was a byte array. They are carried at
/// sixteen bits now rather than narrowed, since narrowing is a decision the
/// caller should get to make.
///
/// <para>They also make the plainest statement about the decoder that this
/// project has. libjxl only dithers eight-bit output — the pattern is applied
/// where the sample type is one byte wide and nowhere else — so at sixteen bits
/// there is nothing between the two decoders. A lossless file comes back
/// identical to <c>djxl</c> sample for sample at that depth, and the lossy one
/// differs by at most two parts in 65,535, which is under a hundredth of one
/// eight-bit level and matches what the float comparison says.</para>
/// </remarks>
[TestFixture]
public sealed class Libjxl16BitParityTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>A 16-bit file survives the whole way to a picture, at its depth.</summary>
  [TestCase("cjxl_16bit_lossless")]
  [TestCase("cjxl_16bit_lossy")]
  public void ADeepFileComesBackDeepRatherThanNarrowedOrRefused(string name) {
    var file = JpegXlReader.FromBytes(_Fixture(name + ".jxl"));

    Assert.Multiple(() => {
      Assert.That(file.BitsPerSample, Is.EqualTo(16));
      Assert.That(file.ComponentCount, Is.EqualTo(3));
      Assert.That(file.PixelData, Has.Length.EqualTo(file.Width * file.Height * 3 * 2));
    });

    var raw = JpegXlFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb48));
  }

  /// <param name="allowed">In units of the 16-bit sample. Zero means identical.</param>
  [TestCase("cjxl_16bit_lossless", 0)]
  [TestCase("cjxl_16bit_lossy", 2)]
  public void ADeepFileDecodesToWhatLibjxlDecodesItTo(string name, int allowed) {
    var file = JpegXlReader.FromBytes(_Fixture(name + ".jxl"));
    var (width, height, expected) = _ReadPpm16(_Fixture(name + ".ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });
    Assert.That(expected, Has.Length.EqualTo(width * height * 3));

    var worst = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var got = (file.PixelData[i * 2] << 8) | file.PixelData[i * 2 + 1];
      worst = Math.Max(worst, Math.Abs(got - expected[i]));
    }

    Assert.That(worst, Is.LessThanOrEqualTo(allowed),
      $"{name}: a sample is out by {worst} of 65,535, and {allowed} is what is accounted for.");
  }

  private static (int Width, int Height, int[] Samples) _ReadPpm16(byte[] ppm) {
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
    Assert.That(Token(), Is.EqualTo("65535"), "the reference is meant to be a deep one");
    ++at;

    var samples = new int[width * height * 3];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (ppm[at + i * 2] << 8) | ppm[at + i * 2 + 1];
    return (width, height, samples);
  }
}
