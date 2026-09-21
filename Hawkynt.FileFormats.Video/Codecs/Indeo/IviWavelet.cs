using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Puts the four wavelet bands of a scalable picture back together into one luminance plane, and
/// splits a plane back into those four bands.
/// </summary>
/// <remarks>
/// A scalable stream codes its luminance as four bands that are the four quadrants of a single
/// wavelet decomposition: a half-size picture and the three detail bands that restore it to full
/// size. A decoder that wanted only the half-size picture could stop after the first band, which is
/// what "scalable" meant; one that wants the whole picture recomposes here.
/// <para/>
/// Which filter recomposes them is the format's choice and not a parameter: Indeo 5 uses the
/// five-three biorthogonal filter, Indeo 4 the Haar. The two produce visibly different pictures from
/// the same coefficients, so the choice belongs with the format rather than with the stream.
/// <para/>
/// Recomposition and the bias are one step. There is no intermediate plane of wavelet output that
/// then gets 128 added to it: the filters below add the bias and clamp as they write each of the four
/// samples they produce at a time, which is what keeps the arithmetic in the range the original
/// decoders used.
/// <para/>
/// <see cref="Decompose"/> is the other direction, and it lives here rather than with the encoder
/// because it is the algebraic inverse of the recomposition immediately above it and has to follow
/// that operator sample for sample. Read on its own it looks nothing like the five-three analysis a
/// textbook would write, and that is the point: the recomposition is not the synthesis half of the
/// textbook pair.
/// </remarks>
internal static class IviWavelet {

  /// <summary>Recomposes the four bands of one plane into eight-bit samples.</summary>
  internal static void Recompose(IviPlane plane, byte[] destination, int destinationPitch, bool useHaar) {
    if (useHaar)
      _RecomposeHaar(plane, destination, destinationPitch);
    else
      _RecomposeFiveThree(plane, destination, destinationPitch);
  }

  private static void _RecomposeHaar(IviPlane plane, byte[] destination, int destinationPitch) {
    var pitch = plane.Bands[0].Pitch;
    var low = plane.Bands[0].Buffer;
    var horizontal = plane.Bands[1].Buffer;
    var vertical = plane.Bands[2].Buffer;
    var diagonal = plane.Bands[3].Buffer;
    var source = 0;
    var target = 0;

    for (var y = 0; y < plane.Height; y += 2) {
      for (int x = 0, index = 0; x < plane.Width; x += 2, ++index) {
        int b0 = low[source + index];
        int b1 = horizontal[source + index];
        int b2 = vertical[source + index];
        int b3 = diagonal[source + index];

        destination[target + x] = _Clamp(((b0 + b1 + b2 + b3 + 2) >> 2) + 128);
        destination[target + x + 1] = _Clamp(((b0 + b1 - b2 - b3 + 2) >> 2) + 128);
        destination[target + destinationPitch + x] = _Clamp(((b0 - b1 + b2 - b3 + 2) >> 2) + 128);
        destination[target + destinationPitch + x + 1] = _Clamp(((b0 - b1 - b2 + b3 + 2) >> 2) + 128);
      }

      target += destinationPitch * 2;
      source += pitch;
    }
  }

