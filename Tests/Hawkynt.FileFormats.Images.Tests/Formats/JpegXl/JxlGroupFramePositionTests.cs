using System;
using System.IO;
using FileFormat.JpegXl;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What a group takes from the frame around it, rather than from its own
/// corner.
/// </summary>
/// <remarks>
/// A frame wider or taller than one group is decoded a group at a time, but
/// several of the things a group needs belong to the whole picture: the
/// quantisation step each block states, and the low frequencies the picture is
/// smoothed over. Reading either of those at the block's position within its
/// own group gives the right answer for the group that starts at the corner of
/// the picture and the wrong one for every other group — which is invisible in
/// any picture small enough to be a single group, and that is most test
/// pictures.
/// </remarks>
[TestFixture]
internal sealed class JxlGroupFramePositionTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A 260x48 file cjxl 0.12.0 wrote, which is two groups across. Its second
  /// group is the rightmost four pixels; with the quantisation step read from
  /// the wrong place those columns came back with the wrong contrast, and the
  /// worst pixel in the picture was 56 levels out against libjxl rather than 6.
  /// </summary>
  [Test]
  public void TheSecondGroupOfAFrameDecodesLikeThePictureAroundIt() {
    var decoded = JpegXlReader.TryReadSpecRgb24(
      _Fixture("cjxl_two_groups_coefficients.jxl"), out var width, out var height, out var rgb);
    Assert.That(decoded, Is.True);

    var (refWidth, refHeight, expected) = _ReadPpm(_Fixture("cjxl_two_groups_coefficients.ppm"));
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

    // With the quantisation step taken from the block's place in its own group
    // rather than in the picture, the second group came back with the wrong
    // contrast and the worst sample here was 56 levels out.
    Assert.That(worst, Is.LessThanOrEqualTo(12),
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
