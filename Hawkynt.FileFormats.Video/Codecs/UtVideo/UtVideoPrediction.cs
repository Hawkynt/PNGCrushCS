using System;

namespace FileFormat.Codecs.UtVideo;

/// <summary>
/// The ways a Ut Video slice turns coded differences back into samples.
/// </summary>
/// <remarks>
/// The three predictors are the codec author's own: the sample to the left; the gradient, which is
/// left plus above less above-left; and the median, which is the median of those three. The last is
/// the LOCO-I predictor that JPEG-LS and FFV1 also use.
/// <para/>
/// <b>They run over a slice linearly, not row by row.</b> The sample to the left of column zero is
/// the last sample of the row above, and the one above-left of it is the last sample of the row
/// above that. Nothing is reset at the end of a row — only at the start of a slice, which is what
/// makes a slice independently decodable and is the whole point of having them.
/// <para/>
/// Everything is a byte and wraps: a difference of 200 added to 100 is 44 and not 255. Saturating
/// would lose the codec's losslessness at the first sample either side of the range.
/// </remarks>
internal static class UtVideoPrediction {

  /// <summary>
  /// What a slice's first sample is predicted from, before any sample of it has been decoded.
  /// </summary>
  /// <remarks>
  /// Not zero, which is the value a reader assumes and which is wrong by exactly 128 on every
  /// sample of every plane — a whole-picture offset rather than a local error, because left
  /// prediction is a running sum and the starting value never leaves it. Measured against ffmpeg's
  /// decode of the same frames, where reading it as zero puts the maximum difference at 128 and
  /// reading it as 128 puts it at nought.
  /// </remarks>
  internal const byte SLICE_START = 0x80;

  /// <summary>
  /// Turns a run of differences into samples by running them up from a starting value.
  /// </summary>
  /// <returns>The last sample, which is the starting value for whatever follows it.</returns>
  internal static byte AddLeft(Span<byte> samples, byte left) {
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = left = (byte)(left + samples[i]);

    return left;
  }

  /// <summary>
  /// Turns the rows after a slice's first into samples, predicting each from its neighbours.
  /// </summary>
  /// <remarks>
  /// <paramref name="plane"/> holds the whole plane, with the differences already in place from
  /// <paramref name="start"/> onwards and the rows before it already decoded.
  /// <para/>
  /// <b>The two predictors part company at column zero, and only there.</b> The median runs on
  /// linearly: the sample to its left is the last sample of the row above and the one above-left of
  /// it the last sample of the row above that. The gradient does not — it starts every row from the
  /// sample above it alone, which is what its own formula reduces to when the left and the
  /// above-left are the same thing.
  /// <para/>
  /// Neither is written down anywhere. The median's rule was measured on ffmpeg's own files: reading
  /// it as "the sample above" instead reproduces most rows of a picture and then gets one wrong,
  /// because the linear rule usually chooses the sample above anyway and only differs where the
  /// neighbours disagree — four rows out of forty-eight in the frame it was found on. The gradient's
  /// rule had to be measured the other way round, by coding streams here and having ffmpeg decode
  /// them, because no encoder reachable here writes a gradient frame.
  /// </remarks>
  internal static void AddPredicted(
    Span<byte> plane, int width, int start, int end, bool median) {
    if (start >= end)
      return;

    // The first sample of the row after a slice's first row has a sample above it and nothing to
    // its left, so both predictors take the one above.
    var left = (byte)(plane[start - width] + plane[start]);
    plane[start] = left;

    for (var i = start + 1; i < end; ++i) {
      var above = plane[i - width];
      byte predicted;

      if (!median && i % width == 0) {
        predicted = above;
      } else {
        var aboveLeft = plane[i - width - 1];
        var gradient = (byte)(left + above - aboveLeft);
        predicted = median ? _Median(left, above, gradient) : gradient;
      }

      left = (byte)(predicted + plane[i]);
      plane[i] = left;
    }
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }
}