  /// <summary>
  /// The five-three biorthogonal recomposition, which produces four samples at a time from a
  /// three-by-three neighbourhood of each band.
  /// </summary>
  /// <remarks>
  /// The filter needs the row above and the row below the pair it is producing. At the top of the
  /// plane the row above is the row itself and at the bottom the row below is likewise, which is what
  /// the pitch being taken to zero on the last row pair and the back pitch starting at zero say; at
  /// the right-hand edge the same is done by stepping every band pointer back one column for the last
  /// pair, so the column to the right of the last is the last. Every value the filter needs twice is
  /// carried over from the previous iteration rather than re-read, which is why so many of them are
  /// named.
  /// <para/>
  /// The horizontal-detail band is filtered differently from the other three, and not in a way any
  /// filter bank would be designed to be. Its running vertical high-pass is added to the two samples
  /// that were supposed to produce it, so the odd output rows see <c>2*b(i-1) - 12*b(i) + b(i+1)</c>
  /// where the diagonal band immediately below is given the plain <c>b(i-1) - 6*b(i) + b(i+1)</c> for
  /// the same job. Worse, the doubled form reaches one of the two odd outputs and not the other, so
  /// the same intermediate value is computed two different ways one column apart. That is what a
  /// transcription slip in register-reusing code looks like, and FFmpeg has it too, at
  /// <c>libavcodec/ivi_dsp.c</c> in <c>ff_ivi_recompose53</c>.
  /// <para/>
  /// It is carried faithfully anyway, because whether it is a defect cannot be settled from here.
  /// Deciding it needs a decoder that is neither this one nor FFmpeg's, and Indeo 5's only other
  /// implementation was Intel's own binary codec. Real scalable content decodes without error either
  /// way and the difference is a mild sharpening, which is what removing a high-pass term looks like
  /// whichever operator was meant - so the obvious measurements do not separate the two. If this is
  /// ever settled, <see cref="Decompose"/> is derived from these equations and has to be re-derived
  /// with them, which is why it sits in this file.
  /// </remarks>
  private static void _RecomposeFiveThree(IviPlane plane, byte[] destination, int destinationPitch) {
    var pitch = plane.Bands[0].Pitch;
    var backPitch = 0;

    var band0 = plane.Bands[0].Buffer;
    var band1 = plane.Bands[1].Buffer;
    var band2 = plane.Bands[2].Buffer;
    var band3 = plane.Bands[3].Buffer;
    int at0 = 0, at1 = 0, at2 = 0, at3 = 0;
    var target = 0;

    for (var y = 0; y < plane.Height; y += 2) {
      if (y + 2 >= plane.Height)
        pitch = 0;

      int b0First = band0[at0];
      int b0Second = band0[at0 + pitch];

      int b1Above = band1[at1 + backPitch];
      int b1Here = band1[at1];
      var b1Filtered = b1Above - b1Here * 6 + band1[at1 + pitch];

      int b2Here = band2[at2];
      var b2Right = b2Here;
      int b2Below = band2[at2 + pitch];
      var b2BelowRight = b2Below;

      int b3Above = band3[at3 + backPitch];
      var b3AboveRight = b3Above;
      int b3Here = band3[at3];
      var b3Right = b3Here;
      var b3Filtered = b3Above - b3Here * 6 + band3[at3 + pitch];
      var b3FilteredRight = b3Filtered;

      for (int x = 0, index = 0; x < plane.Width; x += 2, ++index) {
        if (x + 2 >= plane.Width) {
          --at0;
          --at1;
          --at2;
          --at3;
        }

        var b2Left = b2Here;
        b2Here = b2Right;
        var b2BelowLeft = b2Below;
        b2Below = b2BelowRight;
        var b3AboveLeft = b3Above;
        b3Above = b3AboveRight;
        var b3Left = b3Here;
        b3Here = b3Right;
        var b3FilteredLeft = b3Filtered;
        b3Filtered = b3FilteredRight;

        // The low-pass band, low-pass filtered in both directions.
        var first = b0First;
        var third = b0Second;
        b0First = band0[at0 + index + 1];
        b0Second = band0[at0 + pitch + index + 1];
        var second = first + b0First;

        var p0 = first * 16;
        var p1 = second * 8;
        var p2 = (first + third) * 8;
        var p3 = (second + third + b0Second) * 4;

        // The horizontal detail band: high-pass down, low-pass along.
        var here = b1Here;
        var above = b1Above;
        b1Here = band1[at1 + index + 1];
        b1Above = band1[at1 + backPitch + index + 1];

        var filtered = above - here * 6 + b1Filtered;
        b1Filtered = b1Above - b1Here * 6 + band1[at1 + pitch + index + 1];

        p0 += (here + above) * 8;
        p1 += (here + above + b1Above + b1Here) * 4;
        p2 += filtered * 4;
        p3 += (filtered + b1Filtered) * 2;

        // The vertical detail band: low-pass down, high-pass along.
        b2Right = band2[at2 + index + 1];
        b2BelowRight = band2[at2 + pitch + index + 1];

        var sum = b2Left + b2Here;
        var difference = b2Left - b2Here * 6 + b2Right;

        p0 += sum * 8;
        p1 += difference * 4;
        p2 += (sum + b2BelowLeft + b2Below) * 4;
        p3 += (difference + b2BelowLeft - b2Below * 6 + b2BelowRight) * 2;

        // The diagonal detail band: high-pass in both directions.
        b3Right = band3[at3 + index + 1];
        b3AboveRight = band3[at3 + backPitch + index + 1];

        var upper = b3AboveLeft + b3Left;
        var middle = b3Above + b3Here;
        var lower = b3AboveRight + b3Right;

        b3FilteredRight = b3AboveRight - b3Right * 6 + band3[at3 + pitch + index + 1];

        p0 += (upper + middle) * 4;
        p1 += (upper - middle * 6 + lower) * 2;
        p2 += (b3FilteredLeft + b3Filtered) * 2;
        p3 += b3FilteredLeft - b3Filtered * 6 + b3FilteredRight;

        destination[target + x] = _Clamp((p0 >> 6) + 128);
        destination[target + x + 1] = _Clamp((p1 >> 6) + 128);
        destination[target + destinationPitch + x] = _Clamp((p2 >> 6) + 128);
        destination[target + destinationPitch + x + 1] = _Clamp((p3 >> 6) + 128);
      }

      target += destinationPitch * 2;
      backPitch = -pitch;

      at0 += pitch + 1;
      at1 += pitch + 1;
      at2 += pitch + 1;
      at3 += pitch + 1;
    }
  }

