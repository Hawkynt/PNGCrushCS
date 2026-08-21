using System;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>
/// The three ways a HuffYUV row turns coded differences back into samples.
/// </summary>
/// <remarks>
/// All three are the same running sum along a row, differing only in what is added to it. Left
/// prediction is the sum alone. Gradient prediction is the sum plus the row above, sample for
/// sample — which is the same thing as predicting left plus above minus above-left, because the sum
/// telescopes. Median prediction cannot be written that way and is done a sample at a time.
/// <para/>
/// Everything is a byte and wraps: a difference of 200 added to 100 is 44 and not 255. Saturating
/// would lose the codec's losslessness at the first sample either side of the range.
/// </remarks>
internal static class HuffYuvPrediction {

  /// <summary>
  /// Turns a row of differences into samples by running them up from a starting value.
  /// </summary>
  /// <returns>The last sample, which is the starting value for the row after it.</returns>
  /// <remarks>
  /// The starting value carries from row to row rather than resetting, which is why the first sample
  /// of a row is predicted from the last sample of the one before it and not from nothing.
  /// </remarks>
  internal static byte AddLeft(Span<byte> row, ReadOnlySpan<byte> differences, int count, byte left) {
    for (var i = 0; i < count; ++i)
      row[i] = left = (byte)(left + differences[i]);

    return left;
  }

  /// <summary>Adds the row above to a row of running sums, which makes the sum a gradient.</summary>
  internal static void AddAbove(Span<byte> row, ReadOnlySpan<byte> above, int count) {
    for (var i = 0; i < count; ++i)
      row[i] += above[i];
  }

  /// <summary>
  /// Turns a row of differences into samples against the row above, predicting each from the median
  /// of its left, its top and the plane through both.
  /// </summary>
  internal static void AddMedian(
    Span<byte> row, ReadOnlySpan<byte> above, ReadOnlySpan<byte> differences, int count, ref byte left, ref byte leftAbove) {
    var l = left;
    var lt = leftAbove;

    for (var i = 0; i < count; ++i) {
      var t = above[i];
      var predicted = _Median(l, t, (byte)(l + t - lt));
      l = (byte)(predicted + differences[i]);
      lt = t;
      row[i] = l;
    }

    left = l;
    leftAbove = lt;
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }
}
