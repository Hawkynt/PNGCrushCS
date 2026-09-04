using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Heif;
using NUnit.Framework;

namespace FileFormat.Heif.Tests;

/// <summary>AVCI still images, checked against libheif's decode of the same file.</summary>
/// <remarks>
/// An AVCI is a HEIF whose picture is an H.264 access unit rather than an H.265
/// one, so it is read by the same container code with a different codec behind
/// it, and <c>.avci</c> is one of that format's names rather than a format of
/// its own.
///
/// <para>The fixture was built from an x264 intra frame wrapped in the boxes the
/// format asks for, and libheif reads it: it reports the brand, the item and its
/// size, and decodes it. Its decode is the reference here. The two disagree only
/// by the rounding of the conversion out of YCbCr, which is why the check is a
/// tolerance on each sample rather than equality — the same allowance the other
/// lossy-codec comparisons in this suite make.</para>
/// </remarks>
[TestFixture]
public sealed class AvciTests {

  /// <summary>What the conversion out of YCbCr may differ by, per sample.</summary>
  private const int _Tolerance = 2;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Heif", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [Test]
  public void AnAvciDecodesToWhatLibheifDecodesItTo() {
    var file = HeifReader.FromBytes(_Fixture("x264_intra.avci"));
    var image = HeifFile.ToRawImage(file);
    var (width, height, expected) = _ReadPpm(_Fixture("x264_intra.ppm"));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;
    Assert.That(rgb.Length, Is.EqualTo(expected.Length));

    var worst = 0;
    var at = -1;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = Math.Abs(rgb[i] - expected[i]);
      if (delta <= worst)
        continue;
      worst = delta;
      at = i;
    }

    Assert.That(worst, Is.LessThanOrEqualTo(_Tolerance),
      $"sample {at} is {(at < 0 ? 0 : rgb[at])} where libheif decodes {(at < 0 ? 0 : expected[at])}");
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
