using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Core;

/// <summary>Chooses which colours a reduced picture should be built from.</summary>
public interface IQuantizer {

  /// <summary>The name this quantizer is selected by.</summary>
  string Name { get; }

  /// <summary>Picks at most <paramref name="colors"/> colours as RGB triplets.</summary>
  byte[] BuildPalette(ReadOnlySpan<byte> rgb, int width, int height, int colors);
}

/// <summary>Decides what to do with the colour a palette could not represent.</summary>
public interface IDitherer {

  /// <summary>The name this ditherer is selected by.</summary>
  string Name { get; }

  /// <summary>
  /// Spreads one pixel's error over the pixels not yet visited, or does nothing.
  /// </summary>
  /// <param name="rows">
  /// A rolling window of error rows, the current one first, three bytes a pixel and two pixels of
  /// slack at each end so a filter reaching sideways never leaves the row.
  /// </param>
  void Spread(int[][] rows, int x, int width, (int Red, int Green, int Blue) error);

  /// <summary>How many rows ahead the filter reaches, the current one included.</summary>
  int Reach { get; }
}

/// <summary>
/// Reduces a picture to a palette, in the terms the optimizers name their choices in.
/// </summary>
/// <remarks>
/// Two decisions, and neither has a right answer, so both are named rather than assumed: which
/// colours to keep, and what to do with the difference. Keeping them separate is what lets a
/// caller say "the commonest sixteen, undithered" for a drawing and "median cut with Floyd and
/// Steinberg" for a photograph, which want opposite things.
/// <para/>
/// This works on <see cref="RawImage"/> rather than on a platform bitmap, so it runs anywhere.
/// </remarks>
public static class ColorReduction {

  /// <summary>Every quantizer, in the order they are offered.</summary>
  public static IReadOnlyList<IQuantizer> Quantizers { get; } = [
    new PopularityQuantizer(),
    new MedianCutQuantizer(),
    new UniformQuantizer(),
  ];

  /// <summary>Every ditherer, in the order they are offered.</summary>
  public static IReadOnlyList<IDitherer> Ditherers { get; } = [
    new NoDitherer(),
    new FloydSteinbergDitherer(),
    new AtkinsonDitherer(),
  ];

  /// <summary>Finds a quantizer by name, ignoring case.</summary>
  public static IQuantizer? FindQuantizer(string name)
    => Quantizers.FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));

  /// <summary>Finds a ditherer by name, ignoring case.</summary>
  public static IDitherer? FindDitherer(string name)
    => Ditherers.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

  /// <summary>Reduces a picture to an indexed one with at most the given number of colours.</summary>
  public static RawImage Reduce(RawImage image, IQuantizer quantizer, IDitherer ditherer, int colors) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(quantizer);
    ArgumentNullException.ThrowIfNull(ditherer);
    if (colors is < 2 or > 256)
      throw new ArgumentOutOfRangeException(nameof(colors), colors, "A palette holds 2 to 256 colours.");

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var palette = quantizer.BuildPalette(rgb.PixelData, image.Width, image.Height, colors);
    var indices = new byte[image.Width * image.Height];

    // The error is carried in a rolling window rather than over the whole picture, since no filter
    // here reaches further than two rows down.
    var rows = new int[ditherer.Reach][];
    for (var i = 0; i < rows.Length; ++i)
      rows[i] = new int[(image.Width + 4) * 3];

    for (var y = 0; y < image.Height; ++y) {
      for (var x = 0; x < image.Width; ++x) {
        var source = (y * image.Width + x) * 3;
        var at = (x + 2) * 3;

        var red = Math.Clamp(rgb.PixelData[source] + rows[0][at], 0, 255);
        var green = Math.Clamp(rgb.PixelData[source + 1] + rows[0][at + 1], 0, 255);
        var blue = Math.Clamp(rgb.PixelData[source + 2] + rows[0][at + 2], 0, 255);

        var index = PaletteQuantizer.Nearest(palette, colors, red, green, blue);
        indices[y * image.Width + x] = (byte)index;

        ditherer.Spread(
          rows, x, image.Width,
          (red - palette[index * 3], green - palette[index * 3 + 1], blue - palette[index * 3 + 2]));
      }

      // The finished row's errors are spent; it becomes the far end of the window.
      var first = rows[0];
      for (var i = 1; i < rows.Length; ++i)
        rows[i - 1] = rows[i];

      Array.Clear(first);
      rows[^1] = first;
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = colors,
    };
  }
}

