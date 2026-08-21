using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>The three ways a MagicYUV slice predicts a sample, as its frames number them.</summary>
internal enum MagicYuvPredictor {

  /// <summary>The sample to the left.</summary>
  Left = 1,

  /// <summary>Left plus above less above-left.</summary>
  Gradient = 2,

  /// <summary>The median of the left, the above, and the gradient of the two.</summary>
  Median = 3,
}

/// <summary>
/// Turns a MagicYUV slice's coded differences back into samples.
/// </summary>
/// <remarks>
/// <b>Every row starts again from the sample above it</b>, not from the end of the row before. That
/// is the same at column zero for all three predictors, and it is what separates this codec from
/// HuffYUV and Ut Video, whose running sums carry on across the end of a row. Reading it their way
/// decodes the first row of a plane exactly and then puts every row after it out — which is how it
/// was found, on a plane that agreed for exactly its first 64 samples and disagreed from the 65th.
/// <para/>
/// The first row of a slice has nothing above it, so its first sample is predicted from nought and
/// the rest of it from the left, whichever of the three the slice names. That is what makes a slice
/// independently decodable, which is the point of having them.
/// <para/>
/// Everything is a byte and wraps: a difference of 200 added to 100 is 44 and not 255. Saturating
/// would lose the codec's losslessness at the first sample either side of the range.
/// </remarks>
internal static class MagicYuvPrediction {

  /// <summary>
  /// Turns the differences in rows <paramref name="firstRow"/> to <paramref name="lastRow"/> into
  /// samples.
  /// </summary>
  internal static void Apply(
    Span<byte> plane, int width, int firstRow, int lastRow, MagicYuvPredictor predictor) {
    for (var y = firstRow; y < lastRow; ++y) {
      var row = y * width;
      for (var x = 0; x < width; ++x) {
        var at = row + x;
        byte predicted;

        if (x == 0)
          predicted = y == firstRow ? (byte)0 : plane[at - width];
        else if (y == firstRow)
          predicted = plane[at - 1];
        else {
          var left = plane[at - 1];
          var above = plane[at - width];
          var aboveLeft = plane[at - width - 1];
          var gradient = (byte)(left + above - aboveLeft);
          predicted = predictor switch {
            MagicYuvPredictor.Left => left,
            MagicYuvPredictor.Gradient => gradient,
            _ => _Median(left, above, gradient),
          };
        }

        plane[at] = (byte)(predicted + plane[at]);
      }
    }
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }
}
