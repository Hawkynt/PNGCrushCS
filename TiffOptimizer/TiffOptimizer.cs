using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitMiracle.LibTiff.Classic;

namespace TiffOptimizer;

public sealed class TiffOptimizer {
  private readonly int _bitsPerSample;
  private readonly byte[]? _colorMap;
  private readonly int _height;
  private readonly bool _isGrayscale;
  private readonly TiffOptimizationOptions _options;
  private readonly ushort _photometric;

  private readonly byte[] _pixelData;
  private readonly int _samplesPerPixel;
  private readonly bool _skipPackBits;
  private readonly int _uniqueColors;
  private readonly int _width;

  public TiffOptimizer(Bitmap image, TiffOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new TiffOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._pixelData, out this._samplesPerPixel, out this._bitsPerSample,
      out this._photometric,
      out this._isGrayscale, out this._uniqueColors, out this._colorMap);

    // Estimate PackBits effectiveness; skip if ratio > 0.95 (saves < 5%)
    this._skipPackBits = PackBitsCompressor.EstimateCompressionRatio(this._pixelData) > 0.95;
  }

  public static TiffOptimizer FromFile(FileInfo file, TiffOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TIFF file not found.", file.FullName);

    using var bmp = new Bitmap(file.FullName);
    return new TiffOptimizer(bmp, options);
  }

  public async ValueTask<TiffOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<TiffOptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();

    // Determine if two-phase optimization applies
    var hasExpensive = this._options.EnableTwoPhaseOptimization &&
                       combos.Any(c =>
                         c.Compression is TiffCompression.DeflateUltra or TiffCompression.DeflateHyper);

    List<(TiffOptimizationCombo combo, TiffOptimizationResult result)>? phase1Results = null;
    TiffOptimizationCombo[] finalCombos;
    if (hasExpensive) {
      // Phase 1: replace expensive methods with Deflate
      var phase1Combos = combos.Select(c =>
        c.Compression is TiffCompression.DeflateUltra or TiffCompression.DeflateHyper
          ? c with { Compression = TiffCompression.Deflate }
          : c
      ).Distinct().ToArray();

      phase1Results = await this._RunCombos(phase1Combos, cancellationToken, progress, "Screening");
      cancellationToken.ThrowIfCancellationRequested();
      var topKeys = phase1Results
        .OrderBy(r => r.result.CompressedSize)
        .Take(this._options.Phase2CandidateCount)
        .Select(r => (r.combo.Predictor, r.combo.ColorMode, r.combo.StripRowCount, r.combo.TileWidth,
          r.combo.TileHeight))
        .ToHashSet();

      var cheap = combos.Where(c =>
        c.Compression is not (TiffCompression.DeflateUltra or TiffCompression.DeflateHyper)).ToList();
      var expensive = combos.Where(c =>
        c.Compression is TiffCompression.DeflateUltra or TiffCompression.DeflateHyper &&
        topKeys.Contains((c.Predictor, c.ColorMode, c.StripRowCount, c.TileWidth, c.TileHeight))
      ).ToList();

      finalCombos = [.. cheap, .. expensive];
    } else {
      finalCombos = combos;
    }

    var allResults = await this._RunCombos(finalCombos, cancellationToken, progress);

    // Include Phase 1 screening results so fast compression is always a candidate
    if (phase1Results != null)
      allResults.AddRange(phase1Results);

    if (allResults.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return allResults.MinBy(r => r.result.CompressedSize).result;
  }

  private async ValueTask<List<(TiffOptimizationCombo combo, TiffOptimizationResult result)>> _RunCombos(
    TiffOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<TiffOptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(TiffOptimizationCombo combo, TiffOptimizationResult result)>();
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

        var optimizationResult = new TiffOptimizationResult(
          combo.Compression,
          combo.Predictor,
          combo.ColorMode,
          combo.StripRowCount,
          result.Length,
          sw.Elapsed,
          result,
          combo.TileWidth,
          combo.TileHeight
        );

        lock (resultsLock) {
          results.Add((combo, optimizationResult));
          if (optimizationResult.CompressedSize < bestSize)
            bestSize = optimizationResult.CompressedSize;
        }

        var done = Interlocked.Increment(ref completedCount);
        progress?.Report(new TiffOptimizationProgress(done, combos.Length, bestSize, phase));
      } finally {
        semaphore.Release();
      }
    }));

    await Task.WhenAll(tasks);
    progress?.Report(new TiffOptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));
    return results;
  }

  private TiffOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<TiffOptimizationCombo>();

    var colorModes = new List<TiffColorMode> { TiffColorMode.Original };
    if (this._options.AutoSelectColorMode) {
      if (!this._isGrayscale)
        colorModes.Add(TiffColorMode.Grayscale);
      if (this._uniqueColors <= 256)
        colorModes.Add(TiffColorMode.Palette);
    }

    var stripSizes = this._options.DynamicStripSizing
      ? _GenerateStripSizes(this._height)
      : this._options.StripRowCounts;

    foreach (var compression in this._options.Compressions)
    foreach (var predictor in this._options.Predictors)
    foreach (var colorMode in colorModes) {
      // Prune invalid combinations
      if (predictor == TiffPredictor.HorizontalDifferencing &&
          compression is TiffCompression.None or TiffCompression.PackBits)
        continue;

      // Skip PackBits when estimated to be ineffective (only if other compressions are available)
      if (this._skipPackBits && compression == TiffCompression.PackBits && this._options.Compressions.Count > 1)
        continue;

      // Strip-based combos
      foreach (var stripRows in stripSizes)
        combos.Add(new TiffOptimizationCombo(compression, predictor, colorMode, stripRows));

      // Tile-based combos
      if (this._options.TryTiles)
        foreach (var tileSize in _GenerateTileSizes(this._width, this._height, this._options.TileSizes))
          combos.Add(new TiffOptimizationCombo(compression, predictor, colorMode, 0, tileSize, tileSize));
    }

    return combos.ToArray();
  }

  /// <summary>Generate valid tile sizes (multiples of 16 per TIFF spec) that fit within image dimensions</summary>
  internal static List<int> _GenerateTileSizes(int width, int height, List<int> candidates) {
    var minDimension = Math.Min(width, height);
    var result = new List<int>();
    foreach (var size in candidates) {
      if (size < 16 || size % 16 != 0)
        continue;

      if (size <= minDimension)
        result.Add(size);
    }

    // Always include at least one valid tile size if the image is large enough
    if (result.Count == 0 && minDimension >= 16)
      result.Add(16);

    return result;
  }

  /// <summary>Generate strip sizes from powers of 2 and integer factors of image height</summary>
  internal static List<int> _GenerateStripSizes(int imageHeight) {
    var sizes = new HashSet<int>();

    // Powers of 2 up to image height
    for (var p = 1; p <= imageHeight; p <<= 1)
      sizes.Add(p);

    // Integer factors of image height
    for (var f = 1; f * f <= imageHeight; ++f) {
      if (imageHeight % f != 0)
        continue;

      sizes.Add(f);
      sizes.Add(imageHeight / f);
    }

    var result = new List<int>(sizes);
    result.Sort();
    return result;
  }

  private byte[]? _TestCombination(TiffOptimizationCombo combo) {
    try {
      var pixelData = this._pixelData;
      var samples = this._samplesPerPixel;
      var bits = this._bitsPerSample;
      var photometric = this._photometric;
      var colorMap = this._colorMap;

      // Convert color mode if needed
      if (combo.ColorMode != TiffColorMode.Original)
        (pixelData, samples, bits, photometric, colorMap) = this._ConvertColorMode(combo.ColorMode);

      return TiffAssembler.Assemble(
        pixelData, this._width, this._height,
        samples, bits,
        combo.Compression, combo.Predictor,
        combo.StripRowCount, this._options.ZopfliIterations,
        photometric, colorMap,
        combo.TileWidth, combo.TileHeight
      );
    } catch {
      return null;
    }
  }

  private (byte[] pixelData, int samples, int bits, ushort photometric, byte[]? colorMap) _ConvertColorMode(
    TiffColorMode mode) {
    switch (mode) {
      case TiffColorMode.Grayscale: {
        var grayData = new byte[this._width * this._height];
        var bytesPerPixel = this._samplesPerPixel;
        for (var i = 0; i < grayData.Length; ++i) {
          var srcIdx = i * bytesPerPixel;
          if (srcIdx + 2 < this._pixelData.Length)
            grayData[i] = (byte)(0.299 * this._pixelData[srcIdx] + 0.587 * this._pixelData[srcIdx + 1] +
                                 0.114 * this._pixelData[srcIdx + 2]);
          else if (srcIdx < this._pixelData.Length)
            grayData[i] = this._pixelData[srcIdx];
        }

        return (grayData, 1, 8, (ushort)Photometric.MINISBLACK, null);
      }
      case TiffColorMode.Palette: {
        // Build palette from pixel data
        var colors = new Dictionary<int, byte>();
        var pixelCount = this._width * this._height;
        var paletteData = new byte[pixelCount];
        var colorMapBytes = new byte[256 * 3];
        var colorIndex = 0;
        var bytesPerPixel = this._samplesPerPixel;

        for (var i = 0; i < pixelCount; ++i) {
          var srcIdx = i * bytesPerPixel;
          int key;
          byte r, g, b;
          if (srcIdx + 2 < this._pixelData.Length) {
            r = this._pixelData[srcIdx];
            g = this._pixelData[srcIdx + 1];
            b = this._pixelData[srcIdx + 2];
            key = (r << 16) | (g << 8) | b;
          } else {
            r = g = b = 0;
            key = 0;
          }

          if (!colors.TryGetValue(key, out var idx)) {
            if (colorIndex >= 256)
              return (this._pixelData, this._samplesPerPixel, this._bitsPerSample, this._photometric, this._colorMap);

            idx = (byte)colorIndex;
            colors[key] = idx;
            colorMapBytes[colorIndex * 3] = r;
            colorMapBytes[colorIndex * 3 + 1] = g;
            colorMapBytes[colorIndex * 3 + 2] = b;
            ++colorIndex;
          }

          paletteData[i] = idx;
        }

        // Frequency sort: reorder palette so most-used indices come first
        var frequency = new int[colorIndex];
        for (var i = 0; i < pixelCount; ++i)
          ++frequency[paletteData[i]];

        // Build sorted-index-to-old-index mapping
        var sortOrder = new int[colorIndex];
        for (var i = 0; i < colorIndex; ++i)
          sortOrder[i] = i;
        Array.Sort(sortOrder, (a, b) => frequency[b].CompareTo(frequency[a]));

        // Build old-to-new remap
        var remap = new byte[colorIndex];
        for (var i = 0; i < colorIndex; ++i)
          remap[sortOrder[i]] = (byte)i;

        // Reorder color map
        var sortedColorMap = new byte[256 * 3];
        for (var i = 0; i < colorIndex; ++i) {
          var oldIdx = sortOrder[i];
          sortedColorMap[i * 3] = colorMapBytes[oldIdx * 3];
          sortedColorMap[i * 3 + 1] = colorMapBytes[oldIdx * 3 + 1];
          sortedColorMap[i * 3 + 2] = colorMapBytes[oldIdx * 3 + 2];
        }

        // Remap pixel indices
        for (var i = 0; i < pixelCount; ++i)
          paletteData[i] = remap[paletteData[i]];

        return (paletteData, 1, 8, (ushort)Photometric.PALETTE, sortedColorMap);
      }
      default:
        return (this._pixelData, this._samplesPerPixel, this._bitsPerSample, this._photometric, this._colorMap);
    }
  }

  private static void _ExtractPixelData(
    Bitmap image,
    out byte[] pixelData,
    out int samplesPerPixel,
    out int bitsPerSample,
    out ushort photometric,
    out bool isGrayscale,
    out int uniqueColors,
    out byte[]? colorMap
  ) {
    var width = image.Width;
    var height = image.Height;
    colorMap = null;

    if (image.PixelFormat == PixelFormat.Format8bppIndexed) {
      samplesPerPixel = 1;
      bitsPerSample = 8;
      photometric = (ushort)Photometric.PALETTE;
      pixelData = new byte[width * height];

      var palette = image.Palette.Entries;
      colorMap = new byte[palette.Length * 3];
      for (var i = 0; i < palette.Length; ++i) {
        colorMap[i * 3] = palette[i].R;
        colorMap[i * 3 + 1] = palette[i].G;
        colorMap[i * 3 + 2] = palette[i].B;
      }

      var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
        PixelFormat.Format8bppIndexed);
      try {
        unsafe {
          for (var y = 0; y < height; ++y) {
            var row = (byte*)data.Scan0 + y * data.Stride;
            Array.Copy(new ReadOnlySpan<byte>(row, width).ToArray(), 0, pixelData, y * width, width);
          }
        }
      } finally {
        image.UnlockBits(data);
      }

      // Count unique colors
      var colorSet = new HashSet<int>();
      foreach (var idx in pixelData)
        if (idx < palette.Length)
          colorSet.Add(palette[idx].ToArgb());
      uniqueColors = colorSet.Count;
      isGrayscale = palette.All(c => c.R == c.G && c.G == c.B);
    } else {
      samplesPerPixel = 3;
      bitsPerSample = 8;
      photometric = (ushort)Photometric.RGB;
      pixelData = new byte[width * height * 3];

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
              var dstIdx = (y * width + x) * 3;
              pixelData[dstIdx] = r;
              pixelData[dstIdx + 1] = g;
              pixelData[dstIdx + 2] = b;
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
}
