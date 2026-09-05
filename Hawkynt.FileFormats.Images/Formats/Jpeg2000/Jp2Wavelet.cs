using System;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000;

/// <summary>The discrete wavelet transforms of ITU-T T.800 Annex F.</summary>
/// <remarks>
/// Two details decide whether this agrees with any other implementation. The first is parity: a
/// subband's samples are indexed by their position on the reference grid, so a tile that begins at
/// an odd coordinate has its low-pass and high-pass halves the other way round, and the filter has
/// to be told. The second is order. Synthesis interleaves the four subbands, filters rows, then
/// filters columns; analysis therefore has to filter columns first and rows second, because it undoes
/// synthesis in reverse. Doing both passes the same way round is exactly invertible against itself
/// and wrong against everyone else, since the 5/3 rounding does not commute.
/// </remarks>
internal static class Jp2Wavelet {

  private const float _ALPHA = -1.586134342f;
  private const float _BETA = -0.052980118f;
  private const float _GAMMA = 0.882911075f;
  private const float _DELTA = 0.443506852f;
  private const float _K = 1.230174105f;

  /// <summary>The high-pass scaling of the 9/7 synthesis, which is not the reciprocal of K.</summary>
  private const float _TWO_OVER_K = 1.625732422f;

  /// <summary>
  /// Reversible 5/3 synthesis of one interleaved line. <paramref name="parity"/> is the low bit of
  /// the line's first coordinate; when it is one the first sample is a high-pass one.
  /// </summary>
  public static void Inverse53(int[] signal, int lowCount, int highCount, int parity) {
    ArgumentNullException.ThrowIfNull(signal);

    if (parity == 0) {
      if (highCount <= 0 && lowCount <= 1)
        return;

      for (var i = 0; i < lowCount; ++i)
        signal[2 * i] -= (_High(signal, highCount, i - 1) + _High(signal, highCount, i) + 2) >> 2;
      for (var i = 0; i < highCount; ++i)
        signal[2 * i + 1] += (_Low(signal, lowCount, i) + _Low(signal, lowCount, i + 1)) >> 1;

      return;
    }

    if (lowCount == 0 && highCount == 1) {
      signal[0] /= 2;
      return;
    }

    for (var i = 0; i < lowCount; ++i)
      signal[2 * i + 1] -= (_Even(signal, highCount, i) + _Even(signal, highCount, i + 1) + 2) >> 2;
    for (var i = 0; i < highCount; ++i)
      signal[2 * i] += (_Odd(signal, lowCount, i) + _Odd(signal, lowCount, i - 1)) >> 1;
  }

  /// <summary>Reversible 5/3 analysis of one interleaved line; the exact inverse of <see cref="Inverse53"/>.</summary>
  public static void Forward53(int[] signal, int lowCount, int highCount, int parity) {
    ArgumentNullException.ThrowIfNull(signal);

    if (parity == 0) {
      if (highCount <= 0 && lowCount <= 1)
        return;

      for (var i = 0; i < highCount; ++i)
        signal[2 * i + 1] -= (_Low(signal, lowCount, i) + _Low(signal, lowCount, i + 1)) >> 1;
      for (var i = 0; i < lowCount; ++i)
        signal[2 * i] += (_High(signal, highCount, i - 1) + _High(signal, highCount, i) + 2) >> 2;

      return;
    }

    if (lowCount == 0 && highCount == 1) {
      signal[0] *= 2;
      return;
    }

    for (var i = 0; i < highCount; ++i)
      signal[2 * i] -= (_Odd(signal, lowCount, i) + _Odd(signal, lowCount, i - 1)) >> 1;
    for (var i = 0; i < lowCount; ++i)
      signal[2 * i + 1] += (_Even(signal, highCount, i) + _Even(signal, highCount, i + 1) + 2) >> 2;
  }

