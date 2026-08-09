using System;

namespace FileFormat.ChinonEs1000;

/// <summary>Turns the Chinon ES-1000's raw CCD readout into RGB.</summary>
/// <remarks>
/// The algorithm is YOSHIDA Hideki's <c>cmttoppm.c</c>; the arithmetic is XnView's, which does all
/// of it in double where the C does the interpolation and the saturation in <c>float</c>. That is
/// not a detail one may tidy up: a step that divides three times over by a neighbour's own estimate
/// turns the last bit of a float into a whole level often enough to show, and the two disagree on
/// fifteen samples out of 361500 on one test picture and thirty-seven on another. The order the
/// operations stand in is kept as XnView's instructions have it for the same reason.
/// </remarks>
internal static class ChinonEs1000Demosaic {

  private const int _Columns = ChinonEs1000File.CcdColumns;
  private const int _Lines = ChinonEs1000File.CcdLines;
  private const int _Left = ChinonEs1000File.LeftMargin;
  private const int _Right = ChinonEs1000File.RightMargin;
  private const int _Top = ChinonEs1000File.TopMargin;
  private const int _Bottom = ChinonEs1000File.BottomMargin;
  private const int _NetColumns = ChinonEs1000File.Width;
  private const int _NetLines = ChinonEs1000File.Height;
  private const int _NetPixels = _NetColumns * _NetLines;

  private const int _Scale = 64;
  private const int _HorizontalInterpolations = 3;
  private const int _HistogramSteps = 4096;

  private const double _RedFactor = 0.64;
  private const double _GreenFactor = 0.58;
  private const double _BlueFactor = 1.00;
  private const double _RedIntensity = 0.476;
  private const double _GreenIntensity = 0.299;
  private const double _BlueIntensity = 0.175;
  private const double _Saturation = 1.5;
  private const int _NormPercentage = 3;
  private const double _Gamma = 0.5;

  public static byte[] ToRgb24(byte[] ccd) {
    var horizontal = new short[_Lines * _Columns];
    var red = new short[_Lines * _Columns];
    var green = new short[_Lines * _Columns];
    var blue = new short[_Lines * _Columns];

    _SetInitialInterpolation(ccd, horizontal);
    _InterpolateHorizontally(ccd, horizontal);
    _InterpolateVertically(ccd, horizontal, red, green, blue);
    _AdjustColourAndSaturation(red, green, blue);
    _DetermineLimits(red, green, blue, out var lowI, out var highI);
    return _Output(red, green, blue, lowI, highI);
  }

  private static void _SetInitialInterpolation(byte[] ccd, short[] horizontal) {
    for (var line = 0; line < _Lines; ++line) {
      var row = line * _Columns;
      horizontal[row + _Left] = (short)(ccd[row + _Left + 1] * _Scale);
      horizontal[row + _Columns - _Right - 1] = (short)(ccd[row + _Columns - _Right - 2] * _Scale);
      for (var column = _Left + 1; column < _Columns - _Right - 1; ++column)
        horizontal[row + column] = (short)((ccd[row + column - 1] + ccd[row + column + 1]) * (_Scale / 2));
    }
  }

  private static void _InterpolateHorizontally(byte[] ccd, short[] horizontal) {
    for (var line = _Top - 1; line < _Lines - _Bottom + 1; ++line) {
      var row = line * _Columns;
      for (var i = 0; i < _HorizontalInterpolations; ++i)
        for (var initialColumn = _Left + 1; initialColumn <= _Left + 2; ++initialColumn)
          for (var column = initialColumn; column < _Columns - _Right - 1; column += 2) {
            var left = (double)ccd[row + column - 1] / horizontal[row + column - 1];
            var right = (double)ccd[row + column + 1] / horizontal[row + column + 1];
            var scaled = (left + right) * ccd[row + column] * (_Scale * _Scale / 2) + 0.5;
            horizontal[row + column] = unchecked((short)_Truncate(scaled));
          }
    }
  }

