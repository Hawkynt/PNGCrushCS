using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ArgbPixel = PngOptimizer.PngOptimizer.ArgbPixel;

namespace PngOptimizer;

/// <summary>Median-cut color quantization for reducing images to a limited palette</summary>
internal sealed class MedianCutQuantizer {
  private readonly Dictionary<long, int> _histogram;
  private readonly bool _includeAlpha;
  private readonly int _maxColors;
  private List<(byte R, byte G, byte B, byte A)>? _palette;

  internal MedianCutQuantizer(ArgbPixel[] pixels, int pixelCount, int maxColors, bool includeAlpha) {
    this._maxColors = maxColors;
    this._includeAlpha = includeAlpha;
    this._histogram = _BuildHistogram(pixels, pixelCount, includeAlpha);
  }

  /// <summary>Quantize colors using median-cut and return the resulting palette</summary>
  internal (List<(byte R, byte G, byte B, byte A)> palette, int actualCount) Quantize() {
    var boxes = new List<ColorBox> { new(this._histogram, this._includeAlpha) };

    while (boxes.Count < this._maxColors) {
      // Find the box with the largest weighted range that can be split
      var bestIdx = -1;
      var bestScore = -1L;
      for (var i = 0; i < boxes.Count; ++i) {
        if (boxes[i].ColorCount <= 1)
          continue;

        var score = boxes[i].WeightedRange;
        if (score > bestScore) {
          bestScore = score;
          bestIdx = i;
        }
      }

      if (bestIdx < 0)
        break;

      var (left, right) = boxes[bestIdx].Split();
      boxes[bestIdx] = left;
      boxes.Add(right);
    }

    // Build palette from box centroids, sorted: non-opaque first by frequency desc, then opaque by frequency desc
    var entries = new List<(byte R, byte G, byte B, byte A, long freq)>(boxes.Count);
    foreach (var box in boxes) {
      var (r, g, b, a, freq) = box.Centroid();
      entries.Add((r, g, b, a, freq));
    }

    entries.Sort((x, y) => {
      var xOpaque = x.A == 255;
      var yOpaque = y.A == 255;
      if (xOpaque != yOpaque)
        return xOpaque ? 1 : -1;

      return y.freq.CompareTo(x.freq);
    });

    this._palette = new List<(byte R, byte G, byte B, byte A)>(entries.Count);
    foreach (var (r, g, b, a, _) in entries)
      this._palette.Add((r, g, b, a));

    return (this._palette, this._palette.Count);
  }

  /// <summary>Find the nearest palette index for a given pixel</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal int FindNearest(ArgbPixel pixel) {
    var palette = this._palette!;
    var minDist = int.MaxValue;
    var bestIdx = 0;

    for (var i = 0; i < palette.Count; ++i) {
      var entry = palette[i];
      var dr = entry.R - pixel.R;
      var dg = entry.G - pixel.G;
      var db = entry.B - pixel.B;
      var dist = dr * dr + dg * dg + db * db;

      if (this._includeAlpha) {
        var da = entry.A - pixel.A;
        dist += da * da;
      }

      if (dist >= minDist)
        continue;

      minDist = dist;
      bestIdx = i;
    }

