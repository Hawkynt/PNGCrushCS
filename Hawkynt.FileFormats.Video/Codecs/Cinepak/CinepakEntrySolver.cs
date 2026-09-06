using System;

namespace FileFormat.Codecs.Cinepak;

/// <summary>
/// Turns four wanted colours into the one codebook entry that comes closest to painting them.
/// </summary>
/// <remarks>
/// A codebook entry is four luminances and one chrominance pair, and what it paints is four colours —
/// the chrominance shifts all three channels of all four by the same amounts and each luminance moves
/// its own colour along the diagonal. So designing an entry is choosing a shift and then, per sample,
/// a point on that diagonal, and the second half of that has a closed form once the first is fixed.
/// <para/>
/// <b>Not the forward transform, rounded.</b> Converting a colour with
/// <see cref="CinepakColorConversion.ChromaOf"/> and the luminance row beside it gets it back a level
/// or two out on any channel that saturates — a flat red comes back 254 rather than 255 — because the
/// way out clamps and the way in does not know that. The forward transform is used here only to say
/// where to start looking; every candidate is then scored by running the decoder's own arithmetic and
/// comparing the result to what was wanted.
/// <para/>
/// <b>What "exactly" can mean.</b> Only 2669700 of the 16777216 RGB colours are stateable at all: the
/// red and blue rows of the inverse matrix are doublings, so a colour whose red and blue differ in
/// parity needs one of them to saturate, and the green row has to land on its target as well. All 256
/// greys and all eight corners of the colour cube are among the ones that work, and the search below
/// finds them.
/// </remarks>
internal static class CinepakEntrySolver {

  /// <summary>How many colours one codebook entry paints.</summary>
  internal const int SampleCount = 4;

  /// <summary>Bytes a solved entry occupies: four luminances and two chrominances.</summary>
  internal const int EntryLength = 6;

  /// <summary>Twelve, being <see cref="SampleCount"/> colours of red, green and blue.</summary>
  internal const int TargetLength = SampleCount * 3;

  /// <summary>
  /// How far either side of the transformed chrominance the ordinary search looks.
  /// </summary>
  /// <remarks>
  /// Four levels each way, which is 81 shifts tried per entry. Measured rather than reasoned: the
  /// transform's rounding is worth at most a level or two, and saturation moves the best chrominance a
  /// little further than that — the eight corners of the colour cube all land within three.
  /// </remarks>
  private const int _CHROMA_WINDOW = 4;

  /// <summary>
  /// Solves one entry for four wanted colours, and says what it costs in squared error.
  /// </summary>
  /// <param name="targets">Four colours as red, green and blue, each 0 to 255.</param>
  /// <param name="entry">Six bytes written as four luminances then the two chrominances.</param>
  internal static int Solve(ReadOnlySpan<int> targets, Span<byte> entry) {
    var (centreBlue, centreRed) = CinepakColorConversion.ChromaOf(
      targets[0] + targets[3] + targets[6] + targets[9],
      targets[1] + targets[4] + targets[7] + targets[10],
      targets[2] + targets[5] + targets[8] + targets[11]);

    Span<byte> luminance = stackalloc byte[SampleCount];
    var bestBlue = centreBlue;
    var bestRed = centreRed;
    var best = int.MaxValue;

    for (var blueDifference = centreBlue - _CHROMA_WINDOW; blueDifference <= centreBlue + _CHROMA_WINDOW; ++blueDifference) {
      if (blueDifference is < -128 or > 127)
        continue;

      for (var redDifference = centreRed - _CHROMA_WINDOW; redDifference <= centreRed + _CHROMA_WINDOW; ++redDifference) {
        if (redDifference is < -128 or > 127)
          continue;

        var error = _Fit(targets, blueDifference, redDifference, luminance);
        if (error >= best)
          continue;

        best = error;
        bestBlue = blueDifference;
        bestRed = redDifference;
        luminance.CopyTo(entry);
      }
    }

    // A window around the transform finds the best shift for a colour it can state, but a flat entry
    // is the one case where "the best" and "exact" are worth telling apart: it is what a flat picture
    // is made of, and where an exact answer exists it can be anywhere in the plane. That search is
    // exhaustive and only reached when the cheap one left something on the table.
    if (best > 0 && _IsFlat(targets) && _SolveFlatExactly(targets, out var flatLuminance, out var flatBlue, out var flatRed)) {
      entry[..SampleCount].Fill(flatLuminance);
      bestBlue = flatBlue;
      bestRed = flatRed;
      best = 0;
    }

    entry[4] = (byte)(sbyte)bestBlue;
    entry[5] = (byte)(sbyte)bestRed;
    return best;
  }

  /// <summary>
  /// The best luminances for one chrominance shift, and what they leave behind.
  /// </summary>
  private static int _Fit(ReadOnlySpan<int> targets, int blueDifference, int redDifference, Span<byte> luminance) {
    var red = redDifference * 2;
    var green = -(blueDifference / 2) - redDifference;
    var blue = blueDifference * 2;

    var total = 0;
    for (var sample = 0; sample < SampleCount; ++sample) {
      total += _BestLuminance(
        targets[sample * 3], targets[sample * 3 + 1], targets[sample * 3 + 2], red, green, blue, out var chosen);
      luminance[sample] = (byte)chosen;
    }

    return total;
  }