  private static void _InterpolateVertically(byte[] ccd, short[] horizontal, short[] red, short[] green, short[] blue) {
    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      var up = row - _Columns;
      var down = row + _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column) {
        var thisCcd = ccd[row + column] * _Scale;
        var upCcd = ccd[up + column] * _Scale;
        var downCcd = ccd[down + column] * _Scale;
        var thisHorizontal = (int)horizontal[row + column];
        var thisIntensity = thisCcd + thisHorizontal;
        var upIntensity = horizontal[up + column] + upCcd;
        var downIntensity = horizontal[down + column] + downCcd;

        int thisVertical;
        if (line == _Top)
          thisVertical = _Truncate((double)downCcd / downIntensity * thisIntensity + 0.5);
        else if (line == _Lines - _Bottom - 1)
          thisVertical = _Truncate((double)upCcd / upIntensity * thisIntensity + 0.5);
        else {
          var mean = (double)upCcd / upIntensity + (double)downCcd / downIntensity;
          thisVertical = _Truncate(mean * thisIntensity * 0.5 + 0.5);
        }

        int r, g, b;
        if ((line & 1) != 0) {
          if ((column & 1) != 0) {
            var r2gb = thisCcd;
            var g2b = thisHorizontal;
            var rg2 = thisVertical;
            r = (2 * (r2gb - g2b) + rg2) / 5;
            g = (rg2 - r) / 2;
            b = g2b - 2 * g;
          } else {
            var g2b = thisCcd;
            var r2gb = thisHorizontal;
            var rgb2 = thisVertical;
            r = (3 * r2gb - g2b - rgb2) / 5;
            g = 2 * r - r2gb + g2b;
            b = g2b - 2 * g;
          }
        } else {
          if ((column & 1) != 0) {
            var rg2 = thisCcd;
            var rgb2 = thisHorizontal;
            var r2gb = thisVertical;
            b = (3 * rgb2 - r2gb - rg2) / 5;
            g = (rgb2 - r2gb + rg2 - b) / 2;
            r = rg2 - 2 * g;
          } else {
            var rgb2 = thisCcd;
            var rg2 = thisHorizontal;
            var g2b = thisVertical;
            b = (g2b - 2 * (rg2 - rgb2)) / 5;
            g = (g2b - b) / 2;
            r = rg2 - 2 * g;
          }
        }

        if (r < 0) r = 0;
        if (g < 0) g = 0;
        if (b < 0) b = 0;
        red[row + column] = unchecked((short)r);
        green[row + column] = unchecked((short)g);
        blue[row + column] = unchecked((short)b);
      }
    }
  }

  private static void _AdjustColourAndSaturation(short[] red, short[] green, short[] blue) {
    var squareRootSaturation = Math.Sqrt(_Saturation);
    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column) {
        var at = row + column;
        var r = red[at] * _RedFactor;
        var g = green[at] * _GreenFactor;
        var b = blue[at] * _BlueFactor;

        var intensity = r * _RedIntensity + g * _GreenIntensity + b * _BlueIntensity;

        // The three are sorted and then the middle is lifted by the square root of the saturation
        // and the top by the saturation itself, the bottom being left where it is.
        int minimum, middle, maximum;
        if (r > g) {
          if (r > b) {
            maximum = 0;
            if (g > b) { minimum = 2; middle = 1; } else { minimum = 1; middle = 2; }
          } else {
            minimum = 1; middle = 0; maximum = 2;
          }
        } else {
          if (g > b) {
            maximum = 1;
            if (r > b) { minimum = 2; middle = 0; } else { minimum = 0; middle = 2; }
          } else {
            minimum = 0; middle = 1; maximum = 2;
          }
        }

        var v0 = r;
        var v1 = g;
        var v2 = b;
        var low = minimum == 0 ? v0 : minimum == 1 ? v1 : v2;
        var mid = middle == 0 ? v0 : middle == 1 ? v1 : v2;
        var high = maximum == 0 ? v0 : maximum == 1 ? v1 : v2;
        mid = low + squareRootSaturation * (mid - low);
        high = low + _Saturation * (high - low);
        if (middle == 0) v0 = mid; else if (middle == 1) v1 = mid; else v2 = mid;
        if (maximum == 0) v0 = high; else if (maximum == 1) v1 = high; else v2 = high;
        r = v0;
        g = v1;
        b = v2;

        var newIntensity = r * _RedIntensity + g * _GreenIntensity + b * _BlueIntensity;
        var correction = intensity / newIntensity;
        r *= correction;
        g *= correction;
        b *= correction;

        red[at] = unchecked((short)_Truncate(r + 0.5));
        green[at] = unchecked((short)_Truncate(g + 0.5));
        blue[at] = unchecked((short)_Truncate(b + 0.5));
      }
    }
  }

  private static void _DetermineLimits(short[] red, short[] green, short[] blue, out int lowI, out int highI) {
    var maximumI = 0;
    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column) {
        var i = _Max3(red[row + column], green[row + column], blue[row + column]);
        if (i > maximumI)
          maximumI = i;
      }
    }

    // A picture with nothing in it would divide by zero here; the C would fault, so the reader
    // hands back a black picture instead rather than pretend the limits mean anything.
    if (maximumI <= 0) {
      lowI = 0;
      highI = 0;
      return;
    }

    var histogram = new uint[_HistogramSteps + 1];
    var threshold = _NetPixels * _NormPercentage / 100;

    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column)
        _Count(histogram, _Min3(red[row + column], green[row + column], blue[row + column]) * _HistogramSteps / maximumI);
    }

    int sum;
    for (lowI = 0, sum = 0; lowI <= _HistogramSteps && sum < threshold; ++lowI)
      sum += (int)histogram[lowI];
    lowI = (lowI * maximumI + _HistogramSteps / 2) / _HistogramSteps;

    Array.Clear(histogram);
    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column)
        _Count(histogram, _Max3(red[row + column], green[row + column], blue[row + column]) * _HistogramSteps / maximumI);
    }

    for (highI = _HistogramSteps, sum = 0; highI >= 0 && sum < threshold; --highI)
      sum += (int)histogram[highI];
    highI = (highI * maximumI + _HistogramSteps / 2) / _HistogramSteps;
  }

  private static byte[] _Output(short[] red, short[] green, short[] blue, int lowI, int highI) {
    var gammaTable = _MakeGammaTable(highI - lowI);
    var result = new byte[_NetColumns * _NetLines * 3];
    var at = 0;
    for (var line = _Top; line < _Lines - _Bottom; ++line) {
      var row = line * _Columns;
      for (var column = _Left; column < _Columns - _Right; ++column) {
        result[at++] = _Lookup(red[row + column], lowI, highI, gammaTable);
        result[at++] = _Lookup(green[row + column], lowI, highI, gammaTable);
        result[at++] = _Lookup(blue[row + column], lowI, highI, gammaTable);
      }
    }

    return result;
  }

  private static byte[] _MakeGammaTable(int range) {
    if (range <= 0)
      return [];

    var factor = Math.Pow(256.0, 1.0 / _Gamma) / range;
    var table = new byte[range];
    for (var i = 0; i < range; ++i) {
      var g = _Truncate(Math.Pow(i * factor, _Gamma) + 0.5);
      table[i] = (byte)(g > 255 ? 255 : g);
    }

    return table;
  }

  private static byte _Lookup(int i, int lowI, int highI, byte[] gammaTable) {
    if (i <= lowI)
      return 0;
    if (i >= highI)
      return 255;

    return gammaTable[i - lowI];
  }

  /// <summary>Counts one sample into the histogram, dropping the ones whose bucket falls outside
  /// it. A bright picture can push a colour past what a signed word holds, and the C wraps it to a
  /// negative there, which then indexes the histogram from outside; the count is simply lost. This
  /// keeps that loss without writing anywhere it should not.</summary>
  private static void _Count(uint[] histogram, int bucket) {
    if ((uint)bucket < (uint)histogram.Length)
      ++histogram[bucket];
  }

  /// <summary>Truncates towards zero the way the machine instruction the converter is built on
  /// does. It answers with the smallest integer for anything it cannot represent, including a
  /// value that is not a number, where .NET's own conversion would saturate the other way.</summary>
  private static int _Truncate(double value)
    => value is >= -2147483648.0 and < 2147483648.0 ? (int)value : int.MinValue;

  private static int _Min3(int x, int y, int z) => x < y ? (x < z ? x : z) : (y < z ? y : z);

  private static int _Max3(int x, int y, int z) => x > y ? (x > z ? x : z) : (y > z ? y : z);
}
