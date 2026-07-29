using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>Reduces a BGRA32 buffer to a palette of at most N entries using median-cut in RGBA space.
/// Exact when the source holds no more distinct colours than the palette can address, so images that
/// already fit a palette survive a round-trip bit-exactly.</summary>
public static class ColorQuantizer {

  /// <summary>Palette (RGB triplets), per-entry alpha, and one palette index per pixel.</summary>
  public readonly record struct Result(byte[] Palette, byte[] AlphaTable, int[] Indices) {

    /// <summary>Number of palette entries.</summary>
    public int Count => this.Palette.Length / 3;
  }

  /// <summary>Quantizes BGRA32 pixel data to at most <paramref name="maxColors"/> palette entries.</summary>
  public static Result Quantize(byte[] bgra, int totalPixels, int maxColors) {
    ArgumentNullException.ThrowIfNull(bgra);
    if (maxColors < 1)
      throw new ArgumentOutOfRangeException(nameof(maxColors), maxColors, "Palette must hold at least one colour.");

    // A zero-pixel image still needs a one-entry palette: indexed formats have no way to express
    // "no palette", and downstream writers divide by the entry count.
    if (totalPixels <= 0)
      return new([0, 0, 0], [255], []);

    var histogram = _BuildHistogram(bgra, totalPixels);

    return histogram.Count <= maxColors
      ? _Exact(bgra, totalPixels, histogram)
      : _MedianCut(bgra, totalPixels, histogram, maxColors);
  }

  /// <summary>Maps every pixel to its nearest entry in a palette the caller fixes in advance.
  /// Formats with a hardware palette (CGA, DOOM, NES, C64 …) store bare indices and re-apply that
  /// palette on read, so their indices must address it — not one a quantizer invented.</summary>
  /// <param name="bgra">Source pixels, BGRA32.</param>
  /// <param name="totalPixels">Number of pixels to map.</param>
  /// <param name="palette">Target palette as RGB triplets.</param>
  /// <param name="alphaTable">Optional per-entry alpha; entries default to opaque when omitted.</param>
  public static Result MapToPalette(byte[] bgra, int totalPixels, byte[] palette, byte[]? alphaTable = null) {
    ArgumentNullException.ThrowIfNull(bgra);
    ArgumentNullException.ThrowIfNull(palette);
    var count = palette.Length / 3;
    if (count < 1)
      throw new ArgumentException("Palette must contain at least one colour.", nameof(palette));

    var alpha = new byte[count];
    for (var i = 0; i < count; ++i)
      alpha[i] = alphaTable != null && i < alphaTable.Length ? alphaTable[i] : (byte)255;

    var cache = new Dictionary<uint, int>();
    var indices = new int[Math.Max(0, totalPixels)];
    for (var i = 0; i < totalPixels; ++i) {
      var o = i * 4;
      if (o + 3 >= bgra.Length)
        break;

      var key = _Key(bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]);
      if (!cache.TryGetValue(key, out var index))
        cache[key] = index = _FindNearest(palette, alpha, bgra[o + 2], bgra[o + 1], bgra[o], bgra[o + 3]);

      indices[i] = index;
    }

