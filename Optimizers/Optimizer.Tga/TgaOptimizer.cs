using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.Tga;

namespace Optimizer.Tga;

public sealed class TgaOptimizer {
  private readonly byte[] _argbPixelData;
  private readonly bool _hasAlpha;
  private readonly int _height;
  private readonly bool _isGrayscale;
  private readonly TgaOptimizationOptions _options;
  private readonly int _uniqueColors;
  private readonly int _width;

  public TgaOptimizer(Bitmap image, TgaOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new TgaOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._argbPixelData, out this._isGrayscale, out this._uniqueColors,
      out this._hasAlpha);
  }

  public static TgaOptimizer FromFile(FileInfo file, TgaOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TGA file not found.", file.FullName);

    using var bmp = new Bitmap(file.FullName);
    return new TgaOptimizer(bmp, options);
  }

  public async ValueTask<TgaOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();
    var results = await this._RunCombos(combos, cancellationToken, progress);

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return results.MinBy(r => r.result.CompressedSize).result;
  }

  private async ValueTask<List<(TgaOptimizationCombo combo, TgaOptimizationResult result)>> _RunCombos(
    TgaOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(TgaOptimizationCombo combo, TgaOptimizationResult result)>();
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

        var optimizationResult = new TgaOptimizationResult(
          combo.ColorMode,
          combo.Compression,
          combo.Origin,
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

  private TgaOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<TgaOptimizationCombo>();

    var colorModes = new List<TgaColorMode>(this._options.ColorModes);
    if (this._options.AutoSelectColorMode) {
      if (this._hasAlpha && !colorModes.Contains(TgaColorMode.Rgba32))
        colorModes.Add(TgaColorMode.Rgba32);
      if (!colorModes.Contains(TgaColorMode.Rgb24))
        colorModes.Add(TgaColorMode.Rgb24);
      if (this._isGrayscale && !colorModes.Contains(TgaColorMode.Grayscale8))
        colorModes.Add(TgaColorMode.Grayscale8);
      if (this._uniqueColors <= 256 && !colorModes.Contains(TgaColorMode.Indexed8))
        colorModes.Add(TgaColorMode.Indexed8);
    }

    foreach (var colorMode in colorModes)
    foreach (var compression in this._options.Compressions)
    foreach (var origin in this._options.Origins) {
      // Prune: Indexed8 only when <= 256 colors
      if (colorMode == TgaColorMode.Indexed8 && this._uniqueColors > 256)
        continue;

      // Prune: Grayscale8 only when grayscale
      if (colorMode == TgaColorMode.Grayscale8 && !this._isGrayscale)
        continue;

      combos.Add(new TgaOptimizationCombo(colorMode, compression, origin));
    }

    return combos.Distinct().ToArray();
  }

  private byte[]? _TestCombination(TgaOptimizationCombo combo) {
    try {
      var (pixelData, palette, paletteColorCount) = this._ConvertPixels(combo.ColorMode);

      return TgaWriter.Assemble(
        pixelData, this._width, this._height,
        combo.ColorMode, combo.Compression, combo.Origin,
        palette, paletteColorCount
      );
    } catch {
      return null;
    }
  }

  private (byte[] pixelData, byte[]? palette, int paletteColorCount) _ConvertPixels(TgaColorMode mode) {
    var pixelCount = this._width * this._height;

    switch (mode) {
      case TgaColorMode.Rgba32: {
        // TGA BGRA format
        var bgra = new byte[pixelCount * 4];
        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * 4;
          bgra[i * 4] = this._argbPixelData[srcIdx + 2];     // B
          bgra[i * 4 + 1] = this._argbPixelData[srcIdx + 1]; // G
          bgra[i * 4 + 2] = this._argbPixelData[srcIdx];     // R
          bgra[i * 4 + 3] = this._argbPixelData[srcIdx + 3]; // A
        }

        return (bgra, null, 0);
      }
      case TgaColorMode.Rgb24:
      case TgaColorMode.Original: {
        // TGA BGR format
        var bgr = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * 4;
          bgr[i * 3] = this._argbPixelData[srcIdx + 2];     // B
          bgr[i * 3 + 1] = this._argbPixelData[srcIdx + 1]; // G
          bgr[i * 3 + 2] = this._argbPixelData[srcIdx];     // R
        }

        return (bgr, null, 0);
      }
      case TgaColorMode.Grayscale8: {
        var gray = new byte[pixelCount];
        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * 4;
          gray[i] = (byte)(0.299 * this._argbPixelData[srcIdx] +
                           0.587 * this._argbPixelData[srcIdx + 1] +
                           0.114 * this._argbPixelData[srcIdx + 2]);
        }

        return (gray, null, 0);
      }
      case TgaColorMode.Indexed8: {
        return this._BuildPalette();
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
  }

  private (byte[] pixelData, byte[] palette, int paletteColorCount) _BuildPalette() {
    var pixelCount = this._width * this._height;
    var colors = new Dictionary<int, byte>();
    var indexData = new byte[pixelCount];
    var paletteBytes = new byte[256 * 3];
    var colorIndex = 0;

    for (var i = 0; i < pixelCount; ++i) {
      var srcIdx = i * 4;
      var r = this._argbPixelData[srcIdx];
      var g = this._argbPixelData[srcIdx + 1];
      var b = this._argbPixelData[srcIdx + 2];
      var key = (r << 16) | (g << 8) | b;

      if (!colors.TryGetValue(key, out var idx)) {
        if (colorIndex >= 256)
          throw new InvalidOperationException("Image has more than 256 unique colors.");

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

    var sortedPalette = new byte[256 * 3];
    for (var i = 0; i < colorIndex; ++i) {
      var oldIdx = sortOrder[i];
      sortedPalette[i * 3] = paletteBytes[oldIdx * 3];
      sortedPalette[i * 3 + 1] = paletteBytes[oldIdx * 3 + 1];
      sortedPalette[i * 3 + 2] = paletteBytes[oldIdx * 3 + 2];
    }

    for (var i = 0; i < pixelCount; ++i)
      indexData[i] = remap[indexData[i]];

    return (indexData, sortedPalette, colorIndex);
  }

  private static void _ExtractPixelData(
    Bitmap image,
    out byte[] argbPixelData,
    out bool isGrayscale,
    out int uniqueColors,
    out bool hasAlpha
  ) {
    var width = image.Width;
    var height = image.Height;
    argbPixelData = new byte[width * height * 4]; // R, G, B, A

    var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
      PixelFormat.Format32bppArgb);
    try {
      var colorSet = new HashSet<int>();
      isGrayscale = true;
      hasAlpha = false;

      unsafe {
        for (var y = 0; y < height; ++y) {
          var row = (byte*)data.Scan0 + y * data.Stride;
          for (var x = 0; x < width; ++x) {
            var b = row[x * 4];
            var g = row[x * 4 + 1];
            var r = row[x * 4 + 2];
            var a = row[x * 4 + 3];
            var dstIdx = (y * width + x) * 4;
            argbPixelData[dstIdx] = r;
            argbPixelData[dstIdx + 1] = g;
            argbPixelData[dstIdx + 2] = b;
            argbPixelData[dstIdx + 3] = a;
            colorSet.Add((r << 16) | (g << 8) | b);
            if (r != g || g != b)
              isGrayscale = false;
            if (a != 255)
              hasAlpha = true;
          }
        }
      }

      uniqueColors = colorSet.Count;
    } finally {
      image.UnlockBits(data);
    }
  }
}
