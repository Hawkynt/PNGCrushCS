namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Puts the four wavelet bands of a scalable picture back together into one luminance plane.
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

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