  /// <summary>
  /// Splits one luminance plane into the four bands a scalable picture carries, by inverting
  /// <see cref="_RecomposeFiveThree"/>.
  /// </summary>
  /// <param name="source">The plane, one sample per pixel, already biased by -128 as the bands are.</param>
  /// <param name="width">The plane width, which must be even.</param>
  /// <param name="height">The plane height, which must be even.</param>
  /// <returns>The four bands, each <c>width/2</c> by <c>height/2</c>.</returns>
  /// <remarks>
  /// This is not the textbook five-three analysis, and the difference is the whole point of it
  /// living next to the recomposition rather than in the encoder. The operator it has to invert is
  /// <see cref="_RecomposeFiveThree"/>, and that one is not the synthesis half of the textbook
  /// lifting pair; feeding a textbook analysis into it loses up to 99 of 255 on noise. Everything
  /// below is derived from the recomposition's own equations.
  /// <para/>
  /// So "correct" here means "the inverse of the operator this package decodes with", and that
  /// operator is FFmpeg's. It is worth being plain about what that does and does not buy. It makes
  /// what this encoder writes decode to what went in, both here and in FFmpeg, which is every
  /// decoder anyone can point at these files today. It does not establish that Intel's encoder
  /// targeted the same operator, because the recomposition has a term in its horizontal-detail band
  /// that looks like a mistake and there is no third implementation left to ask. Should that term
  /// ever be found wrong, both halves move together: the recomposition changes, and this routine is
  /// re-derived from the changed equations rather than patched to match.
  /// <para/>
  /// Read as a linear map, the recomposition is a separable pair of one-dimensional filters with a
  /// total gain of 64 and a single arithmetic shift at the end. The one-dimensional synthesis is
  /// <c>even(j) = 4*low(j) + 2*(high(j) + high(j-1))</c> and
  /// <c>odd(j) = 2*(low(j) + low(j+1)) - 6*high(j) + high(j-1) + high(j+1)</c>, with every index
  /// outside the band replicated from the edge. Substituting the first into the second cancels
  /// every low-pass term and collapses to <c>odd(j) = (even(j) + even(j+1))/2 - 8*high(j)</c>, so
  /// the inverse is the forward substitution in <see cref="_InvertPair"/>: no iteration, no filter
  /// design, just the algebra.
  /// <para/>
  /// Three of the four bands obey that model exactly. The horizontal-detail band does not: the
  /// recomposition folds its three-tap vertical high-pass into the odd output rows twice, once
  /// through the running filter value and once again through the two samples that were supposed to
  /// feed it, so its odd-row contribution is <c>2*b(i-1) - 12*b(i) + b(i+1)</c> rather than
  /// <c>b(i-1) - 6*b(i) + b(i+1)</c>. That is carried faithfully here because it is what the
  /// decoder does; it is what makes the operator non-separable, and it is exactly why a textbook
  /// analysis cannot invert it.
  /// <para/>
  /// The extra term is absorbed in two places. Down a column it changes the determinant of the
  /// two-by-two polyphase matrix from the constant <c>-32</c> to <c>4/z - 56</c>, which turns the
  /// column inverse from a finite filter into the first-order recursion in
  /// <see cref="_InvertDoubledPair"/> - stable, because its pole sits at one fourteenth. Along a
  /// row it survives as a single correction of <c>2*q(i,j+1)</c> on the odd samples of odd output
  /// rows, where <c>q</c> is that same extra term. Since <c>q</c> is a function of the band this
  /// routine is still solving for, the row correction is resolved by repeating the whole inversion
  /// with the previous pass's value. That fixed point contracts by 3/26 per pass whatever the size
  /// of the plane, so <see cref="_PASSES"/> passes take it far below what the doubles underneath
  /// can still represent.
  /// <para/>
  /// The plane is targeted at the middle of each output sample's acceptance interval rather than at
  /// its lower end. The recomposition's final shift discards six bits, so any of 64 consecutive
  /// values reconstructs the same byte; aiming at the centre spends that slack on absorbing the
  /// rounding of the real-valued coefficients into the integer samples the bitstream can carry,
  /// which is the only loss left in the round trip.
  /// </remarks>
  internal static short[][] Decompose(short[] source, int width, int height) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var count = halfWidth * halfHeight;