  /// <summary>
  /// The luminance that costs least for one colour under a fixed chrominance shift.
  /// </summary>
  /// <remarks>
  /// The cost is a quadratic in the luminance on every stretch where no channel is against a stop, and
  /// a shorter quadratic in the channels still free on every stretch where some are. So the least is at
  /// one of the stops, at either end of the range, or at the mean of the channels a stretch leaves free
  /// — and that is a list of at most twenty-two numbers rather than a scan of all 256. Trying them all
  /// is exact, which matters: a search that merely got close would make an exactly stateable colour
  /// come back wrong, and nothing downstream could tell that from the format's own loss.
  /// </remarks>
  private static int _BestLuminance(
    int targetRed, int targetGreen, int targetBlue, int red, int green, int blue, out int chosen) {
    var wantedRed = targetRed - red;
    var wantedGreen = targetGreen - green;
    var wantedBlue = targetBlue - blue;

    Span<int> candidates = stackalloc int[22];
    var count = 0;
    candidates[count++] = 0;
    candidates[count++] = 255;
    candidates[count++] = -red;
    candidates[count++] = 255 - red;
    candidates[count++] = -green;
    candidates[count++] = 255 - green;
    candidates[count++] = -blue;
    candidates[count++] = 255 - blue;
    count = _AddMean(candidates, count, wantedRed, 1);
    count = _AddMean(candidates, count, wantedGreen, 1);
    count = _AddMean(candidates, count, wantedBlue, 1);
    count = _AddMean(candidates, count, wantedRed + wantedGreen, 2);
    count = _AddMean(candidates, count, wantedRed + wantedBlue, 2);
    count = _AddMean(candidates, count, wantedGreen + wantedBlue, 2);
    count = _AddMean(candidates, count, wantedRed + wantedGreen + wantedBlue, 3);

    chosen = 0;
    var best = int.MaxValue;
    for (var i = 0; i < count; ++i) {
      var luminance = candidates[i] < 0 ? 0 : candidates[i] > 255 ? 255 : candidates[i];
      var error = _Cost(luminance + red, targetRed) + _Cost(luminance + green, targetGreen) + _Cost(luminance + blue, targetBlue);
      if (error >= best)
        continue;

      best = error;
      chosen = luminance;
    }

    return best;
  }

  /// <summary>Both whole numbers either side of a mean, since the luminance has to be one of them.</summary>
  private static int _AddMean(Span<int> candidates, int count, int sum, int divisor) {
    var floor = sum >= 0 ? sum / divisor : -((-sum + divisor - 1) / divisor);
    candidates[count++] = floor;
    candidates[count++] = floor + 1;
    return count;
  }

  private static int _Cost(int painted, int wanted) {
    var difference = (painted < 0 ? 0 : painted > 255 ? 255 : painted) - wanted;
    return difference * difference;
  }

  private static bool _IsFlat(ReadOnlySpan<int> targets) {
    for (var channel = 3; channel < TargetLength; ++channel)
      if (targets[channel] != targets[channel % 3])
        return false;

    return true;
  }

  /// <summary>
  /// Looks for a luminance and chrominance pair that paints one colour exactly.
  /// </summary>
  /// <remarks>
  /// Not a search of all 256 by 256 by 256 triples. Red and blue each pin their chrominance down to one
  /// value per luminance, or — where the channel is at a stop — to a run of values that all paint the
  /// same stop; and inside that, the green row is one equation in one unknown, which is solved rather
  /// than searched. So the whole thing is a walk over the luminances with a little arithmetic each,
  /// and it is exhaustive: if it finds nothing then no entry states this colour.
  /// </remarks>
  private static bool _SolveFlatExactly(ReadOnlySpan<int> targets, out byte luminance, out int blueDifference, out int redDifference) {
    var wantedRed = targets[0];
    var wantedGreen = targets[1];
    var wantedBlue = targets[2];

    for (var y = 0; y <= 255; ++y) {
      if (!_DifferenceRange(wantedRed, y, out var redLow, out var redHigh))
        continue;
      if (!_DifferenceRange(wantedBlue, y, out var blueLow, out var blueHigh))
        continue;

      for (var candidate = blueLow; candidate <= blueHigh; ++candidate) {
        var above = y - candidate / 2;

        // The green row reads value = above - redDifference, clamped. Where the target is neither stop
        // that names one difference; where it is, it names the half of the range that reaches it, and
        // the nearest end of that half is as good as any other member of it.
        var wanted = wantedGreen switch {
          0 => Math.Max(redLow, above),
          255 => Math.Min(redHigh, above - 255),
          _ => above - wantedGreen,
        };

        if (wanted < redLow || wanted > redHigh)
          continue;

        var painted = above - wanted;
        if ((painted < 0 ? 0 : painted > 255 ? 255 : painted) != wantedGreen)
          continue;

        luminance = (byte)y;
        blueDifference = candidate;
        redDifference = wanted;
        return true;
      }
    }

    luminance = 0;
    blueDifference = 0;
    redDifference = 0;
    return false;
  }

  /// <summary>
  /// Which chrominance differences paint one channel exactly, given a luminance.
  /// </summary>
  /// <remarks>
  /// The channel is <c>clamp(luminance + 2 * difference)</c>. A target between the stops needs the
  /// doubling to land on it, so the difference is fixed and only exists when the gap is even; a target
  /// of nought or 255 needs only to be pushed past the stop, so any difference far enough out will do
  /// and the range is open at that end.
  /// </remarks>
  private static bool _DifferenceRange(int wanted, int luminance, out int low, out int high) {
    switch (wanted) {
      case 0:
        low = -128;
        high = Math.Min(127, -((luminance + 1) / 2));
        return high >= low;
      case 255:
        low = Math.Max(-128, (256 - luminance) / 2);
        high = 127;
        return high >= low;
      default:
        var gap = wanted - luminance;
        low = high = gap / 2;
        return (gap & 1) == 0 && low is >= -128 and <= 127;
    }
  }
}