    return bestIdx;
  }

  private static Dictionary<long, int> _BuildHistogram(ArgbPixel[] pixels, int pixelCount, bool includeAlpha) {
    var histogram = new Dictionary<long, int>();
    for (var i = 0; i < pixelCount; ++i) {
      var p = pixels[i];
      var key = includeAlpha
        ? ((long)p.A << 24) | ((long)p.R << 16) | ((long)p.G << 8) | p.B
        : ((long)p.R << 16) | ((long)p.G << 8) | p.B;

      if (histogram.TryGetValue(key, out var count))
        histogram[key] = count + 1;
      else
        histogram[key] = 1;
    }

    return histogram;
  }

  /// <summary>A box in color space containing a set of colors with frequencies</summary>
  private sealed class ColorBox {
    private readonly List<(long key, int freq)> _colors;
    private readonly bool _includeAlpha;
    private byte _minR, _maxR, _minG, _maxG, _minB, _maxB, _minA, _maxA;
    private long _totalFreq;

    internal ColorBox(Dictionary<long, int> histogram, bool includeAlpha) {
      this._includeAlpha = includeAlpha;
      this._colors = new List<(long, int)>(histogram.Count);
      foreach (var kvp in histogram)
        this._colors.Add((kvp.Key, kvp.Value));

      this._ComputeBounds();
    }

    private ColorBox(List<(long key, int freq)> colors, bool includeAlpha) {
      this._includeAlpha = includeAlpha;
      this._colors = colors;
      this._ComputeBounds();
    }

    internal int ColorCount => this._colors.Count;
    internal long WeightedRange => this._totalFreq * this._LargestAxisRange();

    /// <summary>Split the box along its widest axis at the median</summary>
    internal (ColorBox left, ColorBox right) Split() {
      var axis = this._WidestAxis();
      this._colors.Sort((a, b) => _ExtractChannel(a.key, axis).CompareTo(_ExtractChannel(b.key, axis)));

      // Find median by accumulated frequency
      var halfFreq = this._totalFreq / 2;
      var accum = 0L;
      var splitIdx = 0;
      for (var i = 0; i < this._colors.Count - 1; ++i) {
        accum += this._colors[i].freq;
        if (accum < halfFreq)
          continue;

        splitIdx = i + 1;
        break;
      }

      if (splitIdx == 0)
        splitIdx = 1;

      var leftColors = this._colors.GetRange(0, splitIdx);
      var rightColors = this._colors.GetRange(splitIdx, this._colors.Count - splitIdx);

      return (new ColorBox(leftColors, this._includeAlpha), new ColorBox(rightColors, this._includeAlpha));
    }

    /// <summary>Compute the frequency-weighted centroid of this box</summary>
    internal (byte R, byte G, byte B, byte A, long freq) Centroid() {
      long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
      foreach (var (key, freq) in this._colors) {
        sumR += ((key >> 16) & 0xFF) * freq;
        sumG += ((key >> 8) & 0xFF) * freq;
        sumB += (key & 0xFF) * freq;
        sumA += this._includeAlpha ? ((key >> 24) & 0xFF) * freq : 255L * freq;
      }

      var r = (byte)((sumR + this._totalFreq / 2) / this._totalFreq);
      var g = (byte)((sumG + this._totalFreq / 2) / this._totalFreq);
      var b = (byte)((sumB + this._totalFreq / 2) / this._totalFreq);
      var a = (byte)((sumA + this._totalFreq / 2) / this._totalFreq);

      return (r, g, b, a, this._totalFreq);
    }

    private void _ComputeBounds() {
      this._minR = this._minG = this._minB = this._minA = 255;
      this._maxR = this._maxG = this._maxB = this._maxA = 0;
      this._totalFreq = 0;

      foreach (var (key, freq) in this._colors) {
        var r = (byte)((key >> 16) & 0xFF);
        var g = (byte)((key >> 8) & 0xFF);
        var b = (byte)(key & 0xFF);
        var a = this._includeAlpha ? (byte)((key >> 24) & 0xFF) : (byte)255;

        if (r < this._minR)
          this._minR = r;
        if (r > this._maxR)
          this._maxR = r;
        if (g < this._minG)
          this._minG = g;
        if (g > this._maxG)
          this._maxG = g;
        if (b < this._minB)
          this._minB = b;
        if (b > this._maxB)
          this._maxB = b;
        if (a < this._minA)
          this._minA = a;
        if (a > this._maxA)
          this._maxA = a;

        this._totalFreq += freq;
      }
    }

    private int _LargestAxisRange() {
      var rangeR = this._maxR - this._minR;
      var rangeG = this._maxG - this._minG;
      var rangeB = this._maxB - this._minB;
      var max = Math.Max(rangeR, Math.Max(rangeG, rangeB));
      if (this._includeAlpha)
        max = Math.Max(max, this._maxA - this._minA);

      return max;
    }

    private int _WidestAxis() {
      var rangeR = this._maxR - this._minR;
      var rangeG = this._maxG - this._minG;
      var rangeB = this._maxB - this._minB;
      var rangeA = this._includeAlpha ? this._maxA - this._minA : 0;

      if (rangeA >= rangeR && rangeA >= rangeG && rangeA >= rangeB)
        return 3; // alpha
      if (rangeR >= rangeG && rangeR >= rangeB)
        return 0; // red
      if (rangeG >= rangeB)
        return 1; // green
      return 2; // blue
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int _ExtractChannel(long key, int axis) {
      return axis switch {
        0 => (int)((key >> 16) & 0xFF),
        1 => (int)((key >> 8) & 0xFF),
        2 => (int)(key & 0xFF),
        3 => (int)((key >> 24) & 0xFF),
        _ => 0
      };
    }
  }
}