    // The intermediate rows the recomposition would hold between its two filter passes.
    var evenRows = new double[count];
    var oddRows = new double[count];
    var evenDetailRows = new double[count];
    var oddDetailRows = new double[count];

    var bands = new[] { new double[count], new double[count], new double[count], new double[count] };
    var correction = new double[count];

    var rowEven = new double[halfWidth];
    var rowOdd = new double[halfWidth];
    var rowLow = new double[halfWidth];
    var rowHigh = new double[halfWidth];
    var columnEven = new double[halfHeight];
    var columnOdd = new double[halfHeight];
    var columnLow = new double[halfHeight];
    var columnHigh = new double[halfHeight];

    for (var pass = 0; pass < _PASSES; ++pass) {
      for (var i = 0; i < halfHeight; ++i) {
        var band = i * halfWidth;
        var top = (i << 1) * width;
        var bottom = top + width;

        for (var j = 0; j < halfWidth; ++j) {
          rowEven[j] = _Target(source[top + (j << 1)]);
          rowOdd[j] = _Target(source[top + (j << 1) + 1]);
        }

        _InvertPair(rowEven, rowOdd, rowLow, rowHigh, halfWidth);
        Array.Copy(rowLow, 0, evenRows, band, halfWidth);
        Array.Copy(rowHigh, 0, evenDetailRows, band, halfWidth);

        for (var j = 0; j < halfWidth; ++j) {
          rowEven[j] = _Target(source[bottom + (j << 1)]);
          rowOdd[j] = _Target(source[bottom + (j << 1) + 1])
            + 2 * correction[band + Math.Min(j + 1, halfWidth - 1)];
        }

        _InvertPair(rowEven, rowOdd, rowLow, rowHigh, halfWidth);
        Array.Copy(rowLow, 0, oddRows, band, halfWidth);
        Array.Copy(rowHigh, 0, oddDetailRows, band, halfWidth);
      }

      for (var j = 0; j < halfWidth; ++j) {
        for (var i = 0; i < halfHeight; ++i) {
          columnEven[i] = evenRows[i * halfWidth + j];
          columnOdd[i] = oddRows[i * halfWidth + j];
        }

        _InvertDoubledPair(columnEven, columnOdd, columnLow, columnHigh, halfHeight);
        for (var i = 0; i < halfHeight; ++i) {
          bands[0][i * halfWidth + j] = columnLow[i];
          bands[1][i * halfWidth + j] = columnHigh[i];
        }

        for (var i = 0; i < halfHeight; ++i) {
          columnEven[i] = evenDetailRows[i * halfWidth + j];
          columnOdd[i] = oddDetailRows[i * halfWidth + j];
        }

        _InvertPair(columnEven, columnOdd, columnLow, columnHigh, halfHeight);
        for (var i = 0; i < halfHeight; ++i) {
          bands[2][i * halfWidth + j] = columnLow[i];
          bands[3][i * halfWidth + j] = columnHigh[i];
        }
      }

      var horizontalDetail = bands[1];
      for (var i = 0; i < halfHeight; ++i) {
        var here = i * halfWidth;
        var above = (i > 0 ? i - 1 : 0) * halfWidth;
        for (var j = 0; j < halfWidth; ++j)
          correction[here + j] = horizontalDetail[above + j] - 6 * horizontalDetail[here + j];
      }
    }