    return new(palette, alpha, indices);
  }

  /// <summary>Packs one index per pixel into the bit layout <paramref name="format"/> expects.
  /// Rows are not padded — indices run continuously across the whole image, matching the
  /// <c>Indexed*ToBgra</c> decoders.</summary>
  public static byte[] PackIndices(int[] indices, PixelFormat format) {
    ArgumentNullException.ThrowIfNull(indices);
    var count = indices.Length;

    switch (format) {
      case PixelFormat.Indexed8: {
        var result = new byte[count];
        for (var i = 0; i < count; ++i)
          result[i] = (byte)indices[i];
        return result;
      }
      case PixelFormat.Indexed16: {
        var result = new byte[count * 2];
        for (var i = 0; i < count; ++i) {
          result[i * 2] = (byte)indices[i];
          result[i * 2 + 1] = (byte)(indices[i] >> 8);
        }
        return result;
      }
      case PixelFormat.Indexed4: {
        // 2 pixels per byte, high nibble first.
        var result = new byte[(count + 1) / 2];
        for (var i = 0; i < count; ++i) {
          var nibble = indices[i] & 0x0F;
          if ((i & 1) == 0)
            result[i >> 1] |= (byte)(nibble << 4);
          else
            result[i >> 1] |= (byte)nibble;
        }
        return result;
      }
      case PixelFormat.Indexed1: {
        // 8 pixels per byte, MSB first.
        var result = new byte[(count + 7) / 8];
        for (var i = 0; i < count; ++i)
          if ((indices[i] & 1) != 0)
            result[i >> 3] |= (byte)(0x80 >> (i & 7));
        return result;
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(format), format, "Not an indexed pixel format.");
    }
  }

  /// <summary>Largest palette an indexed format can address.</summary>
  public static int MaxColorsFor(PixelFormat format) => format switch {
    PixelFormat.Indexed1 => 2,
    PixelFormat.Indexed4 => 16,
    PixelFormat.Indexed8 => 256,
    PixelFormat.Indexed16 => 65536,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an indexed pixel format.")
  };

  private static Dictionary<uint, int> _BuildHistogram(byte[] bgra, int totalPixels) {
    var histogram = new Dictionary<uint, int>();
    for (var i = 0; i < totalPixels; ++i) {
      var o = i * 4;
      if (o + 3 >= bgra.Length)
        break;

      var key = _Key(bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]);
      histogram[key] = histogram.TryGetValue(key, out var n) ? n + 1 : 1;
    }

    return histogram;
  }

  /// <summary>Every distinct colour gets its own entry, so no pixel value changes.</summary>
  private static Result _Exact(byte[] bgra, int totalPixels, Dictionary<uint, int> histogram) {
    // Most-frequent-first: formats that truncate a palette keep the colours that matter most,
    // and index 0 lands on the dominant colour (a useful background for GIF/ICO).
    var colors = new List<KeyValuePair<uint, int>>(histogram);
    colors.Sort(static (x, y) => y.Value != x.Value ? y.Value.CompareTo(x.Value) : x.Key.CompareTo(y.Key));

    var palette = new byte[colors.Count * 3];
    var alpha = new byte[colors.Count];
    var lookup = new Dictionary<uint, int>(colors.Count);

    for (var i = 0; i < colors.Count; ++i) {
      var key = colors[i].Key;
      lookup[key] = i;
      palette[i * 3] = (byte)(key >> 16);      // R
      palette[i * 3 + 1] = (byte)(key >> 8);   // G
      palette[i * 3 + 2] = (byte)key;          // B
      alpha[i] = (byte)(key >> 24);
    }

    var indices = new int[totalPixels];
    for (var i = 0; i < totalPixels; ++i) {
      var o = i * 4;
      if (o + 3 >= bgra.Length)
        break;

      indices[i] = lookup[_Key(bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3])];
    }

    return new(palette, alpha, indices);
  }

  private static Result _MedianCut(byte[] bgra, int totalPixels, Dictionary<uint, int> histogram, int maxColors) {
    var boxes = new List<_Box> { new(histogram) };

    while (boxes.Count < maxColors) {
      var bestIndex = -1;
      var bestScore = -1L;
      for (var i = 0; i < boxes.Count; ++i) {
        if (boxes[i].ColorCount <= 1)
          continue;

        var score = boxes[i].WeightedRange;
        if (score <= bestScore)
          continue;

        bestScore = score;
        bestIndex = i;
      }

      if (bestIndex < 0)
        break;

      var (left, right) = boxes[bestIndex].Split();
      boxes[bestIndex] = left;
      boxes.Add(right);
    }

    var palette = new byte[boxes.Count * 3];
    var alpha = new byte[boxes.Count];
    for (var i = 0; i < boxes.Count; ++i) {
      var (r, g, b, a) = boxes[i].Centroid();
      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
      alpha[i] = a;
    }

    // Nearest-entry search is O(palette) per pixel; memoize by colour because real images have far
    // fewer distinct colours than pixels.
    var cache = new Dictionary<uint, int>(histogram.Count);
    var indices = new int[totalPixels];
    for (var i = 0; i < totalPixels; ++i) {
      var o = i * 4;
      if (o + 3 >= bgra.Length)
        break;

      var key = _Key(bgra[o], bgra[o + 1], bgra[o + 2], bgra[o + 3]);
      if (!cache.TryGetValue(key, out var index))
        cache[key] = index = _FindNearest(palette, alpha, bgra[o + 2], bgra[o + 1], bgra[o], bgra[o + 3]);

      indices[i] = index;
    }

    return new(palette, alpha, indices);
  }

  private static int _FindNearest(byte[] palette, byte[] alphaTable, byte r, byte g, byte b, byte a) {
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var i = 0; i < alphaTable.Length; ++i) {
      var dr = palette[i * 3] - r;
      var dg = palette[i * 3 + 1] - g;
      var db = palette[i * 3 + 2] - b;
      var da = alphaTable[i] - a;
      var distance = dr * dr + dg * dg + db * db + da * da;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
      if (distance == 0)
        break;
    }

    return best;
  }

  /// <summary>Packs a BGRA pixel into the ARGB key used by the histogram.</summary>
  private static uint _Key(byte b, byte g, byte r, byte a)
    => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

  /// <summary>A region of RGBA colour space holding a set of distinct colours and their frequencies.</summary>
  private sealed class _Box {
    private readonly List<KeyValuePair<uint, int>> _colors;
    private byte _minR, _maxR, _minG, _maxG, _minB, _maxB, _minA, _maxA;
    private long _totalFrequency;

    public _Box(Dictionary<uint, int> histogram) : this(new List<KeyValuePair<uint, int>>(histogram)) { }

    private _Box(List<KeyValuePair<uint, int>> colors) {
      this._colors = colors;
      this._ComputeBounds();
    }

    public int ColorCount => this._colors.Count;

    /// <summary>Splitting priority: a box is worth splitting in proportion to how many pixels it
    /// covers and how far its colours spread.</summary>
    public long WeightedRange => this._totalFrequency * this._LargestRange();

    public (_Box Left, _Box Right) Split() {
      var axis = this._WidestAxis();
      this._colors.Sort((x, y) => _Channel(x.Key, axis).CompareTo(_Channel(y.Key, axis)));

      var half = this._totalFrequency / 2;
      var accumulated = 0L;
      var splitIndex = 0;
      for (var i = 0; i < this._colors.Count - 1; ++i) {
        accumulated += this._colors[i].Value;
        if (accumulated < half)
          continue;

        splitIndex = i + 1;
        break;
      }

      if (splitIndex == 0)
        splitIndex = 1;

      return (
        new _Box(this._colors.GetRange(0, splitIndex)),
        new _Box(this._colors.GetRange(splitIndex, this._colors.Count - splitIndex))
      );
    }

    public (byte R, byte G, byte B, byte A) Centroid() {
      long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
      foreach (var (key, frequency) in this._colors) {
        sumR += ((key >> 16) & 0xFF) * (long)frequency;
        sumG += ((key >> 8) & 0xFF) * (long)frequency;
        sumB += (key & 0xFF) * (long)frequency;
        sumA += ((key >> 24) & 0xFF) * (long)frequency;
      }

      var total = this._totalFrequency;
      return (
        (byte)((sumR + total / 2) / total),
        (byte)((sumG + total / 2) / total),
        (byte)((sumB + total / 2) / total),
        (byte)((sumA + total / 2) / total)
      );
    }

    private void _ComputeBounds() {
      this._minR = this._minG = this._minB = this._minA = 255;
      this._maxR = this._maxG = this._maxB = this._maxA = 0;
      this._totalFrequency = 0;

      foreach (var (key, frequency) in this._colors) {
        var r = (byte)(key >> 16);
        var g = (byte)(key >> 8);
        var b = (byte)key;
        var a = (byte)(key >> 24);

        if (r < this._minR) this._minR = r;
        if (r > this._maxR) this._maxR = r;
        if (g < this._minG) this._minG = g;
        if (g > this._maxG) this._maxG = g;
        if (b < this._minB) this._minB = b;
        if (b > this._maxB) this._maxB = b;
        if (a < this._minA) this._minA = a;
        if (a > this._maxA) this._maxA = a;

        this._totalFrequency += frequency;
      }
    }

    private int _LargestRange() {
      var range = Math.Max(this._maxR - this._minR, Math.Max(this._maxG - this._minG, this._maxB - this._minB));
      return Math.Max(range, this._maxA - this._minA);
    }

    private int _WidestAxis() {
      int rangeR = this._maxR - this._minR, rangeG = this._maxG - this._minG;
      int rangeB = this._maxB - this._minB, rangeA = this._maxA - this._minA;

      if (rangeA >= rangeR && rangeA >= rangeG && rangeA >= rangeB)
        return 3;
      if (rangeR >= rangeG && rangeR >= rangeB)
        return 0;

      return rangeG >= rangeB ? 1 : 2;
    }

    private static int _Channel(uint key, int axis) => axis switch {
      0 => (int)((key >> 16) & 0xFF),
      1 => (int)((key >> 8) & 0xFF),
      2 => (int)(key & 0xFF),
      _ => (int)((key >> 24) & 0xFF)
    };
  }
}
