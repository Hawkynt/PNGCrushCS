using System;
using System.IO;
using FileFormat.JpegXl;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The square pieces of a shape that divides a block.
/// </summary>
/// <remarks>
/// A whole block is kept the way the scan order fills it, which is the
/// transpose of the way the format writes it down, and the transform is written
/// to match. The shapes that divide a block gather their pieces by the numbers
/// the format states, so those pieces come out the written way round and have
/// to be turned back before the transform sees them.
///
/// <para>It shows only on the square pieces. A four-by-eight strip is stored
/// with its short side first whichever way round it is read, so turning it is
/// the same as not turning it; a four-by-four cannot hide the difference. The
/// two places it matters are the quarters of a 4x4 block and the square quarter
/// of a corner block.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlSquareSubBlockTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A 100x100 lossy file cjxl 0.12.0 wrote, with `djxl`'s own decode beside
  /// it. It uses the corner shape, whose square quarter was being transformed
  /// the wrong way round: one block of it was seven levels out where the rest
  /// of the picture was within one.
  /// </summary>
  [Test]
  public void APictureUsingTheCornerShapeMatchesLibjxlToWithinALevel() {
    var decoded = JpegXlReader.TryReadSpecRgb24(
      _Fixture("cjxl_afv_corner.jxl"), out var width, out var height, out var rgb);
    Assert.That(decoded, Is.True);

    var (refWidth, refHeight, expected) = _ReadPpm(_Fixture("cjxl_afv_corner.ppm"));
    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(refWidth));
      Assert.That(height, Is.EqualTo(refHeight));
      Assert.That(rgb, Is.Not.Null.And.Length.EqualTo(expected.Length));
    });

    var worst = 0;
    var worstAt = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = Math.Abs(rgb![i] - expected[i]);
      if (delta <= worst)
        continue;

      worst = delta;
      worstAt = i;
    }

    // One level is where an independent decoder stops: the two float pipelines
    // agree to about two ten-thousandths of a level, so they part only where a
    // value sits that close to a rounding boundary.
    Assert.That(worst, Is.LessThanOrEqualTo(1),
      $"sample {worstAt} (x={worstAt / 3 % width}, y={worstAt / 3 / width}) is "
      + $"{rgb![worstAt]} where libjxl has {expected[worstAt]}");
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
