using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fif;

/// <summary>How a picture is drawn at a requested output size, as DECO_32.DLL plans it.</summary>
/// <param name="Width">The requested output width.</param>
/// <param name="Height">The requested output height.</param>
/// <param name="Level">The reconstruction level: 0 half resolution, 1 the coded size, each further level doubling it.</param>
/// <param name="ExtraHalvings">For FIF1 below half resolution: how many of the finest coded levels are left out.</param>
/// <param name="ScaleX">Output width over the drawn width, or 0 when the drawn size is the output size.</param>
/// <param name="ScaleY">See <paramref name="ScaleX"/>.</param>
internal readonly record struct FifOutputPlan(int Width, int Height, int Level, int ExtraHalvings, float ScaleX, float ScaleY) {

  public bool Resamples => ScaleX > 0 || ScaleY > 0;

  /// <summary>The plan for drawing an <paramref name="originalWidth"/>×<paramref name="originalHeight"/> picture at <paramref name="width"/>×<paramref name="height"/>.</summary>
  /// <remarks>
  /// The picture is drawn at the smallest power-of-two multiple of its size that covers the request
  /// (half resolution at the least) and area-averaged down to it. Exactly half the size is drawn
  /// directly at half resolution.
  /// </remarks>
  public static FifOutputPlan For(int originalWidth, int originalHeight, int width, int height) {
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "The output size must be positive.");

    var halfWidth = originalWidth >> 1;
    var halfHeight = originalHeight >> 1;
    if (width == halfWidth && height == halfHeight)
      return new(width, height, 0, 0, 0, 0);

    if (width <= halfWidth && height <= halfHeight) {
      var extra = 1;
      int w = halfWidth >> 1, h = halfHeight;
      while (width <= w && height <= (h >>= 1)) {
        w >>= 1;
        ++extra;
      }

      return new(width, height, 0, extra, (float)width / halfWidth, (float)height / halfHeight);
    }

    int drawnWidth = originalWidth, drawnHeight = originalHeight, level = 1;
    for (; (drawnWidth < width || drawnHeight < height) && level < 10; ++level) {
      drawnWidth *= 2;
      drawnHeight *= 2;
    }

    if (level == 10)
      throw new ArgumentOutOfRangeException(nameof(width), "The output size is beyond 256 times the picture.");

    return drawnWidth == width && drawnHeight == height
      ? new(width, height, level, 0, 0, 0)
      : new(width, height, level, 0, (float)width / drawnWidth, (float)height / drawnHeight);
  }

  /// <summary>Where each region lands in the output, given its size at the drawn level.</summary>
  /// <remarks>
  /// Regions tile the picture in rows; the first row ends where a region starts again at x = 0. Without
  /// resampling a region keeps its scaled position. With it, the edges are running sums of the
  /// scaled widths and heights, each rounded to an even pixel (truncating sum + 1, low bit cleared),
  /// the running width kept in single precision as the DLL keeps it; the last column and row reach
  /// the scaled total of the first row and column.
  /// </remarks>
  public (int X, int Y, int Width, int Height)[] Rectangles(IReadOnlyList<(int X, int Y, int Width, int Height)> regions) {
    var count = regions.Count;
    var rects = new (int X, int Y, int Width, int Height)[count];
    if (!Resamples) {
      for (var i = 0; i < count; ++i) {
        var r = regions[i];
        rects[i] = (FifReconstruction.AtLevel(r.X, Level), FifReconstruction.AtLevel(r.Y, Level), FifReconstruction.AtLevel(r.Width, Level), FifReconstruction.AtLevel(r.Height, Level));
      }

      return rects;
    }

    var columns = 1;
    while (columns < count && regions[columns].X != 0)
      ++columns;

    long totalWidth = 0, totalHeight = 0;
    for (var i = 0; i < columns; ++i)
      totalWidth += FifReconstruction.AtLevel(regions[i].Width, Level);
    for (var i = 0; i < count; i += columns)
      totalHeight += FifReconstruction.AtLevel(regions[i].Height, Level);

    var outputWidth = _Edge(totalWidth * (double)ScaleX);
    var outputHeight = _Edge(totalHeight * (double)ScaleY);
    var rows = count / columns;
    var row = 1;
    var accumulatedWidth = 0f;
    var accumulatedHeight = 0.0;
    int x = 0, y = 0;
    for (var i = 0; i < count; ++i) {
      var levelWidth = FifReconstruction.AtLevel(regions[i].Width, Level);
      var levelHeight = FifReconstruction.AtLevel(regions[i].Height, Level);
      if (i % columns == 0)
        accumulatedHeight += levelHeight * (double)ScaleY;

      var sum = levelWidth * (double)ScaleX + accumulatedWidth;
      accumulatedWidth = (float)sum;
      var width = (_Edge(sum) & 0xFFFF) - x;
      var height = (row == rows ? outputHeight : _Edge(accumulatedHeight) & 0xFFFF) - y;
      if ((i + 1) % columns == 0) {
        ++row;
        width = outputWidth - x;
        rects[i] = (x, y, width, height);
        x = 0;
        accumulatedWidth = 0;
        y += height;
      } else {
        rects[i] = (x, y, width, height);
        x += width;
      }
    }

    return rects;
  }

  private static int _Edge(double value) => (int)Math.Truncate(value + 1.0) & ~1;

  /// <summary>
  /// Area-averages a plane down to a smaller size, with exact fractional coverage and a truncating
  /// division, as the DLL resamples every region plane.
  /// </summary>
  public static byte[] AreaResample(byte[] source, int sourceWidth, int sourceHeight, int width, int height) {
    if (width > sourceWidth || height > sourceHeight)
      throw new InvalidDataException("The DLL's resampler only reduces.");

    var output = new byte[width * height];
    var denominator = (long)sourceWidth * sourceHeight;
    for (var j = 0; j < height; ++j) {
      var top = j * sourceHeight;
      var firstRow = top / height;
      var lastRow = (top + sourceHeight - 1) / height;
      var firstRowWeight = (firstRow + 1) * height - top;
      var lastRowWeight = top + sourceHeight - lastRow * height;
      for (var i = 0; i < width; ++i) {
        var left = i * sourceWidth;
        var firstColumn = left / width;
        var lastColumn = (left + sourceWidth - 1) / width;
        var firstColumnWeight = (firstColumn + 1) * width - left;
        var lastColumnWeight = left + sourceWidth - lastColumn * width;

        long RowSum(int r) {
          var at = r * sourceWidth;
          if (firstColumn == lastColumn)
            return (long)source[at + firstColumn] * sourceWidth;

          long middle = 0;
          for (var c = firstColumn + 1; c < lastColumn; ++c)
            middle += source[at + c];

          return (long)source[at + firstColumn] * firstColumnWeight + middle * width + (long)source[at + lastColumn] * lastColumnWeight;
        }

        long total;
        if (firstRow == lastRow)
          total = RowSum(firstRow) * sourceHeight;
        else {
          long middle = 0;
          for (var r = firstRow + 1; r < lastRow; ++r)
            middle += RowSum(r);

          total = RowSum(firstRow) * firstRowWeight + middle * height + RowSum(lastRow) * lastRowWeight;
        }

        output[j * width + i] = (byte)(total / denominator);
      }
    }

    return output;
  }
}