  /// <summary>Irreversible 9/7 synthesis of one interleaved line.</summary>
  public static void Inverse97(float[] signal, int lowCount, int highCount, int parity) {
    ArgumentNullException.ThrowIfNull(signal);

    var lowStart = parity;
    var highStart = 1 - parity;
    if (lowCount == 0 && highCount == 0)
      return;

    for (var i = 0; i < lowCount; ++i)
      signal[lowStart + 2 * i] *= _K;
    for (var i = 0; i < highCount; ++i)
      signal[highStart + 2 * i] *= _TWO_OVER_K;

    _Lift97(signal, lowStart, lowCount, highStart, highCount, -_DELTA);
    _Lift97(signal, highStart, highCount, lowStart, lowCount, -_GAMMA);
    _Lift97(signal, lowStart, lowCount, highStart, highCount, -_BETA);
    _Lift97(signal, highStart, highCount, lowStart, lowCount, -_ALPHA);
  }

  /// <summary>Irreversible 9/7 analysis of one interleaved line.</summary>
  public static void Forward97(float[] signal, int lowCount, int highCount, int parity) {
    ArgumentNullException.ThrowIfNull(signal);

    var lowStart = parity;
    var highStart = 1 - parity;
    if (lowCount == 0 && highCount == 0)
      return;

    _Lift97(signal, highStart, highCount, lowStart, lowCount, _ALPHA);
    _Lift97(signal, lowStart, lowCount, highStart, highCount, _BETA);
    _Lift97(signal, highStart, highCount, lowStart, lowCount, _GAMMA);
    _Lift97(signal, lowStart, lowCount, highStart, highCount, _DELTA);

    for (var i = 0; i < lowCount; ++i)
      signal[lowStart + 2 * i] /= _K;
    for (var i = 0; i < highCount; ++i)
      signal[highStart + 2 * i] /= _TWO_OVER_K;
  }

  /// <summary>
  /// One lifting step: every sample of the target half takes a weighted sum of the two source-half
  /// samples that straddle it, with the edge value repeated outside the line.
  /// </summary>
  private static void _Lift97(
    float[] signal,
    int targetStart,
    int targetCount,
    int sourceStart,
    int sourceCount,
    float weight
  ) {
    if (targetCount == 0 || sourceCount == 0)
      return;

    // Target i sits between source i-1 and source i when the source half starts one place later,
    // and between source i and source i+1 when it starts one place earlier.
    var offset = targetStart < sourceStart ? -1 : 0;
    for (var i = 0; i < targetCount; ++i) {
      var left = Math.Clamp(i + offset, 0, sourceCount - 1);
      var right = Math.Clamp(i + offset + 1, 0, sourceCount - 1);
      signal[targetStart + 2 * i] += weight * (signal[sourceStart + 2 * left] + signal[sourceStart + 2 * right]);
    }
  }

  private static int _Low(int[] signal, int count, int index)
    => signal[2 * Math.Clamp(index, 0, count - 1)];

  private static int _High(int[] signal, int count, int index)
    => signal[2 * Math.Clamp(index, 0, count - 1) + 1];

  private static int _Even(int[] signal, int count, int index)
    => signal[2 * Math.Clamp(index, 0, count - 1)];

  private static int _Odd(int[] signal, int count, int index)
    => signal[2 * Math.Clamp(index, 0, count - 1) + 1];

  /// <summary>Rebuilds a tile-component's samples from its subband coefficients.</summary>
  public static void InverseTransform(Jp2TileComponent component) {
    ArgumentNullException.ThrowIfNull(component);

    if (component.Style.Transform == 1)
      _InverseReversible(component);
    else
      _InverseIrreversible(component);
  }

  /// <summary>Splits a tile-component's samples into its subband coefficients.</summary>
  public static void ForwardTransform(Jp2TileComponent component) {
    ArgumentNullException.ThrowIfNull(component);
    if (component.Style.Transform != 1)
      throw new NotSupportedException("The JPEG 2000 encoder writes the reversible 5/3 transform only.");

    var levels = component.Resolutions.Length - 1;
    var current = component.Samples;
    var currentWidth = component.Width;
    var currentHeight = component.Height;

    for (var resolution = levels; resolution >= 1; --resolution) {
      var level = component.Resolutions[resolution];
      var lower = component.Resolutions[resolution - 1];
      var width = level.X1 - level.X0;
      var height = level.Y1 - level.Y0;
      if (width <= 0 || height <= 0)
        continue;

      if (width != currentWidth || height != currentHeight)
        throw new InvalidOperationException("JPEG 2000 analysis lost track of the resolution geometry.");

      var lowWidth = lower.X1 - lower.X0;
      var lowHeight = lower.Y1 - lower.Y0;
      var parityX = level.X0 & 1;
      var parityY = level.Y0 & 1;

      var column = new int[height];
      for (var x = 0; x < width; ++x) {
        for (var y = 0; y < height; ++y)
          column[y] = current[y * width + x];

        Forward53(column, lowHeight, height - lowHeight, parityY);

        for (var y = 0; y < height; ++y)
          current[y * width + x] = column[y];
      }

      var row = new int[width];
      for (var y = 0; y < height; ++y) {
        Array.Copy(current, y * width, row, 0, width);
        Forward53(row, lowWidth, width - lowWidth, parityX);
        Array.Copy(row, 0, current, y * width, width);
      }

      var next = new int[lowWidth * lowHeight];
      _Deinterleave(current, width, height, level, lower, next);

      current = next;
      currentWidth = lowWidth;
      currentHeight = lowHeight;
    }

    var root = component.Resolutions[0].Bands[0];
    Array.Copy(current, root.Coefficients, Math.Min(current.Length, root.Coefficients.Length));
  }

