using System;
using System.Collections.Generic;

namespace FileFormat.Iwc;

/// <summary>Writes IWC files the WaveL applet decodes.</summary>
/// <remarks>
/// WaveL's own encoder picks its quantiser steps from its quality setting; this writer picks, for
/// every band, the finest step whose 253 values still span the band — step 1, and so no loss at
/// all, whenever they fit. Colour is converted to YCbCr choosing, for every pixel, the integer
/// triple the applet's conversion maps closest back to the original.
/// </remarks>
internal static class IwcWriter {

  /// <summary>The deepest pyramid the writer builds unless told otherwise; WaveL's samples use 6 and 7.</summary>
  public const int DefaultMaxLevels = 7;

  public static byte[] ToBytes(IwcFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    if (file.Width < 2 || file.Height < 2 || file.Width > short.MaxValue || file.Height > short.MaxValue)
      throw new ArgumentException(
        $"IWC cannot hold a {file.Width}x{file.Height} picture: the applet's lifting step reads two samples of every row and column, so both sides must be 2..32767.",
        nameof(file));

    if ((long)file.Width * file.Height > IwcHeader.MaxPixels)
      throw new ArgumentException($"IWC pictures are limited to {IwcHeader.MaxPixels} pixels here.", nameof(file));

    if (file.PixelData.Length < file.Width * file.Height * 3)
      throw new ArgumentException($"IWC pixel data holds {file.PixelData.Length} bytes; {file.Width * file.Height * 3} are needed.", nameof(file));

    if (file.Quality is < short.MinValue or > short.MaxValue)
      throw new ArgumentException("The IWC quality field is 16 bits.", nameof(file));

    var levels = file.Levels;
    if (levels == 0) {
      levels = 1;
      while (levels < DefaultMaxLevels && IwcHeader.IsDecomposable(file.Width, file.Height, levels + 1))
        ++levels;
    } else if (!IwcHeader.IsDecomposable(file.Width, file.Height, levels))
      throw new ArgumentException($"A {file.Width}x{file.Height} picture cannot be decomposed {levels} times: every level needs 2x2 samples.", nameof(file));

    var planes = _ToYCbCr(file);
    var nodes = IwcCodec.Nodes(file.Width, file.Height, levels);
    var horizontal = new int[levels][][];
    var vertical = new int[levels][][];
    var diagonal = new int[levels][][];
    var approximation = new int[3][];
    for (var channel = 0; channel < 3; ++channel) {
      var current = planes[channel];
      for (var level = 0; level < levels; ++level) {
        horizontal[level] ??= new int[3][];
        vertical[level] ??= new int[3][];
        diagonal[level] ??= new int[3][];
        var (w, h) = nodes[level];
        (current, horizontal[level][channel], vertical[level][channel], diagonal[level][channel]) = IwcCodec.Analyse(current, w, h);
      }

      approximation[channel] = current;
    }

    var symbols = new List<byte>((int)IwcCodec.SymbolCount(file.Width, file.Height, levels));
    var (lastWidth, lastHeight) = nodes[^1];
    int approximationWidth = lastWidth / 2, approximationHeight = lastHeight / 2;
    var approximationScales = new float[3];
    var approximationMinima = new short[3];
    var approximationValues = new byte[3][];
    for (var channel = 0; channel < 3; ++channel)
      (approximationMinima[channel], approximationScales[channel], approximationValues[channel]) = _QuantiseApproximation(approximation[channel]);

    for (var x = 0; x < approximationWidth; ++x)
      for (var y = 0; y < approximationHeight; ++y)
        for (var channel = 0; channel < 3; ++channel)
          symbols.Add(approximationValues[channel][y * approximationWidth + x]);

    var levelScales = new IwcLevelScales[levels];
    for (var level = levels - 1; level >= 0; --level) {
      var (w, h) = nodes[level];
      int ax = w / 2, ay = h / 2, hx = w - ax, vy = h - ay;
      var scales = new IwcLevelScales(new float[3], new float[3], new float[3]);
      for (var channel = 0; channel < 3; ++channel)
        scales.Horizontal[channel] = _WriteDetail(symbols, horizontal[level][channel], hx, ay, columnMajor: true);
      for (var channel = 0; channel < 3; ++channel)
        scales.Vertical[channel] = _WriteDetail(symbols, vertical[level][channel], ax, vy, columnMajor: false);
      for (var channel = 0; channel < 3; ++channel)
        scales.Diagonal[channel] = _WriteDetail(symbols, diagonal[level][channel], hx, vy, columnMajor: true);
      levelScales[level] = scales;
    }

    var payload = IwcSplayCoder.Encode(symbols.ToArray());
    var header = new IwcHeader(IwcHeader.KnownVersion, file.Width, file.Height, levels, file.Quality, approximationScales, approximationMinima, levelScales, payload.Length);
    return header.ToBytes(payload);
  }

