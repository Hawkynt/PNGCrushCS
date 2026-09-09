using System;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LTransformTests {

  #region SubtractGreen

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_AddsGreenToRedAndBlue() {
    var pixels = new uint[] { 0xFF004020u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    var g = (byte)((0xFF004020u >> 8) & 0xFF);
    var expectedR = (byte)(((0xFF004020u >> 16) & 0xFF) + g);
    var expectedB = (byte)((0xFF004020u & 0xFF) + g);
    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo(expectedR));
    Assert.That((byte)(pixels[0] & 0xFF), Is.EqualTo(expectedB));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_PreservesAlphaAndGreen() {
    var pixels = new uint[] { 0xAB00CD00u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    Assert.That((byte)((pixels[0] >> 24) & 0xFF), Is.EqualTo(0xAB));
    Assert.That((byte)((pixels[0] >> 8) & 0xFF), Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_ZeroGreen_NoChange() {
    var original = 0xFF800060u;
    var pixels = new uint[] { original };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    Assert.That(pixels[0], Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_WrapsAround() {
    var pixels = new uint[] { 0xFFFEFF01u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 1, 1);

    var g = 0xFF;
    var r = (0xFE + g) & 0xFF;
    var b = (0x01 + g) & 0xFF;
    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo((byte)r));
    Assert.That((byte)(pixels[0] & 0xFF), Is.EqualTo((byte)b));
  }

  [Test]
  [Category("Unit")]
  public void SubtractGreen_InverseTransform_MultiplePixels() {
    var pixels = new uint[] { 0xFF001000u, 0xFF002000u };
    var transform = new Vp8LSubtractGreenTransform();

    transform.InverseTransform(pixels, 2, 1);

    Assert.That((byte)((pixels[0] >> 16) & 0xFF), Is.EqualTo(0x10));
    Assert.That((byte)((pixels[1] >> 16) & 0xFF), Is.EqualTo(0x20));
  }

  #endregion

  #region ColorIndexing

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_SmallPalette_PacksPixels() {
    var palette = new uint[] { 0xFF000000u, 0xFFFFFFFFu };
    var ci = new Vp8LColorIndexingTransform(palette, 8);

    Assert.That(ci.EncodedWidth, Is.EqualTo(1));
    Assert.That(ci.OriginalWidth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_4ColorPalette() {
    var palette = new uint[] { 0xFF000000u, 0xFF333333u, 0xFF666666u, 0xFF999999u };
    var ci = new Vp8LColorIndexingTransform(palette, 16);

    Assert.That(ci.EncodedWidth, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_EncodedWidth_LargePalette_NoPacking() {
    var palette = new uint[256];
    for (var i = 0; i < 256; ++i)
      palette[i] = 0xFF000000u | ((uint)i << 16) | ((uint)i << 8) | (uint)i;
    var ci = new Vp8LColorIndexingTransform(palette, 100);

    Assert.That(ci.EncodedWidth, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ColorIndexing_InverseTransform_LookupsByGreenChannel() {
    // Use a palette > 16 entries so bitsPerPixel=8 and the simple lookup branch is taken
    var palette = new uint[17];
    palette[0] = 0xFFFF0000u;
    palette[1] = 0xFF00FF00u;
    palette[2] = 0xFF0000FFu;
    for (var i = 3; i < 17; ++i)
      palette[i] = 0xFF000000u;

    // Green channel holds the palette index
    var pixels = new uint[3];
    pixels[0] = 0x00000000u; // green=0 -> palette[0]=red
    pixels[1] = 0x00000100u; // green=1 -> palette[1]=green
    pixels[2] = 0x00000200u; // green=2 -> palette[2]=blue

    var ci = new Vp8LColorIndexingTransform(palette, 3);
    ci.InverseTransform(pixels, 3, 1);

    Assert.That(pixels[0], Is.EqualTo(0xFFFF0000u));
    Assert.That(pixels[1], Is.EqualTo(0xFF00FF00u));
    Assert.That(pixels[2], Is.EqualTo(0xFF0000FFu));
  }

  #endregion

  #region Predictor neighbour definitions

  /// <summary>
  /// Runs every predictor over a whole picture and checks it against the neighbour rules spelled
  /// out independently below.
  /// </summary>
  /// <remarks>
  /// The shapes deliberately include a single column, a single row and pictures only a few rows
  /// tall, because the edge rules are where the neighbour definitions differ from the obvious
  /// reading: the very first pixel predicts opaque black, the rest of the first row predicts from
  /// the left, the first pixel of each later row predicts from above, and — the one that is easy to
  /// get wrong — "top right" in the last column means the first pixel of the row being built, not
  /// the pixel above. The same streams these rules were derived from were decoded by
  /// <c>dwebp</c> and matched sample for sample across all fourteen predictors.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Predictor_EveryMode_MatchesTheNeighbourRules(
    [Range(0, 13)] int mode,
    [Values(1, 2, 5, 17, 24)] int width,
    [Values(1, 3, 20)] int height) {

    var residuals = new uint[width * height];
    var seed = 0x9E3779B9u;
    for (var i = 0; i < residuals.Length; ++i) {
      seed = seed * 1664525u + 1013904223u;
      residuals[i] = seed;
    }

    // One tile covering everything, carrying the mode in its green channel.
    var transform = new Vp8LPredictorTransform([0xFF000000u | ((uint)mode << 8)], 9);
    var actual = (uint[])residuals.Clone();
    transform.InverseTransform(actual, width, height);

    Assert.That(actual, Is.EqualTo(_ReferenceInverse(residuals, width, height, mode)));
  }

  private static uint[] _ReferenceInverse(uint[] residuals, int width, int height, int mode) {
    var output = new uint[residuals.Length];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var index = y * width + x;
        uint predicted;
        if (x == 0 && y == 0)
          predicted = 0xFF000000u;
        else if (y == 0)
          predicted = output[index - 1];
        else if (x == 0)
          predicted = output[index - width];
        else {
          var left = output[index - 1];
          var top = output[index - width];
          var topLeft = output[index - width - 1];
          var topRight = output[index - width + 1];
          predicted = _ReferencePredict(mode, left, top, topRight, topLeft);
        }

        output[index] = _Add(residuals[index], predicted);
      }

    return output;
  }

  private static uint _ReferencePredict(int mode, uint left, uint top, uint topRight, uint topLeft) => mode switch {
    0 => 0xFF000000u,
    1 => left,
    2 => top,
    3 => topRight,
    4 => topLeft,
    5 => _Avg(_Avg(left, topRight), top),
    6 => _Avg(left, topLeft),
    7 => _Avg(left, top),
    8 => _Avg(topLeft, top),
    9 => _Avg(top, topRight),
    10 => _Avg(_Avg(left, topLeft), _Avg(top, topRight)),
    11 => _Distance(left, topLeft) <= _Distance(top, topLeft) ? top : left,
    12 => _PerChannel(left, top, topLeft, static (l, t, tl) => _Clamp255(l + t - tl)),
    13 => _PerChannel(_Avg(left, top), topLeft, 0, static (a, tl, _) => _Clamp255(a + (a - tl) / 2)),
    _ => 0xFF000000u,
  };

  private static uint _Add(uint a, uint b) => _PerChannel(a, b, 0, static (p, q, _) => (uint)((p + q) & 0xFF));

  private static uint _Avg(uint a, uint b) => _PerChannel(a, b, 0, static (p, q, _) => (uint)((p + q) >> 1));

  private static int _Distance(uint a, uint b) {
    var total = 0;
    for (var shift = 0; shift < 32; shift += 8)
      total += Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF));
    return total;
  }

  private static uint _Clamp255(int value) => (uint)(value < 0 ? 0 : value > 255 ? 255 : value);

  private static uint _PerChannel(uint a, uint b, uint c, Func<int, int, int, uint> combine) {
    var result = 0u;
    for (var shift = 0; shift < 32; shift += 8)
      result |= (combine((int)((a >> shift) & 0xFF), (int)((b >> shift) & 0xFF), (int)((c >> shift) & 0xFF)) & 0xFF) << shift;
    return result;
  }

  /// <summary>
  /// Distance codes 1..120 name a position near the current pixel. The offsets have to come out of
  /// the packed table rather than out of any pattern it appears to follow.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void DistanceCodes_UnpackToTheDocumentedNeighbourhood() {
    var seen = new System.Collections.Generic.HashSet<(int, int)>();

    for (var code = 1; code <= Vp8LDecoder.CodeToPlaneCount; ++code) {
      var (dx, dy) = Vp8LDecoder.CodeToOffset(code);
      Assert.That(dy, Is.InRange(0, 7), $"code {code} names an impossible row offset");
      Assert.That(dx, Is.InRange(-7, 8), $"code {code} names an impossible column offset");
      Assert.That(dy != 0 || dx > 0, Is.True, $"code {code} names a pixel that is not yet decoded");
      Assert.That(seen.Add((dx, dy)), Is.True, $"code {code} repeats an offset an earlier code already names");
    }

    Assert.That(seen, Has.Count.EqualTo(Vp8LDecoder.CodeToPlaneCount));

    // Spot checks at both ends and either side of the point where a symmetric guess diverges.
    Assert.Multiple(() => {
      Assert.That(Vp8LDecoder.CodeToOffset(1), Is.EqualTo((0, 1)));
      Assert.That(Vp8LDecoder.CodeToOffset(2), Is.EqualTo((1, 0)));
      Assert.That(Vp8LDecoder.CodeToOffset(97), Is.EqualTo((8, 0)));
      Assert.That(Vp8LDecoder.CodeToOffset(106), Is.EqualTo((8, 3)));
      Assert.That(Vp8LDecoder.CodeToOffset(120), Is.EqualTo((8, 7)));
    });
  }

  #endregion
}