/// <summary>Keeps the colours that appear most often.</summary>
/// <remarks>
/// Exact for a picture that already has few enough colours, which is the case worth being exact
/// for: a screenshot or a drawing keeps every colour it had. It is a poor choice for a photograph,
/// where the commonest colours are all the same shade of sky.
/// </remarks>
public sealed class PopularityQuantizer : IQuantizer {

  public string Name => "Popularity";

  public byte[] BuildPalette(ReadOnlySpan<byte> rgb, int width, int height, int colors) {
    var counts = new Dictionary<int, int>();
    for (var i = 0; i + 2 < rgb.Length; i += 3) {
      var key = (rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2];
      counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
    }

    var chosen = counts.Keys.ToList();

    // Ties break on the colour itself, so the result does not depend on dictionary order.
    chosen.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));

    return _ToPalette(chosen, colors);
  }

  internal static byte[] _ToPalette(List<int> chosen, int colors) {
    var palette = new byte[colors * 3];
    for (var i = 0; i < colors && i < chosen.Count; ++i) {
      palette[i * 3] = (byte)(chosen[i] >> 16);
      palette[i * 3 + 1] = (byte)(chosen[i] >> 8);
      palette[i * 3 + 2] = (byte)chosen[i];
    }

    return palette;
  }
}

/// <summary>
/// Splits the colours into boxes of equal population and keeps the average of each.
/// </summary>
/// <remarks>
/// Where the popularity method asks which colours there are most of, this asks which parts of the
/// picture's colour range are worth a name — so a photograph keeps the range of its sky rather than
/// sixteen shades of one part of it. It costs a sort per split and nothing else.
/// </remarks>
public sealed class MedianCutQuantizer : IQuantizer {

  public string Name => "MedianCut";

  public byte[] BuildPalette(ReadOnlySpan<byte> rgb, int width, int height, int colors) {
    var pixels = new List<int>(width * height);
    for (var i = 0; i + 2 < rgb.Length; i += 3)
      pixels.Add((rgb[i] << 16) | (rgb[i + 1] << 8) | rgb[i + 2]);

    if (pixels.Count == 0)
      return new byte[colors * 3];

    var boxes = new List<List<int>> { pixels };

    while (boxes.Count < colors) {
      // Split whichever box spans the most of any one channel; a box that spans nothing is done.
      var widest = -1;
      var widestSpan = 0;
      var widestChannel = 0;

      for (var i = 0; i < boxes.Count; ++i) {
        if (boxes[i].Count < 2)
          continue;

        for (var channel = 0; channel < 3; ++channel) {
          var shift = 16 - channel * 8;
          var low = 255;
          var high = 0;

          foreach (var color in boxes[i]) {
            var value = (color >> shift) & 255;
            low = Math.Min(low, value);
            high = Math.Max(high, value);
          }

          if (high - low <= widestSpan)
            continue;

          widestSpan = high - low;
          widest = i;
          widestChannel = channel;
        }
      }

      if (widest < 0)
        break;

      var box = boxes[widest];
      var sortShift = 16 - widestChannel * 8;
      box.Sort((a, b) => ((a >> sortShift) & 255).CompareTo((b >> sortShift) & 255));

      // Split at the nearest boundary between two distinct values rather than at the middle of
      // the population. Splitting by population alone can leave both halves holding the same
      // colours — a box of a hundred of one and one of another divides into a hundred of the one
      // and a mixture — so a picture that would have fitted exactly comes back averaged.
      var middle = _DistinctBoundary(box, sortShift);
      boxes[widest] = box.GetRange(0, middle);
      boxes.Add(box.GetRange(middle, box.Count - middle));
    }

    var palette = new byte[colors * 3];
    for (var i = 0; i < boxes.Count && i < colors; ++i) {
      long red = 0, green = 0, blue = 0;
      foreach (var color in boxes[i]) {
        red += (color >> 16) & 255;
        green += (color >> 8) & 255;
        blue += color & 255;
      }

      var count = Math.Max(boxes[i].Count, 1);
      palette[i * 3] = (byte)(red / count);
      palette[i * 3 + 1] = (byte)(green / count);
      palette[i * 3 + 2] = (byte)(blue / count);
    }

    return palette;
  }