  /// <summary>Per pixel, the integer Y, Cb, Cr the applet turns back into the colour nearest the original.</summary>
  private static int[][] _ToYCbCr(IwcFile file) {
    var count = file.Width * file.Height;
    var planes = new[] { new int[count], new int[count], new int[count] };
    var pixels = file.PixelData;
    for (var i = 0; i < count; ++i) {
      int red = pixels[i * 3], green = pixels[i * 3 + 1], blue = pixels[i * 3 + 2];
      var y0 = (int)Math.Round(0.299 * red + 0.587 * green + 0.114 * blue);
      var cb0 = (int)Math.Round(-0.168736 * red - 0.331264 * green + 0.5 * blue);
      var cr0 = (int)Math.Round(0.5 * red - 0.418688 * green - 0.081312 * blue);
      int bestY = y0, bestCb = cb0, bestCr = cr0, bestError = int.MaxValue;
      for (var dy = -1; dy <= 1; ++dy)
        for (var dcb = -1; dcb <= 1; ++dcb)
          for (var dcr = -1; dcr <= 1; ++dcr) {
            var (r, g, b) = IwcCodec.ToRgb((short)(y0 + dy), (short)(cb0 + dcb), (short)(cr0 + dcr));
            var error = Math.Abs(r - red) + Math.Abs(g - green) + Math.Abs(b - blue);
            if (error >= bestError)
              continue;
            (bestY, bestCb, bestCr, bestError) = (y0 + dy, cb0 + dcb, cr0 + dcr, error);
          }

      planes[0][i] = bestY;
      planes[1][i] = bestCb;
      planes[2][i] = bestCr;
    }

    return planes;
  }

  /// <summary>Offset, step and byte values of an approximation band.</summary>
  private static (short Minimum, float Scale, byte[] Values) _QuantiseApproximation(int[] band) {
    int minimum = int.MaxValue, maximum = int.MinValue;
    foreach (var value in band) {
      minimum = Math.Min(minimum, value);
      maximum = Math.Max(maximum, value);
    }

    if (band.Length == 0)
      minimum = maximum = 0;

    minimum = Math.Clamp(minimum, short.MinValue, short.MaxValue);
    var scale = Math.Max(1f, (maximum - minimum) / (float)IwcSplayCoder.MaxSymbol);
    var values = new byte[band.Length];
    for (var i = 0; i < band.Length; ++i)
      values[i] = _Nearest(band[i], (int)Math.Round((band[i] - minimum) / scale), symbol => (short)minimum + symbol * scale);

    return ((short)minimum, scale, values);
  }

  /// <summary>Appends a detail band in the applet's scan order and returns its step.</summary>
  private static float _WriteDetail(List<byte> symbols, int[] band, int width, int height, bool columnMajor) {
    int low = 0, high = 0;
    foreach (var value in band) {
      low = Math.Min(low, value);
      high = Math.Max(high, value);
    }

    var scale = Math.Max(1f, Math.Max(high / (float)(IwcSplayCoder.MaxSymbol - 128), -low / 128f));
    var outer = columnMajor ? width : height;
    var inner = columnMajor ? height : width;
    for (var o = 0; o < outer; ++o)
      for (var i = 0; i < inner; ++i) {
        var (x, y) = columnMajor ? (o, i) : (i, o);
        var value = band[y * width + x];
        symbols.Add(_Nearest(value, 128 + (int)Math.Round(value / scale), symbol => (symbol - 128) * scale));
      }

    return scale;
  }

  /// <summary>The byte value within one step of <paramref name="guess"/> that the applet's dequantiser maps closest to <paramref name="target"/>.</summary>
  private static byte _Nearest(int target, int guess, Func<int, float> dequantise) {
    var best = Math.Clamp(guess, 0, IwcSplayCoder.MaxSymbol);
    var bestError = long.MaxValue;
    for (var candidate = guess - 1; candidate <= guess + 1; ++candidate) {
      if (candidate < 0 || candidate > IwcSplayCoder.MaxSymbol)
        continue;
      var error = Math.Abs((long)(short)IwcCodec.JavaInt(dequantise(candidate)) - target);
      if (error < bestError)
        (best, bestError) = (candidate, error);
    }

    return (byte)best;
  }
}
