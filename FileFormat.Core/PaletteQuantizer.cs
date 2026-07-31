using System;

namespace FileFormat.Core;

/// <summary>Reduces a picture to a fixed set of colours.</summary>
/// <remarks>
/// Most of the machines here have a palette they cannot change, so writing a picture for one means
/// choosing, for every pixel, which of a handful of colours to use. Two choices have to be made and
/// neither has a right answer, so both are named here rather than buried:
/// <para/>
/// Distance is measured on the channel values as stored, weighted by how much of the eye's
/// brightness response each channel carries. Plain Euclidean distance in RGB treats a shift in blue
/// as costing what the same shift in green costs, which it visibly does not; weighting is closer to
/// what a person sees without needing a colour space nobody stored their pictures in.
/// <para/>
/// Error is diffused by Floyd and Steinberg's filter, which pushes what a pixel could not represent
/// on to its neighbours. On a sixteen-colour screen that is the difference between a photograph and
/// a poster; on a two-colour one it is the difference between a picture and a silhouette. It can be
/// turned off, because a drawing with flat areas comes out worse for it — a dither turns a solid
/// region into noise where the nearest colour alone would have kept it solid.
/// </remarks>
public static class PaletteQuantizer {

  /// <summary>How much of the eye's brightness response each channel carries, out of 256.</summary>
  private const int _RED_WEIGHT = 77;
  private const int _GREEN_WEIGHT = 150;
  private const int _BLUE_WEIGHT = 29;

  /// <summary>The index of the palette entry closest to a colour.</summary>
  public static int Nearest(ReadOnlySpan<byte> palette, int colors, int red, int green, int blue) {
    var best = 0;
    var bestDistance = long.MaxValue;

    for (var i = 0; i < colors && (i + 1) * 3 <= palette.Length; ++i) {
      long dr = palette[i * 3] - red;
      long dg = palette[i * 3 + 1] - green;
      long db = palette[i * 3 + 2] - blue;
      var distance = dr * dr * _RED_WEIGHT + dg * dg * _GREEN_WEIGHT + db * db * _BLUE_WEIGHT;

      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return best;
  }

  /// <summary>Reduces an RGB picture to indices into a palette.</summary>
  /// <param name="dither">
  /// Whether to diffuse what each pixel could not represent on to its neighbours.
  /// </param>
  public static byte[] Quantize(
    ReadOnlySpan<byte> rgb, int width, int height, ReadOnlySpan<byte> palette, int colors, bool dither = true) {
    var indices = new byte[width * height];

    if (!dither) {
      for (var i = 0; i < indices.Length && (i + 1) * 3 <= rgb.Length; ++i)
        indices[i] = (byte)Nearest(palette, colors, rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

      return indices;
    }

    // The error is carried in a rolling pair of rows rather than over the whole picture, since
    // Floyd and Steinberg's filter never reaches further than the row below.
    var current = new int[(width + 2) * 3];
    var next = new int[(width + 2) * 3];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var source = (y * width + x) * 3;
        if (source + 2 >= rgb.Length)
          break;

        var at = (x + 1) * 3;
        var red = Math.Clamp(rgb[source] + current[at], 0, 255);
        var green = Math.Clamp(rgb[source + 1] + current[at + 1], 0, 255);
        var blue = Math.Clamp(rgb[source + 2] + current[at + 2], 0, 255);

        var index = Nearest(palette, colors, red, green, blue);
        indices[y * width + x] = (byte)index;

        var errors = (red - palette[index * 3], green - palette[index * 3 + 1], blue - palette[index * 3 + 2]);

        // Seven sixteenths to the right, and three, five and one to the row below.
        _Spread(current, at + 3, errors, 7);
        _Spread(next, at - 3, errors, 3);
        _Spread(next, at, errors, 5);
        _Spread(next, at + 3, errors, 1);
      }

      (current, next) = (next, current);
      Array.Clear(next);
    }

    return indices;
  }

  private static void _Spread(int[] row, int at, (int Red, int Green, int Blue) error, int weight) {
    if (at < 0 || at + 2 >= row.Length)
      return;

    row[at] += error.Red * weight / 16;
    row[at + 1] += error.Green * weight / 16;
    row[at + 2] += error.Blue * weight / 16;
  }
}