  private static void _InverseReversible(Jp2TileComponent component) {
    var levels = component.Resolutions.Length - 1;
    var root = component.Resolutions[0].Bands[0];
    var current = (int[])root.Coefficients.Clone();
    var currentWidth = root.Width;
    var currentHeight = root.Height;

    for (var resolution = 1; resolution <= levels; ++resolution) {
      var level = component.Resolutions[resolution];
      var width = level.X1 - level.X0;
      var height = level.Y1 - level.Y0;
      var buffer = new int[Math.Max(0, width) * Math.Max(0, height)];
      if (width <= 0 || height <= 0) {
        current = buffer;
        currentWidth = Math.Max(0, width);
        currentHeight = Math.Max(0, height);
        continue;
      }

      _Interleave(current, currentWidth, currentHeight, level, buffer);

      var lowWidth = Jp2Math.CeilDivPow2(level.X1, 1) - Jp2Math.CeilDivPow2(level.X0, 1);
      var lowHeight = Jp2Math.CeilDivPow2(level.Y1, 1) - Jp2Math.CeilDivPow2(level.Y0, 1);
      var parityX = level.X0 & 1;
      var parityY = level.Y0 & 1;

      var row = new int[width];
      for (var y = 0; y < height; ++y) {
        Array.Copy(buffer, y * width, row, 0, width);
        Inverse53(row, lowWidth, width - lowWidth, parityX);
        Array.Copy(row, 0, buffer, y * width, width);
      }

      var column = new int[height];
      for (var x = 0; x < width; ++x) {
        for (var y = 0; y < height; ++y)
          column[y] = buffer[y * width + x];

        Inverse53(column, lowHeight, height - lowHeight, parityY);

        for (var y = 0; y < height; ++y)
          buffer[y * width + x] = column[y];
      }

      current = buffer;
      currentWidth = width;
      currentHeight = height;
    }

    component.Samples = current;
  }

  private static void _InverseIrreversible(Jp2TileComponent component) {
    var levels = component.Resolutions.Length - 1;
    var root = component.Resolutions[0].Bands[0];
    var current = new float[root.Coefficients.Length];
    for (var i = 0; i < current.Length; ++i)
      current[i] = root.Coefficients[i] * (root.StepSize * 0.5f);

    var currentWidth = root.Width;
    var currentHeight = root.Height;

    for (var resolution = 1; resolution <= levels; ++resolution) {
      var level = component.Resolutions[resolution];
      var width = level.X1 - level.X0;
      var height = level.Y1 - level.Y0;
      var buffer = new float[Math.Max(0, width) * Math.Max(0, height)];
      if (width <= 0 || height <= 0) {
        current = buffer;
        currentWidth = Math.Max(0, width);
        currentHeight = Math.Max(0, height);
        continue;
      }

      _InterleaveFloat(current, currentWidth, currentHeight, level, buffer);

      var lowWidth = Jp2Math.CeilDivPow2(level.X1, 1) - Jp2Math.CeilDivPow2(level.X0, 1);
      var lowHeight = Jp2Math.CeilDivPow2(level.Y1, 1) - Jp2Math.CeilDivPow2(level.Y0, 1);
      var parityX = level.X0 & 1;
      var parityY = level.Y0 & 1;

      var row = new float[width];
      for (var y = 0; y < height; ++y) {
        Array.Copy(buffer, y * width, row, 0, width);
        Inverse97(row, lowWidth, width - lowWidth, parityX);
        Array.Copy(row, 0, buffer, y * width, width);
      }

      var column = new float[height];
      for (var x = 0; x < width; ++x) {
        for (var y = 0; y < height; ++y)
          column[y] = buffer[y * width + x];

        Inverse97(column, lowHeight, height - lowHeight, parityY);

        for (var y = 0; y < height; ++y)
          buffer[y * width + x] = column[y];
      }

      current = buffer;
      currentWidth = width;
      currentHeight = height;
    }

    var samples = new int[current.Length];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (int)MathF.Round(current[i]);

    component.Samples = samples;
  }

