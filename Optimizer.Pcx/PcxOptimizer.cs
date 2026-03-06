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
using FileFormat.Pcx;

namespace Optimizer.Pcx;

public sealed class PcxOptimizer {
  private readonly byte[] _argbPixelData;
  private readonly int _height;
  private readonly bool _isGrayscale;
  private readonly PcxOptimizationOptions _options;
  private readonly int _uniqueColors;
  private readonly int _width;

  public PcxOptimizer(Bitmap image, PcxOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new PcxOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._argbPixelData, out this._isGrayscale, out this._uniqueColors);
  }

  public static PcxOptimizer FromFile(FileInfo file, PcxOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCX file not found.", file.FullName);

    using var bmp = new Bitmap(file.FullName);
    return new PcxOptimizer(bmp, options);
  }

  public async ValueTask<PcxOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();
    var results = await this._RunCombos(combos, cancellationToken, progress);

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return results.MinBy(r => r.result.CompressedSize).result;
  }

  private async ValueTask<List<(PcxOptimizationCombo combo, PcxOptimizationResult result)>> _RunCombos(
    PcxOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(PcxOptimizationCombo combo, PcxOptimizationResult result)>();
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

        var optimizationResult = new PcxOptimizationResult(
          combo.ColorMode,
          combo.PlaneConfig,
          combo.PaletteOrder,
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

  private PcxOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<PcxOptimizationCombo>();

    var colorModes = new List<PcxColorMode>(this._options.ColorModes);
    if (this._options.AutoSelectColorMode) {
      if (!colorModes.Contains(PcxColorMode.Rgb24))
        colorModes.Add(PcxColorMode.Rgb24);
      if (this._uniqueColors <= 256 && !colorModes.Contains(PcxColorMode.Indexed8))
        colorModes.Add(PcxColorMode.Indexed8);
      if (this._uniqueColors <= 16 && !colorModes.Contains(PcxColorMode.Indexed4))
        colorModes.Add(PcxColorMode.Indexed4);
      if (this._uniqueColors <= 2 && !colorModes.Contains(PcxColorMode.Monochrome))
        colorModes.Add(PcxColorMode.Monochrome);
    }

    foreach (var colorMode in colorModes)
    foreach (var planeConfig in this._options.PlaneConfigs)
    foreach (var paletteOrder in this._options.PaletteOrders) {
      // SeparatePlanes only meaningful for RGB24
      if (planeConfig == PcxPlaneConfig.SeparatePlanes && colorMode != PcxColorMode.Rgb24)
        continue;

      // Palette ordering only for indexed modes
      if (paletteOrder == PcxPaletteOrder.FrequencySorted &&
          colorMode is not (PcxColorMode.Indexed8 or PcxColorMode.Indexed4 or PcxColorMode.Monochrome))
        continue;

      // Indexed modes need enough colors
      if (colorMode == PcxColorMode.Indexed8 && this._uniqueColors > 256)
        continue;
      if (colorMode == PcxColorMode.Indexed4 && this._uniqueColors > 16)
        continue;
      if (colorMode == PcxColorMode.Monochrome && this._uniqueColors > 2)
        continue;

      combos.Add(new PcxOptimizationCombo(colorMode, planeConfig, paletteOrder));
    }

    return combos.Distinct().ToArray();
  }

  private byte[]? _TestCombination(PcxOptimizationCombo combo) {
    try {
      var (pixelData, palette, paletteColorCount) = this._ConvertPixels(combo.ColorMode, combo.PaletteOrder);

      return PcxWriter.Assemble(
        pixelData, this._width, this._height,
        combo.ColorMode, combo.PlaneConfig,
        palette, paletteColorCount
      );
    } catch {
      return null;
    }
  }

  private (byte[] pixelData, byte[]? palette, int paletteColorCount) _ConvertPixels(PcxColorMode mode,
    PcxPaletteOrder order) {
    var pixelCount = this._width * this._height;

    switch (mode) {
      case PcxColorMode.Rgb24:
      case PcxColorMode.Original: {
        var rgb = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * 4;
          rgb[i * 3] = this._argbPixelData[srcIdx];       // R
          rgb[i * 3 + 1] = this._argbPixelData[srcIdx + 1]; // G
          rgb[i * 3 + 2] = this._argbPixelData[srcIdx + 2]; // B
        }

        return (rgb, null, 0);
      }
      case PcxColorMode.Indexed8:
      case PcxColorMode.Indexed4:
      case PcxColorMode.Monochrome: {
        return this._BuildPalette(mode, order);
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
  }

  private (byte[] pixelData, byte[] palette, int paletteColorCount) _BuildPalette(PcxColorMode mode,
    PcxPaletteOrder order) {
    var maxColors = mode switch {
      PcxColorMode.Monochrome => 2,
      PcxColorMode.Indexed4 => 16,
      _ => 256
    };

    var pixelCount = this._width * this._height;
    var colors = new Dictionary<int, byte>();
    var indexData = new byte[pixelCount];
    var paletteBytes = new byte[maxColors * 3];
    var colorIndex = 0;

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

    // Frequency sort if requested
    if (order == PcxPaletteOrder.FrequencySorted) {
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

      paletteBytes = sortedPalette;
    }

    // Pack bits for sub-byte modes
    byte[] packedData;
    switch (mode) {
      case PcxColorMode.Monochrome: {
        var bytesPerRow = (this._width + 7) / 8;
        packedData = new byte[bytesPerRow * this._height];
        for (var y = 0; y < this._height; ++y)
        for (var x = 0; x < this._width; ++x) {
          if (indexData[y * this._width + x] != 0)
            packedData[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
        }

        break;
      }
      case PcxColorMode.Indexed4: {
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

    return (packedData, paletteBytes, colorIndex);
  }

  private static void _ExtractPixelData(
    Bitmap image,
    out byte[] argbPixelData,
    out bool isGrayscale,
    out int uniqueColors
  ) {
    var width = image.Width;
    var height = image.Height;
    argbPixelData = new byte[width * height * 4];

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
