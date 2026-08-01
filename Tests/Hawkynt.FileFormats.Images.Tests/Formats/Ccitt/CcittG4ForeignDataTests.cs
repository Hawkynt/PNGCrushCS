using System;
using System.Text;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

/// <summary>
/// Decodes Group 4 streams produced elsewhere, and checks that what this encoder produces says the
/// same thing back.
/// </summary>
/// <remarks>
/// The round-trip tests next door could not see what was wrong here, because both halves were wrong
/// the same way: each looked for the first pixel of the opposite colour where T.6 means the first
/// *change* to it, which is a different position on any line with more than one run. Encoding and
/// decoding against each other therefore agreed, while libtiff's verdict on the same output was
/// "Line length mismatch at line 12 (got 46, expected 40)".
///
/// The bytes below come from ImageMagick, so they are an outside opinion rather than this library's
/// own.
/// </remarks>
[TestFixture]
public sealed class CcittG4ForeignDataTests {

  /// <summary>
  /// The complete Group 4 payload of a 40x24 CALS file written by ImageMagick, whose left half is
  /// one colour and right half the other.
  /// </summary>
  private static readonly byte[] _HalfAndHalf40X24 =
    [0x22, 0x03, 0x47, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xE0, 0x02, 0x00, 0x20];

  [Test]
  [Category("Unit")]
  public void Decode_ForeignStream_SplitsTheLineWhereTheEncoderPutIt() {
    var pixels = CcittG4Decoder.Decode(_HalfAndHalf40X24, 40, 24);

    Assert.That(pixels, Has.Length.EqualTo(5 * 24), "ceil(40/8) bytes a row");
    for (var y = 0; y < 24; ++y)
      for (var x = 0; x < 40; ++x)
        Assert.That(_IsSet(pixels, 40, x, y), Is.EqualTo(x >= 20), $"pixel {x},{y}");
  }

  /// <summary>
  /// The whole point of the reference line: every row after the first is coded against the one above
  /// it, so a decoder that mislocates b1 goes wrong from row 2 onwards even when row 1 comes out
  /// right.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Decode_ForeignStream_KeepsTheSecondRowInStepWithTheFirst() {
    var pixels = CcittG4Decoder.Decode(_HalfAndHalf40X24, 40, 24);

    var first = _RowAsText(pixels, 40, 0);
    for (var y = 1; y < 24; ++y)
      Assert.That(_RowAsText(pixels, 40, y), Is.EqualTo(first), $"row {y}");
  }

  /// <summary>
  /// Encodes shapes that force every mode — long vertical edges for vertical mode, a run that
  /// outlives two changes above it for pass mode, and speckle too irregular for either.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCase(40, 24)]
  [TestCase(64, 40)]
  [TestCase(101, 37)] // a width that is not a whole number of bytes
  public void EncodeThenDecode_ExercisingEveryMode_ReturnsTheSamePixels(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var original = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        // A wedge whose edge moves one pixel a row (vertical mode), a bar that swallows it for a few
        // rows (pass mode), and a scattering of single pixels (horizontal mode).
        var wedge = x > y && x < y + 12;
        var bar = y % 11 == 0;
        var speckle = ((x * 7) + (y * 13)) % 23 == 0;
        if (wedge ^ bar ^ speckle)
          original[(y * bytesPerRow) + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
      }

    var decoded = CcittG4Decoder.Decode(CcittG4Encoder.Encode(original, width, height), width, height);

    for (var y = 0; y < height; ++y)
      Assert.That(_RowAsText(decoded, width, y), Is.EqualTo(_RowAsText(original, width, y)), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_RunsLongerThanOneMakeUpCode_ReturnTheSamePixels() {
    // 3000 pixels of black is past 2560, the longest run any single make-up code can name.
    const int width = 3000;
    var original = new byte[(width + 7) / 8];
    Array.Fill(original, (byte)0xFF);

    var decoded = CcittG4Decoder.Decode(CcittG4Encoder.Encode(original, width, 1), width, 1);

    Assert.That(_RowAsText(decoded, width, 0), Is.EqualTo(_RowAsText(original, width, 0)));
  }

  private static bool _IsSet(byte[] pixels, int width, int x, int y)
    => ((pixels[(y * ((width + 7) / 8)) + (x >> 3)] >> (7 - (x & 7))) & 1) != 0;

  private static string _RowAsText(byte[] pixels, int width, int y) {
    var sb = new StringBuilder(width);
    for (var x = 0; x < width; ++x)
      sb.Append(_IsSet(pixels, width, x, y) ? '#' : '.');

    return sb.ToString();
  }
}
