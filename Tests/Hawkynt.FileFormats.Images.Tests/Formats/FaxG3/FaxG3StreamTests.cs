using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.FaxG3;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.FaxG3.Tests;

/// <summary>
/// Holds a bare Group 3 stream to T.4's punctuation, which is the only thing in it that says how
/// tall the page is.
/// </summary>
/// <remarks>
/// A raw fax file has no header. Nothing states the width and nothing states the height, so a
/// decoder learns the height by counting rows until the coding ends — and what tells it a row has
/// ended is the end-of-line word. T.4 puts one in front of every row and closes the page with
/// return-to-control, six of them in a row.
/// <para/>
/// This wrote a marker after every row instead of before it, and stopped rather than closing. The
/// two forms look identical in the middle and differ only at the ends, which is exactly where it
/// matters: a decoder that takes the marker as the start of a row finds the first row lying in front
/// of the first marker, and then runs out of file where the last row should have begun. netpbm's
/// <c>g3topbm</c> is such a decoder and returned 199 rows for a 200-row picture, and 7 for 8.
/// <para/>
/// Our own decoder skips a marker before each row and so reads either form, which is why the round
/// trip never noticed.
/// </remarks>
[TestFixture]
public sealed class FaxG3StreamTests {

  /// <summary>The end-of-line word: eleven zero bits and a one.</summary>
  private const int _EOL_BITS = 12;

  /// <summary>How many of them make return-to-control.</summary>
  private const int _RETURN_TO_CONTROL = 6;

  private static byte[] _Written(int width, int height) {
    var stride = (width + 7) / 8;
    var rows = new byte[stride * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      if (((x / 3) + y) % 2 == 0)
        rows[y * stride + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));

    return FaxG3Writer.ToBytes(new FaxG3File { Width = width, Height = height, PixelData = rows });
  }

  /// <summary>Where every end-of-line word starts, counted in bits from the front of the stream.</summary>
  /// <remarks>
  /// Eleven zeros in a row appear nowhere else in the coding — that is what makes the word findable
  /// without knowing where the rows are — so counting zeros is enough to find them all.
  /// </remarks>
  private static List<int> _Markers(byte[] stream) {
    var found = new List<int>();
    var zeros = 0;

    for (var bit = 0; bit < stream.Length * 8; ++bit) {
      var set = ((stream[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
      if (!set) {
        ++zeros;
        continue;
      }

      if (zeros >= _EOL_BITS - 1)
        found.Add(bit - (_EOL_BITS - 1));

      zeros = 0;
    }

    return found;
  }

  [Test]
  [Category("Unit")]
  public void TheStreamOpensWithAMarkerSoTheFirstRowIsBehindOne() {
    var markers = _Markers(_Written(32, 6));

    Assert.That(markers, Is.Not.Empty);
    Assert.That(markers[0], Is.Zero,
      "T.4 puts the word in front of the row, not after it. Without the leading one the first row "
      + "falls outside the punctuation and a decoder that syncs on the word loses it.");
  }

  [Test]
  [Category("Unit")]
  public void TheStreamClosesWithReturnToControl() {
    var markers = _Markers(_Written(32, 6));

    Assert.That(markers, Has.Count.GreaterThanOrEqualTo(_RETURN_TO_CONTROL));

    var last = markers[^_RETURN_TO_CONTROL];
    for (var i = 0; i < _RETURN_TO_CONTROL; ++i)
      Assert.That(markers[^(_RETURN_TO_CONTROL - i)], Is.EqualTo(last + i * _EOL_BITS),
        "Return-to-control is six of the words back to back, and it is how a page with no header "
        + "says it has ended rather than been cut off.");
  }

  [Test]
  [Category("Unit")]
  public void EveryRowIsPunctuated() {
    const int height = 6;

    // One in front of each row, one more in front of the first, and the last row's own word counts
    // as the first of the six that close the page.
    Assert.That(_Markers(_Written(32, height)), Has.Count.EqualTo(1 + height + _RETURN_TO_CONTROL - 1),
      "One word in front of each row, and six to close.");
  }

  // ============================================================================================
  // What something that is not this package makes of it
  // ============================================================================================

  [TestCase(320, 200)]
  [TestCase(8, 8)]
  [Category("Conformance")]
  public void NetpbmReadsBackEveryRow(int width, int height) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".g3");
    File.WriteAllBytes(path, _Written(width, height));

    try {
      using var decoder = ExternalTool.StartOrIgnore("g3topbm", $"-width={width} \"{path}\"");
      var portableBitmap = decoder.StandardOutput.ReadToEnd();
      var complaints = decoder.StandardError.ReadToEnd();
      decoder.WaitForExit();

      Assert.That(portableBitmap, Does.StartWith("P4"), $"g3topbm would not decode it: {complaints}");

      var stated = portableBitmap.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      Assert.Multiple(() => {
        Assert.That(stated[1], Is.EqualTo(width.ToString()));
        Assert.That(stated[2], Is.EqualTo(height.ToString()),
          $"g3topbm counted the rows in the coding and made it {stated[2]} rather than {height}. "
          + $"It said: {complaints}");
      });
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }
}