    var result = new[] { new short[count], new short[count], new short[count], new short[count] };
    for (var band = 0; band < 4; ++band)
      for (var i = 0; i < count; ++i)
        result[band][i] = checked((short)_Round(bands[band][i]));

    return result;
  }

  /// <summary>How many passes <see cref="Decompose"/> takes to settle its one self-referring term.</summary>
  /// <remarks>
  /// The repetition contracts by 3/26 per pass and that ratio does not move with the size of the
  /// plane, so twelve passes divide the first pass's error by more than ten to the eleventh - far
  /// past anything a double still distinguishes, and equally far past the half a coefficient that
  /// would round differently.
  /// </remarks>
  private const int _PASSES = 12;

  /// <summary>
  /// The value the recomposition has to reach for one plane sample, at the centre of the interval
  /// of 64 values that all reconstruct it.
  /// </summary>
  private static double _Target(short sample) => sample * 64.0 + 32.0;

  /// <summary>
  /// Inverts one pass of the separable five-three synthesis, recovering the low-pass and high-pass
  /// halves from the even and odd samples the synthesis would have produced from them.
  /// </summary>
  /// <remarks>
  /// Substituting the even equation into the odd one leaves
  /// <c>odd(j) = (even(j) + even(j+1))/2 - 8*high(j)</c>, so the high-pass half falls out one
  /// sample at a time. The last pair is the exception: there the synthesis replicated both halves
  /// past the edge, which leaves <c>odd = even(last) - high(last-1) - 7*high(last)</c> instead.
  /// </remarks>
  private static void _InvertPair(
    ReadOnlySpan<double> even,
    ReadOnlySpan<double> odd,
    Span<double> low,
    Span<double> high,
    int count) {
    for (var i = 0; i < count - 1; ++i)
      high[i] = ((even[i] + even[i + 1]) * 0.5 - odd[i]) * 0.125;

    high[count - 1] = count == 1
      ? (even[0] - odd[0]) * 0.125
      : (even[count - 1] - high[count - 2] - odd[count - 1]) / 7.0;

    for (var i = 0; i < count; ++i)
      low[i] = (even[i] - 2 * high[i] - 2 * high[i > 0 ? i - 1 : 0]) * 0.25;
  }

  /// <summary>
  /// Inverts the same pass down a column for the low-pass and horizontal-detail pair, where the
  /// recomposition applies its vertical high-pass twice.
  /// </summary>
  /// <remarks>
  /// The doubled term replaces the <c>-8*high(i)</c> of <see cref="_InvertPair"/> with
  /// <c>high(i-1) - 14*high(i)</c>, so the column no longer separates and has to be run as a
  /// recursion instead. Its ratio of one fourteenth makes it stable in the direction it is run and
  /// leaves nothing accumulated worth compensating. Both ends replicate past the edge, which
  /// removes the recursive term and divides by thirteen rather than fourteen.
  /// </remarks>
  private static void _InvertDoubledPair(
    ReadOnlySpan<double> even,
    ReadOnlySpan<double> odd,
    Span<double> low,
    Span<double> high,
    int count) {
    if (count == 1)
      high[0] = (even[0] - odd[0]) / 13.0;
    else {
      high[0] = ((even[0] + even[1]) * 0.5 - odd[0]) / 13.0;
      for (var i = 1; i < count - 1; ++i)
        high[i] = ((even[i] + even[i + 1]) * 0.5 + high[i - 1] - odd[i]) / 14.0;

      high[count - 1] = (even[count - 1] - odd[count - 1]) / 13.0;
    }

    for (var i = 0; i < count; ++i)
      low[i] = (even[i] - 2 * high[i] - 2 * high[i > 0 ? i - 1 : 0]) * 0.25;
  }

  private static int _Round(double value)
    => value >= 0 ? (int)Math.Floor(value + 0.5) : (int)Math.Ceiling(value - 0.5);

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