  /// <summary>
  /// Where to cut a sorted box so that the two halves differ: the boundary between distinct values
  /// nearest the middle.
  /// </summary>
  private static int _DistinctBoundary(List<int> box, int shift) {
    var middle = box.Count / 2;

    for (var offset = 0; offset <= box.Count; ++offset) {
      var forward = middle + offset;
      if (forward > 0 && forward < box.Count
          && ((box[forward] >> shift) & 255) != ((box[forward - 1] >> shift) & 255))
        return forward;

      var back = middle - offset;
      if (back > 0 && back < box.Count
          && ((box[back] >> shift) & 255) != ((box[back - 1] >> shift) & 255))
        return back;
    }

    // Every value in the box is the same in this channel, so any cut is as good as another.
    return Math.Max(1, middle);
  }
}

/// <summary>Spreads the palette evenly over the colour cube, ignoring the picture.</summary>
/// <remarks>
/// The only one that does not look at the picture, which is exactly when it is wanted: a palette
/// that does not depend on the content can be shared between frames of an animation without every
/// frame's colours shifting under the viewer.
/// </remarks>
public sealed class UniformQuantizer : IQuantizer {

  public string Name => "Uniform";

  public byte[] BuildPalette(ReadOnlySpan<byte> rgb, int width, int height, int colors) {
    // As near a cube as fits, with the spare levels given to green, which the eye reads best.
    var levels = 1;
    while ((levels + 1) * (levels + 1) * (levels + 1) <= colors)
      ++levels;

    var greens = levels;
    while (levels * (greens + 1) * levels <= colors)
      ++greens;

    var palette = new byte[colors * 3];
    var at = 0;

    for (var r = 0; r < levels && at < colors; ++r)
    for (var g = 0; g < greens && at < colors; ++g)
    for (var b = 0; b < levels && at < colors; ++b, ++at) {
      palette[at * 3] = (byte)(levels == 1 ? 0 : r * 255 / (levels - 1));
      palette[at * 3 + 1] = (byte)(greens == 1 ? 0 : g * 255 / (greens - 1));
      palette[at * 3 + 2] = (byte)(levels == 1 ? 0 : b * 255 / (levels - 1));
    }

    return palette;
  }
}

/// <summary>Throws the error away.</summary>
/// <remarks>
/// The right choice for a drawing with flat areas: a dither turns a solid region into noise where
/// the nearest colour alone would have kept it solid.
/// </remarks>
public sealed class NoDitherer : IDitherer {

  public string Name => "None";

  public int Reach => 1;

  public void Spread(int[][] rows, int x, int width, (int Red, int Green, int Blue) error) { }
}

/// <summary>Floyd and Steinberg's filter: seven sixteenths right, then three, five and one below.</summary>
public sealed class FloydSteinbergDitherer : IDitherer {

  public string Name => "FloydSteinberg";

  public int Reach => 2;

  public void Spread(int[][] rows, int x, int width, (int Red, int Green, int Blue) error) {
    var at = (x + 2) * 3;
    _Add(rows[0], at + 3, error, 7, 16);
    _Add(rows[1], at - 3, error, 3, 16);
    _Add(rows[1], at, error, 5, 16);
    _Add(rows[1], at + 3, error, 1, 16);
  }

  internal static void _Add(int[] row, int at, (int Red, int Green, int Blue) error, int weight, int total) {
    if (at < 0 || at + 2 >= row.Length)
      return;

    row[at] += error.Red * weight / total;
    row[at + 1] += error.Green * weight / total;
    row[at + 2] += error.Blue * weight / total;
  }
}

/// <summary>
/// Atkinson's filter, which passes on only three quarters of the error.
/// </summary>
/// <remarks>
/// Losing a quarter is the point: it keeps a dithered picture's blacks black and its whites white
/// where Floyd and Steinberg would grey both, at the cost of losing detail in the extremes. It is
/// what the first Macintosh dithered with and it still suits a one-bit target better.
/// </remarks>
public sealed class AtkinsonDitherer : IDitherer {

  public string Name => "Atkinson";

  public int Reach => 3;

  public void Spread(int[][] rows, int x, int width, (int Red, int Green, int Blue) error) {
    var at = (x + 2) * 3;
    FloydSteinbergDitherer._Add(rows[0], at + 3, error, 1, 8);
    FloydSteinbergDitherer._Add(rows[0], at + 6, error, 1, 8);
    FloydSteinbergDitherer._Add(rows[1], at - 3, error, 1, 8);
    FloydSteinbergDitherer._Add(rows[1], at, error, 1, 8);
    FloydSteinbergDitherer._Add(rows[1], at + 3, error, 1, 8);
    FloydSteinbergDitherer._Add(rows[2], at, error, 1, 8);
  }
}
