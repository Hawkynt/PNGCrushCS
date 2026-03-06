using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Bmp;
using Crush.Core;

namespace Optimizer.Bmp;

public sealed class BmpOptimizer {
  private readonly byte[] _argbPixelData;
  private readonly int _height;
  private readonly bool _isGrayscale;
  private readonly BmpOptimizationOptions _options;
  private readonly int _uniqueColors;
  private readonly int _width;

  public BmpOptimizer(Bitmap image, BmpOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new BmpOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._argbPixelData, out this._isGrayscale, out this._uniqueColors);
  }

  public static BmpOptimizer FromFile(FileInfo file, BmpOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BMP file not found.", file.FullName);

    using var bmp = new Bitmap(file.FullName);
    return new BmpOptimizer(bmp, options);
  }

  public async ValueTask<BmpOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();

    var results = await this._RunCombos(combos, cancellationToken, progress);

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return results.MinBy(r => r.result.CompressedSize).result;
  }

  private async ValueTask<List<(BmpOptimizationCombo combo, BmpOptimizationResult result)>> _RunCombos(
    BmpOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(BmpOptimizationCombo combo, BmpOptimizationResult result)>();
    var resultsLock = new object();
    var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);
    var completedCount = 0;
    var bestSize = long.MaxValue;

    var tasks = combos.Select(combo => Task.Run(async () => {
      await semaphore.WaitAsync(cancellationToken);
      try {
        var sw = Stopwatch.StartNew();
        var result = this._TestCombination(combo);
        sw.Stop();

        if (result == null)
          return;

        var optimizationResult = new BmpOptimizationResult(
          combo.ColorMode,
          combo.Compression,
          combo.RowOrder,
          result.Length,
          sw.Elapsed,
          result
        );

        lock (resultsLock) {
          results.Add((combo, optimizationResult));
          if (optimizationResult.CompressedSize < bestSize)
            bestSize = optimizationResult.CompressedSize;
        }

        var done = Interlocked.Increment(ref completedCount);
        progress?.Report(new OptimizationProgress(done, combos.Length, bestSize, phase));
      } finally {
        semaphore.Release();
      }
    }));

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));
    return results;
  }

  private BmpOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<BmpOptimizationCombo>();

    var colorModes = new List<BmpColorMode>(this._options.ColorModes);
    if (this._options.AutoSelectColorMode) {
      if (!colorModes.Contains(BmpColorMode.Rgb24))
        colorModes.Add(BmpColorMode.Rgb24);
      if (!colorModes.Contains(BmpColorMode.Rgb16_565))
        colorModes.Add(BmpColorMode.Rgb16_565);
      if (this._isGrayscale && !colorModes.Contains(BmpColorMode.Grayscale8))
        colorModes.Add(BmpColorMode.Grayscale8);
      if (this._uniqueColors <= 256 && !colorModes.Contains(BmpColorMode.Palette8))
        colorModes.Add(BmpColorMode.Palette8);
      if (this._uniqueColors <= 16 && !colorModes.Contains(BmpColorMode.Palette4))
        colorModes.Add(BmpColorMode.Palette4);
      if (this._uniqueColors <= 2 && !colorModes.Contains(BmpColorMode.Palette1))
        colorModes.Add(BmpColorMode.Palette1);
    }

    foreach (var colorMode in colorModes)
    foreach (var compression in this._options.Compressions)
    foreach (var rowOrder in this._options.RowOrders) {
      // Prune invalid combinations
      if (compression == BmpCompression.Rle8 && colorMode != BmpColorMode.Palette8 &&
          colorMode != BmpColorMode.Grayscale8)
        continue;

      if (compression == BmpCompression.Rle4 && colorMode != BmpColorMode.Palette4)
        continue;

      // RLE requires bottom-up row order in BMP
      if (compression is BmpCompression.Rle8 or BmpCompression.Rle4 && rowOrder == BmpRowOrder.TopDown)
        continue;

      combos.Add(new BmpOptimizationCombo(colorMode, compression, rowOrder));
    }

    return combos.Distinct().ToArray();
  }

  private byte[]? _TestCombination(BmpOptimizationCombo combo) {
    try {
      var (pixelData, palette, paletteColorCount) = this._ConvertPixels(combo.ColorMode);

      return BmpWriter.Assemble(
        pixelData, this._width, this._height,
        combo.ColorMode, combo.Compression, combo.RowOrder,
        palette, paletteColorCount
      );
    } catch {
      return null;
    }
  }

  private (byte[] pixelData, byte[]? palette, int paletteColorCount) _ConvertPixels(BmpColorMode mode) {
    switch (mode) {
      case BmpColorMode.Rgb24:
      case BmpColorMode.Original: {
        var rgb = new byte[this._width * this._height * 3];
        for (var i = 0; i < this._width * this._height; ++i) {
          var srcIdx = i * 4;
          var dstIdx = i * 3;
          rgb[dstIdx] = this._argbPixelData[srcIdx + 2];     // B
          rgb[dstIdx + 1] = this._argbPixelData[srcIdx + 1]; // G
          rgb[dstIdx + 2] = this._argbPixelData[srcIdx];     // R
        }

        return (rgb, null, 0);
      }
      case BmpColorMode.Rgb16_565: {
        var data = new byte[this._width * this._height * 2];
        for (var i = 0; i < this._width * this._height; ++i) {
          var srcIdx = i * 4;
          var r = this._argbPixelData[srcIdx];
          var g = this._argbPixelData[srcIdx + 1];
          var b = this._argbPixelData[srcIdx + 2];
          var pixel = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
          data[i * 2] = (byte)(pixel & 0xFF);
          data[i * 2 + 1] = (byte)(pixel >> 8);
        }

        return (data, null, 0);
      }
      case BmpColorMode.Palette8:
      case BmpColorMode.Palette4:
      case BmpColorMode.Palette1: {
        return this._BuildPalette(mode);
      }
      case BmpColorMode.Grayscale8: {
        var grayData = new byte[this._width * this._height];
        var grayPalette = new byte[256 * 3];
        for (var i = 0; i < 256; ++i) {
          grayPalette[i * 3] = (byte)i;
          grayPalette[i * 3 + 1] = (byte)i;
          grayPalette[i * 3 + 2] = (byte)i;
        }

        for (var i = 0; i < this._width * this._height; ++i) {
          var srcIdx = i * 4;
          grayData[i] = (byte)(0.299 * this._argbPixelData[srcIdx] +
                               0.587 * this._argbPixelData[srcIdx + 1] +
                               0.114 * this._argbPixelData[srcIdx + 2]);
        }

        return (grayData, grayPalette, 256);
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
  }

  private (byte[] pixelData, byte[] palette, int paletteColorCount) _BuildPalette(BmpColorMode mode) {
    var maxColors = mode switch {
      BmpColorMode.Palette1 => 2,
      BmpColorMode.Palette4 => 16,
      _ => 256
    };

    var colors = new Dictionary<int, byte>();
    var pixelCount = this._width * this._height;
    var indexData = new byte[pixelCount];
    var paletteBytes = new byte[maxColors * 3];
    var colorIndex = 0;

    // Build palette and index data
    for (var i = 0; i < pixelCount; ++i) {
      var srcIdx = i * 4;
      var r = this._argbPixelData[srcIdx];
      var g = this._argbPixelData[srcIdx + 1];
      var b = this._argbPixelData[srcIdx + 2];
      var key = (r << 16) | (g << 8) | b;

      if (!colors.TryGetValue(key, out var idx)) {
        if (colorIndex >= maxColors)
          throw new InvalidOperationException($"Image has more than {maxColors} unique colors.");

        idx = (byte)colorIndex;
        colors[key] = idx;
        paletteBytes[colorIndex * 3] = r;
        paletteBytes[colorIndex * 3 + 1] = g;
        paletteBytes[colorIndex * 3 + 2] = b;
        ++colorIndex;
      }

      indexData[i] = idx;
    }

    // Frequency sort
    var frequency = new int[colorIndex];
    for (var i = 0; i < pixelCount; ++i)
      ++frequency[indexData[i]];

    var sortOrder = new int[colorIndex];
    for (var i = 0; i < colorIndex; ++i)
      sortOrder[i] = i;
    Array.Sort(sortOrder, (a, b) => frequency[b].CompareTo(frequency[a]));

    var remap = new byte[colorIndex];
    for (var i = 0; i < colorIndex; ++i)
      remap[sortOrder[i]] = (byte)i;

    var sortedPalette = new byte[maxColors * 3];
    for (var i = 0; i < colorIndex; ++i) {
      var oldIdx = sortOrder[i];
      sortedPalette[i * 3] = paletteBytes[oldIdx * 3];
      sortedPalette[i * 3 + 1] = paletteBytes[oldIdx * 3 + 1];
      sortedPalette[i * 3 + 2] = paletteBytes[oldIdx * 3 + 2];
    }

    for (var i = 0; i < pixelCount; ++i)
      indexData[i] = remap[indexData[i]];

    // Pack bits for sub-byte modes
    byte[] packedData;
    switch (mode) {
      case BmpColorMode.Palette1: {
        var bytesPerRow = (this._width + 7) / 8;
        packedData = new byte[bytesPerRow * this._height];
        for (var y = 0; y < this._height; ++y)
        for (var x = 0; x < this._width; ++x) {
          if (indexData[y * this._width + x] != 0)
            packedData[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
        }

        break;
      }
      case BmpColorMode.Palette4: {
        var bytesPerRow = (this._width + 1) / 2;
        packedData = new byte[bytesPerRow * this._height];
        for (var y = 0; y < this._height; ++y)
        for (var x = 0; x < this._width; ++x) {
          var byteIdx = y * bytesPerRow + x / 2;
          if (x % 2 == 0)
            packedData[byteIdx] |= (byte)(indexData[y * this._width + x] << 4);
          else
            packedData[byteIdx] |= (byte)(indexData[y * this._width + x] & 0x0F);
        }

        break;
      }
      default:
        packedData = indexData;
        break;
    }

    return (packedData, sortedPalette, colorIndex);
  }

  private static void _ExtractPixelData(
    Bitmap image,
    out byte[] argbPixelData,
    out bool isGrayscale,
    out int uniqueColors
  ) {
    var width = image.Width;
    var height = image.Height;
    argbPixelData = new byte[width * height * 4]; // RGBA order: R, G, B, A

    var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
      PixelFormat.Format32bppArgb);
    try {
      var colorSet = new HashSet<int>();
      isGrayscale = true;

      unsafe {
        for (var y = 0; y < height; ++y) {
          var row = (byte*)data.Scan0 + y * data.Stride;
          for (var x = 0; x < width; ++x) {
            var b = row[x * 4];
            var g = row[x * 4 + 1];
            var r = row[x * 4 + 2];
            var dstIdx = (y * width + x) * 4;
            argbPixelData[dstIdx] = r;
            argbPixelData[dstIdx + 1] = g;
            argbPixelData[dstIdx + 2] = b;
            argbPixelData[dstIdx + 3] = 255;
            colorSet.Add((r << 16) | (g << 8) | b);
            if (r != g || g != b)
              isGrayscale = false;
          }
        }
      }

      uniqueColors = colorSet.Count;
    } finally {
      image.UnlockBits(data);
    }
  }
}