  /// <summary>F.3.3 2D_INTERLEAVE: the four subbands back onto one grid, low samples on even coordinates.</summary>
  private static void _Interleave(int[] low, int lowWidth, int lowHeight, Jp2Resolution level, int[] target) {
    var width = level.X1 - level.X0;
    var originX = level.X0;
    var originY = level.Y0;

    for (var y = 0; y < lowHeight; ++y)
      for (var x = 0; x < lowWidth; ++x) {
        var gx = 2 * (Jp2Math.CeilDivPow2(originX, 1) + x) - originX;
        var gy = 2 * (Jp2Math.CeilDivPow2(originY, 1) + y) - originY;
        target[gy * width + gx] = low[y * lowWidth + x];
      }

    foreach (var band in level.Bands) {
      var xob = band.Orientation & 1;
      var yob = (band.Orientation >> 1) & 1;
      for (var y = 0; y < band.Height; ++y)
        for (var x = 0; x < band.Width; ++x) {
          var gx = 2 * (band.X0 + x) + xob - originX;
          var gy = 2 * (band.Y0 + y) + yob - originY;
          target[gy * width + gx] = band.Coefficients[y * band.Width + x];
        }
    }
  }

  private static void _InterleaveFloat(float[] low, int lowWidth, int lowHeight, Jp2Resolution level, float[] target) {
    var width = level.X1 - level.X0;
    var originX = level.X0;
    var originY = level.Y0;

    for (var y = 0; y < lowHeight; ++y)
      for (var x = 0; x < lowWidth; ++x) {
        var gx = 2 * (Jp2Math.CeilDivPow2(originX, 1) + x) - originX;
        var gy = 2 * (Jp2Math.CeilDivPow2(originY, 1) + y) - originY;
        target[gy * width + gx] = low[y * lowWidth + x];
      }

    foreach (var band in level.Bands) {
      var xob = band.Orientation & 1;
      var yob = (band.Orientation >> 1) & 1;
      for (var y = 0; y < band.Height; ++y)
        for (var x = 0; x < band.Width; ++x) {
          var gx = 2 * (band.X0 + x) + xob - originX;
          var gy = 2 * (band.Y0 + y) + yob - originY;
          target[gy * width + gx] = band.Coefficients[y * band.Width + x] * (band.StepSize * 0.5f);
        }
    }
  }

  /// <summary>The inverse of <see cref="_Interleave"/>, writing the three detail bands and the next low band.</summary>
  private static void _Deinterleave(int[] source, int width, int height, Jp2Resolution level, Jp2Resolution lower, int[] low) {
    _ = height;
    var originX = level.X0;
    var originY = level.Y0;
    var lowWidth = lower.X1 - lower.X0;

    for (var y = 0; y < lower.Y1 - lower.Y0; ++y)
      for (var x = 0; x < lowWidth; ++x) {
        var gx = 2 * (lower.X0 + x) - originX;
        var gy = 2 * (lower.Y0 + y) - originY;
        low[y * lowWidth + x] = source[gy * width + gx];
      }

    foreach (var band in level.Bands) {
      var xob = band.Orientation & 1;
      var yob = (band.Orientation >> 1) & 1;
      for (var y = 0; y < band.Height; ++y)
        for (var x = 0; x < band.Width; ++x) {
          var gx = 2 * (band.X0 + x) + xob - originX;
          var gy = 2 * (band.Y0 + y) + yob - originY;
          band.Coefficients[y * band.Width + x] = source[gy * width + gx];
        }
    }
  }
}
