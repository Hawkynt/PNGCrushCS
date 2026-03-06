using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Compression.Core;
using Crush.Core;
using Hawkynt.ColorProcessing;
using Hawkynt.ColorProcessing.Dithering;
using Hawkynt.ColorProcessing.Quantization;
using Hawkynt.Drawing;
using FileFormat.Png;

namespace Optimizer.Png;

/// <summary>Main class for PNG optimization</summary>
public sealed partial class PngOptimizer {
  private readonly ArgbPixel[] _imageBitmapPixelData;
  private readonly int _imageHeight;
  private readonly ImageStats _imageStats;
  private readonly int _imageWidth;
  private readonly PngOptimizationOptions _options;
  private readonly PngChunkReader? _preservedChunks;
  private readonly Bitmap _sourceImage;

  /// <summary>Constructor for the PNG optimizer</summary>
  public PngOptimizer(Bitmap image, PngOptimizationOptions? options = null)
    : this(image, null, options) { }

  /// <summary>Constructor with optional original PNG bytes for chunk preservation</summary>
  public PngOptimizer(Bitmap image, byte[]? originalPngBytes, PngOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new PngOptimizationOptions();
    this._sourceImage = image;
    this._imageWidth = image.Width;
    this._imageHeight = image.Height;
    (this._imageStats, this._imageBitmapPixelData) = _ExtractImageData(image);

    if (this._options.PreserveAncillaryChunks && originalPngBytes != null)
      this._preservedChunks = PngChunkReader.Parse(originalPngBytes);
  }

  /// <summary>Optimize the PNG image and return the best result</summary>
  public async ValueTask<OptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null) {
    var stopwatch = Stopwatch.StartNew();
    Console.WriteLine("Starting PNG optimization...");

    var combinations = this._GenerateCombinations();
    Console.WriteLine($"Testing {combinations.Length} optimization combinations");

    // Pre-compute pixel data once per (ColorMode, BitDepth, LossyPaletteCombo) group
    var conversionCache =
      new Dictionary<(PngColorType, int, QuantizerDithererCombo?), (byte[][] imageData, byte[]? palette, byte[]? tRNS
        , int paletteCount)>();
    foreach (var combo in combinations) {
      var key = (combo.ColorMode, combo.BitDepth, combo.LossyPaletteCombo);
      if (conversionCache.ContainsKey(key))
        continue;

      var data = this._ConvertPixelData(combo.ColorMode, combo.BitDepth, combo.LossyPaletteCombo, out var palette,
        out var tRNS, out var paletteCount);
      conversionCache[key] = (data, palette, tRNS, paletteCount);
    }

    // Pre-compute Adam7 sub-images for interlaced combos
    var adam7Cache = new Dictionary<(PngColorType, int, QuantizerDithererCombo?), byte[][][]>();
    var needsAdam7 = combinations.Any(c => c.InterlaceMethod == PngInterlaceMethod.Adam7);
    if (needsAdam7)
      foreach (var combo in combinations) {
        if (combo.InterlaceMethod != PngInterlaceMethod.Adam7)
          continue;

        var key = (combo.ColorMode, combo.BitDepth, combo.LossyPaletteCombo);
        if (adam7Cache.ContainsKey(key))
          continue;

        var (fullImageData, _, _, _) = conversionCache[key];
        adam7Cache[key] = _ExtractAdam7SubImages(fullImageData, this._imageWidth, this._imageHeight, combo.ColorMode,
          combo.BitDepth);
      }

    // Determine if two-phase optimization applies
    var hasExpensive = this._options.EnableTwoPhaseOptimization &&
                       combinations.Any(c => c.DeflateMethod is DeflateMethod.Ultra or DeflateMethod.Hyper);

    List<(OptimizationCombo combo, OptimizationResult result)>? phase1Results = null;
    OptimizationCombo[] phase2Combos;
    if (hasExpensive) {
      // Phase 1: replace expensive methods with Maximum (ZLibStream SmallestSize)
      var phase1Combos = combinations.Select(c =>
        c.DeflateMethod is DeflateMethod.Ultra or DeflateMethod.Hyper
          ? c with { DeflateMethod = DeflateMethod.Maximum }
          : c
      ).Distinct().ToArray();

      Console.WriteLine(
        $"Two-phase optimization: Phase 1 screening {phase1Combos.Length} combos with fast compression");
      phase1Results = await this._RunCombos(phase1Combos, conversionCache, adam7Cache, cancellationToken, progress,
        "Screening");
      cancellationToken.ThrowIfCancellationRequested();

      // Rank by size and take top N, then restore their original expensive methods
      var topResults = phase1Results
        .OrderBy(r => r.result.CompressedSize)
        .Take(this._options.Phase2CandidateCount)
        .ToList();

      // Build phase 2: non-expensive combos stay as-is, expensive combos only for top N candidates
      var cheapCombos = combinations
        .Where(c => c.DeflateMethod is not (DeflateMethod.Ultra or DeflateMethod.Hyper)).ToList();
      var expensiveCandidates =
        new HashSet<(PngColorType, int, PngInterlaceMethod, FilterStrategy, QuantizerDithererCombo?)>(
          topResults.Select(r => (r.combo.ColorMode, r.combo.BitDepth, r.combo.InterlaceMethod,
            r.combo.FilterStrategy, r.combo.LossyPaletteCombo))
        );

      var expensiveCombos = combinations
        .Where(c => c.DeflateMethod is DeflateMethod.Ultra or DeflateMethod.Hyper &&
                    expensiveCandidates.Contains((c.ColorMode, c.BitDepth, c.InterlaceMethod, c.FilterStrategy,
                      c.LossyPaletteCombo)))
        .ToList();

      Console.WriteLine(
        $"Phase 2: re-testing {expensiveCombos.Count} expensive combos from top {topResults.Count} candidates");
      phase2Combos = [.. cheapCombos, .. expensiveCombos];
    } else {
      phase2Combos = combinations;
    }

    var allResults = await this._RunCombos(phase2Combos, conversionCache, adam7Cache, cancellationToken, progress);

    // Include Phase 1 screening results so fast compression is always a candidate
    if (phase1Results != null)
      allResults.AddRange(phase1Results);

    var bestResult = allResults.MinBy(r => r.result.CompressedSize).result;

    stopwatch.Stop();
    Console.WriteLine($"Optimization completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");
    Console.WriteLine($"Best result: {bestResult}");

    return bestResult;
  }

  /// <summary>Run a set of optimization combos in parallel and return all results</summary>
  private async ValueTask<List<(OptimizationCombo combo, OptimizationResult result)>> _RunCombos(
    OptimizationCombo[] combos,
    Dictionary<(PngColorType, int, QuantizerDithererCombo?), (byte[][] imageData, byte[]? palette, byte[]? tRNS, int
      paletteCount)> conversionCache,
    Dictionary<(PngColorType, int, QuantizerDithererCombo?), byte[][][]> adam7Cache,
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null,
    string phase = "Optimizing") {
    var results = new List<(OptimizationCombo combo, OptimizationResult result)>();
    var resultsLock = new object();
    var completedCount = 0;
    var bestSize = long.MaxValue;

    using var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);
    var tasks = combos.Select(async combo => {
      await semaphore.WaitAsync(cancellationToken);
      try {
        var (cachedImageData, cachedPalette, cachedTRNS, cachedPaletteCount) =
          conversionCache[(combo.ColorMode, combo.BitDepth, combo.LossyPaletteCombo)];
        byte[][][]? cachedAdam7SubImages = null;
        if (combo.InterlaceMethod == PngInterlaceMethod.Adam7)
          cachedAdam7SubImages = adam7Cache[(combo.ColorMode, combo.BitDepth, combo.LossyPaletteCombo)];

        var combinationResult = this._TestCombinationWithData(combo, cachedImageData, cachedPalette, cachedTRNS,
          cachedPaletteCount, cachedAdam7SubImages);
        Console.WriteLine($"""
                           Found Optimization Result:
                             Color Mode       : {combinationResult.ColorMode}
                             Bit Depth        : {combinationResult.BitDepth}
                             Interlace Method : {combinationResult.InterlaceMethod}
                             Filter Strategy  : {combinationResult.FilterStrategy}
                             Deflate Method   : {combinationResult.DeflateMethod}
                             Compressed Size  : {combinationResult.CompressedSize} bytes
                             Filter Transitions: {combinationResult.FilterTransitions}
                           """);

        lock (resultsLock) {
          results.Add((combo, combinationResult));
          if (combinationResult.CompressedSize < bestSize)
            bestSize = combinationResult.CompressedSize;
        }

        var done = Interlocked.Increment(ref completedCount);
        progress?.Report(new OptimizationProgress(done, combos.Length, bestSize, phase));
      } finally {
        semaphore.Release();
      }
    }).ToArray();

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));
    return results;
  }

  /// <summary>Extract pixel data and image statistics from bitmap</summary>
  private static unsafe (ImageStats, ArgbPixel[]) _ExtractImageData(Bitmap image) {
    var uniqueArgbColors = new HashSet<uint>();
    var uniqueRgbColors = new HashSet<uint>();
    var hasAlpha = false;
    var isGrayscale = true;

    var width = image.Width;
    var height = image.Height;
    var result = new ArgbPixel[width * height];

    var bmpData = image.LockBits(
      new Rectangle(0, 0, width, height),
      ImageLockMode.ReadOnly,
      PixelFormat.Format32bppArgb);

    try {
      var stride = bmpData.Stride;
      var scan0 = bmpData.Scan0;

      fixed (ArgbPixel* resultPtr = result) {
        var rowOffset = (byte*)(void*)scan0;
        var destPtr = resultPtr;

        for (var y = 0; y < height; ++y, rowOffset += stride) {
          var currentRowOffset = rowOffset;
          for (var x = 0; x < width; ++x, currentRowOffset += 4, ++destPtr) {
            var pixelValue = *(ArgbPixel*)currentRowOffset;
            var rawValue = *(uint*)currentRowOffset;
            uniqueArgbColors.Add(rawValue);
            uniqueRgbColors.Add(rawValue & 0x00FFFFFFu);

            *destPtr = pixelValue;
            hasAlpha |= pixelValue.A < 255;
            isGrayscale &= pixelValue.R == pixelValue.G && pixelValue.G == pixelValue.B;
          }
        }
      }
    } finally {
      image.UnlockBits(bmpData);
    }

    // Detect binary transparency: all alpha values are 0 or 255, all transparent
    // pixels share the same RGB, and that RGB is not used by any opaque pixel
    (byte R, byte G, byte B)? transparentKeyColor = null;
    if (hasAlpha) {
      var isBinaryAlpha = true;
      var transparentRgb = -1L;
      var opaqueRgbSet = new HashSet<long>();

      foreach (var pixel in result) {
        if (pixel.A != 0 && pixel.A != 255) {
          isBinaryAlpha = false;
          break;
        }

        if (pixel.A == 0) {
          var rgb = ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B;
          if (transparentRgb < 0) {
            transparentRgb = rgb;
          } else if (transparentRgb != rgb) {
            isBinaryAlpha = false;
            break;
          }
        } else {
          opaqueRgbSet.Add(((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B);
        }
      }

      if (isBinaryAlpha && transparentRgb >= 0 && !opaqueRgbSet.Contains(transparentRgb))
        transparentKeyColor = ((byte)(transparentRgb >> 16), (byte)(transparentRgb >> 8), (byte)transparentRgb);
    }

    // Detect grayscale transparent key: when image is grayscale with binary alpha and
    // all transparent pixels share the same gray value not used by any opaque pixel
    byte? transparentKeyGray = null;
    if (hasAlpha && isGrayscale && transparentKeyColor.HasValue)
      transparentKeyGray = transparentKeyColor.Value.R;

    var stats = new ImageStats(uniqueRgbColors.Count, uniqueArgbColors.Count, hasAlpha, isGrayscale,
      transparentKeyColor, transparentKeyGray);
    Console.WriteLine(
      $"Image analysis: {uniqueRgbColors.Count} unique RGB colors, {uniqueArgbColors.Count} unique ARGB colors, Alpha: {hasAlpha}, Grayscale: {isGrayscale}");

    return (stats, result);
  }

  /// <summary>Generate all optimization combinations based on image stats and options</summary>
  private OptimizationCombo[] _GenerateCombinations() {
    var colorModesToTry = new List<(PngColorType colorMode, int bitDepth)>();

    if (this._options.AutoSelectColorMode) {
      var (uniqueColors, uniqueArgbColors, hasAlpha, isGrayscale, _, transparentKeyGray) = this._imageStats;

      if (isGrayscale) {
        if (hasAlpha) {
          colorModesToTry.Add((PngColorType.GrayscaleAlpha, 8));

          // If binary transparency detected with single gray key, also try Grayscale + tRNS
          if (transparentKeyGray.HasValue)
            colorModesToTry.Add((PngColorType.Grayscale, 8));
        } else {
          colorModesToTry.Add((PngColorType.Grayscale, 8));
          if (uniqueColors <= 16)
            colorModesToTry.Add((PngColorType.Grayscale, 4));
          if (uniqueColors <= 4)
            colorModesToTry.Add((PngColorType.Grayscale, 2));
          if (uniqueColors <= 2)
            colorModesToTry.Add((PngColorType.Grayscale, 1));
        }
      } else {
        colorModesToTry.Add(hasAlpha ? (PngColorType.RGBA, 8) : (PngColorType.RGB, 8));

        // If binary transparency detected, also try RGB + tRNS key color
        if (hasAlpha && this._imageStats.TransparentKeyColor.HasValue)
          colorModesToTry.Add((PngColorType.RGB, 8));

        // Use ARGB count for palette eligibility when alpha is present (tRNS support)
        var paletteColorCount = hasAlpha ? uniqueArgbColors : uniqueColors;
        if (paletteColorCount <= this._options.MaxPaletteColors) {
          colorModesToTry.Add((PngColorType.Palette, 8));
          if (paletteColorCount <= 16)
            colorModesToTry.Add((PngColorType.Palette, 4));
          if (paletteColorCount <= 4)
            colorModesToTry.Add((PngColorType.Palette, 2));
          if (paletteColorCount <= 2)
            colorModesToTry.Add((PngColorType.Palette, 1));
        } else if (this._options.AllowLossyPalette || this._options.UseDithering) {
          // Lossy palette: quantize to MaxPaletteColors
          colorModesToTry.Add((PngColorType.Palette, 8));
        }
      }
    } else {
      colorModesToTry.Add(this._imageStats.HasAlpha
        ? (PngColorType.RGBA, 8)
        : (PngColorType.RGB, 8));
    }

    // Build ditherer combo list for lossy palette modes
    var ditheringCombos = new List<QuantizerDithererCombo?> { null }; // null = use built-in quantizer
    if (this._options.UseDithering)
      foreach (var qName in this._options.QuantizerNames)
      foreach (var dName in this._options.DithererNames)
        ditheringCombos.Add(new QuantizerDithererCombo(qName, dName));

    return (
      from colorModeInfo in colorModesToTry
      from interlaceMethod in this._options.TryInterlacing
        ? new[] { PngInterlaceMethod.None, PngInterlaceMethod.Adam7 }
        : [PngInterlaceMethod.None]
      from filterStrategy in this._options.FilterStrategies
      where colorModeInfo.colorMode != PngColorType.Palette || colorModeInfo.bitDepth >= 8 ||
            filterStrategy is FilterStrategy.SingleFilter or FilterStrategy.BruteForce
              or FilterStrategy.BruteForceAdaptive
      where colorModeInfo.colorMode != PngColorType.Grayscale || colorModeInfo.bitDepth >= 8 ||
            filterStrategy is FilterStrategy.SingleFilter or FilterStrategy.BruteForce
              or FilterStrategy.BruteForceAdaptive
      from deflateMethod in this._options.DeflateMethods
      from lossyCombo in colorModeInfo.colorMode == PngColorType.Palette &&
                         this._imageStats.UniqueArgbColors > 1 << colorModeInfo.bitDepth
        ? ditheringCombos
        : [null]
      select new OptimizationCombo(
        colorModeInfo.colorMode,
        colorModeInfo.bitDepth,
        interlaceMethod,
        filterStrategy,
        deflateMethod,
        lossyCombo)
    ).ToArray();
  }

  /// <summary>Test a single optimization combination with pre-computed pixel data</summary>
  private OptimizationResult _TestCombinationWithData(OptimizationCombo combo, byte[][] imageData, byte[]? palette,
    byte[]? tRNS, int paletteCount, byte[][][]? adam7SubImages = null) {
    var sw = Stopwatch.StartNew();

    PngFilterType[] filters;
    byte[][] filteredData;

    var filterStride = GetFilterStride(combo.ColorMode, combo.BitDepth);

    if (combo.InterlaceMethod == PngInterlaceMethod.Adam7 && adam7SubImages != null) {
      var allFilters = new List<PngFilterType>();
      var allFilteredData = new List<byte[]>();

      for (var pass = 0; pass < Adam7.PassCount; ++pass) {
        var subImage = adam7SubImages[pass];
        if (subImage.Length == 0)
          continue;

        var (subW, subH) = Adam7.GetPassDimensions(pass, this._imageWidth, this._imageHeight);
        var (subFilters, subFilteredData) = this._FilterImageData(subImage, subW, subH, filterStride, combo);
        allFilters.AddRange(subFilters);
        allFilteredData.AddRange(subFilteredData);
      }

      filters = allFilters.ToArray();
      filteredData = allFilteredData.ToArray();
    } else {
      (filters, filteredData) =
        this._FilterImageData(imageData, this._imageWidth, this._imageHeight, filterStride, combo);
    }

    var filterTransitions = _CountFilterTransitions(filteredData);
    var bytes = this._CompressData(filteredData, combo.DeflateMethod, combo.InterlaceMethod, combo.ColorMode,
      combo.BitDepth, palette, tRNS, paletteCount);

    sw.Stop();

    return new OptimizationResult(
      combo.ColorMode,
      combo.BitDepth,
      combo.InterlaceMethod,
      combo.FilterStrategy,
      combo.DeflateMethod,
      bytes.Length,
      filters,
      filterTransitions,
      sw.Elapsed,
      bytes,
      combo.LossyPaletteCombo
    );
  }

  /// <summary>Filter image data using the specified combo's filter strategy</summary>
  private (PngFilterType[] filters, byte[][] filteredData) _FilterImageData(byte[][] imageData, int width, int height,
    int filterStride, OptimizationCombo combo) {
    if (combo.FilterStrategy == FilterStrategy.PartitionOptimized && this._options.TryPartitioning) {
      var partitioner = new ImagePartitioner(
        imageData,
        height,
        filterStride,
        combo.ColorMode == PngColorType.Palette,
        combo.ColorMode is PngColorType.Grayscale or PngColorType.GrayscaleAlpha,
        combo.BitDepth
      );

      return partitioner.OptimizePartitions();
    }

    var filterOptimizer = new PngFilterOptimizer(
      width,
      height,
      filterStride,
      combo.ColorMode is PngColorType.Grayscale or PngColorType.GrayscaleAlpha,
      combo.ColorMode == PngColorType.Palette,
      combo.BitDepth,
      imageData
    );

    var filters = filterOptimizer.OptimizeFilters(combo.FilterStrategy);
    var filteredData = FilterTools.ApplyFilters(imageData, filters, filterStride);
    return (filters, filteredData);
  }

  /// <summary>Convert ARGB pixel data to the target color mode scanlines</summary>
  private byte[][] _ConvertPixelData(PngColorType colorMode, int bitDepth, QuantizerDithererCombo? lossyCombo,
    out byte[]? palette, out byte[]? tRNS, out int paletteCount) {
    palette = null;
    tRNS = null;
    paletteCount = 0;
    var width = this._imageWidth;
    var height = this._imageHeight;
    var filterStride = GetFilterStride(colorMode, bitDepth);
    var bitsPerPixel = bitDepth * GetSamplesPerPixel(colorMode);
    var bytesPerScanline = (width * bitsPerPixel + 7) >> 3;

    var result = new byte[height][];
    for (var y = 0; y < height; ++y)
      result[y] = new byte[bytesPerScanline];

    if (colorMode == PngColorType.Palette && lossyCombo.HasValue)
      this._QuantizeWithFrameworkExtensions(width, height, 1 << bitDepth, this._imageStats.HasAlpha, lossyCombo.Value,
        out palette, out tRNS, out paletteCount, result);
    else if (colorMode == PngColorType.Palette)
      this._QuantizePixelData(width, height, 1 << bitDepth, this._imageStats.HasAlpha, out palette, out tRNS,
        out paletteCount, result);
    else if (colorMode == PngColorType.Grayscale && bitDepth < 8)
      this._ConvertSubByteGrayscale(width, height, bitDepth, result);
    else
      this._ConvertDirectPixelData(width, height, filterStride, colorMode, result);

    // Set grayscale tRNS for binary transparency
    if (colorMode == PngColorType.Grayscale && this._imageStats.TransparentKeyGray is { } grayKey)
      tRNS = [(byte)(grayKey >> 8), grayKey]; // 2-byte big-endian gray sample value

    // Set RGB tRNS for binary transparency
    if (colorMode == PngColorType.RGB && this._imageStats.TransparentKeyColor is var (tkR2, tkG2, tkB2) && this._imageStats.HasAlpha) {
      tRNS = new byte[6];
      tRNS[0] = 0; tRNS[1] = tkR2;
      tRNS[2] = 0; tRNS[3] = tkG2;
      tRNS[4] = 0; tRNS[5] = tkB2;
    }

    return result;
  }

  /// <summary>Convert to sub-byte grayscale (1/2/4 bit) with bit packing</summary>
  private void _ConvertSubByteGrayscale(int width, int height, int bitDepth, byte[][] result) {
    var pixelsPerByte = 8 / bitDepth;
    var maxValue = (1 << bitDepth) - 1;
    var sourceOffset = 0;

    for (var y = 0; y < height; ++y) {
      var scanline = result[y];
      for (var x = 0; x < width; x += pixelsPerByte) {
        byte packed = 0;

        for (var bit = 0; bit < pixelsPerByte && x + bit < width; ++bit) {
          var pixel = this._imageBitmapPixelData[sourceOffset++];
          var gray = (pixel.R * 77 + pixel.G * 150 + pixel.B * 29 + 128) >> 8;
          var quantized = (gray * maxValue + 127) / 255;
          packed |= (byte)((quantized & maxValue) << (8 - bitDepth * (bit + 1)));
        }

        scanline[x / pixelsPerByte] = packed;
      }
    }
  }

  /// <summary>Direct pixel format conversion for non-palette modes</summary>
  private void _ConvertDirectPixelData(int width, int height, int filterStride, PngColorType colorMode, byte[][] result) {
    var sourceOffset = 0;
    for (var y = 0; y < height; ++y) {
      var scanline = result[y];
      for (var x = 0; x < width; ++x, ++sourceOffset) {
        var pixel = this._imageBitmapPixelData[sourceOffset];
        var b = pixel.B;
        var g = pixel.G;
        var r = pixel.R;
        var a = pixel.A;

        var destIdx = x * filterStride;
        switch (colorMode) {
          case PngColorType.Grayscale:
            if (a == 0 && this._imageStats.TransparentKeyGray is { } tkGray)
              scanline[destIdx] = tkGray;
            else
              scanline[destIdx] = (byte)((r * 77 + g * 150 + b * 29 + 128) >> 8);
            break;

          case PngColorType.GrayscaleAlpha:
            scanline[destIdx] = (byte)((r * 77 + g * 150 + b * 29 + 128) >> 8);
            scanline[destIdx + 1] = a;
            break;

          case PngColorType.RGB:
            if (a == 0 && this._imageStats.TransparentKeyColor is var (tkR, tkG, tkB)) {
              scanline[destIdx] = tkR;
              scanline[destIdx + 1] = tkG;
              scanline[destIdx + 2] = tkB;
            } else {
              scanline[destIdx] = r;
              scanline[destIdx + 1] = g;
              scanline[destIdx + 2] = b;
            }

            break;

          case PngColorType.RGBA:
            scanline[destIdx] = r;
            scanline[destIdx + 1] = g;
            scanline[destIdx + 2] = b;
            scanline[destIdx + 3] = a;
            break;
        }
      }
    }
  }

  /// <summary>Quantize pixel data to palette indices, with optional alpha support via tRNS</summary>
  private void _QuantizePixelData(int width, int height, int maxColors, bool includeAlpha, out byte[] palette,
    out byte[]? tRNS, out int actualPaletteCount, byte[][] result) {
    // Use ARGB key when alpha is included, RGB-only otherwise
    var uniqueColors = new Dictionary<long, int>();
    var paletteRgba = new List<(byte R, byte G, byte B, byte A)>();

    // Build palette from unique colors
    var totalPixels = width * height;
    var scannedAll = true;
    var pixelIndex = 0;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var pixel = this._imageBitmapPixelData[pixelIndex++];
      var colorKey = includeAlpha
        ? ((long)pixel.A << 24) | ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B
        : ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B;

      if (uniqueColors.ContainsKey(colorKey))
        continue;

      if (uniqueColors.Count >= maxColors) {
        scannedAll = false;
        break;
      }

      uniqueColors[colorKey] = paletteRgba.Count;
      paletteRgba.Add((pixel.R, pixel.G, pixel.B, pixel.A));
    }

    // If more unique colors than maxColors and lossy palette is enabled, use median-cut quantizer
    if (!scannedAll && this._options.AllowLossyPalette) {
      this._QuantizeWithMedianCut(width, height, maxColors, includeAlpha, out palette, out tRNS,
        out actualPaletteCount, result);
      return;
    }

    actualPaletteCount = uniqueColors.Count;

    // Frequency-sort with two-tier sorting: non-opaque first (by freq desc), then opaque (by freq desc)
    if (actualPaletteCount > 1)
      _FrequencySortPaletteWithAlpha(this._imageBitmapPixelData, width, height, includeAlpha, uniqueColors,
        paletteRgba);

    // Build output palette (RGB only) and tRNS (alpha values)
    palette = new byte[maxColors * 3];
    for (var i = 0; i < actualPaletteCount; ++i) {
      var (r, g, b, _) = paletteRgba[i];
      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
    }

    // Build tRNS chunk if any entry has non-255 alpha
    tRNS = null;
    if (includeAlpha) {
      // Find last non-opaque entry index
      var lastNonOpaque = -1;
      for (var i = 0; i < actualPaletteCount; ++i)
        if (paletteRgba[i].A < 255)
          lastNonOpaque = i;

      if (lastNonOpaque >= 0) {
        tRNS = new byte[lastNonOpaque + 1];
        for (var i = 0; i <= lastNonOpaque; ++i)
          tRNS[i] = paletteRgba[i].A;
      }
    }

    // Cache for nearest-neighbor lookups of colors not in the palette
    var nearestCache = new Dictionary<long, int>();

    // Assign indices
    var bitDepth = GetBitDepthForColors(maxColors);
    var pixelsPerByte = 8 / bitDepth;
    pixelIndex = 0;
    for (var y = 0; y < height; ++y) {
      var scanline = result[y];

      if (bitDepth == 8)
        for (var x = 0; x < width; ++x) {
          var pixel = this._imageBitmapPixelData[pixelIndex++];
          var colorKey = includeAlpha
            ? ((long)pixel.A << 24) | ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B
            : ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B;
          scanline[x] = (byte)_FindClosestColorLong(uniqueColors, nearestCache, colorKey, palette,
            paletteRgba, actualPaletteCount, includeAlpha);
        }
      else
        for (var x = 0; x < width; x += pixelsPerByte) {
          byte packed = 0;

          for (var bit = 0; bit < pixelsPerByte && x + bit < width; ++bit) {
            var pixel = this._imageBitmapPixelData[pixelIndex++];
            var colorKey = includeAlpha
              ? ((long)pixel.A << 24) | ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B
              : ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B;
            var paletteIdx = _FindClosestColorLong(uniqueColors, nearestCache, colorKey, palette,
              paletteRgba, actualPaletteCount, includeAlpha);

            var mask = (1 << bitDepth) - 1;
            packed |= (byte)((paletteIdx & mask) << (8 - bitDepth * (bit + 1)));
          }

          scanline[x / pixelsPerByte] = packed;
        }
    }

    // Deflate-optimized palette reordering: try alternative orderings, pick the best
    if (this._options.OptimizePaletteOrder && actualPaletteCount > 2) {
      var frequencyOrder = PngPaletteReorderer.IdentityOrder(actualPaletteCount);
      var bestOrder = PngPaletteReorderer.DeflateOptimizedSort(
        paletteRgba, result, width, bitDepth, Math.Max(1, bitDepth / 8), frequencyOrder);

      // Only apply if different from identity (frequency sort is already applied)
      var isDifferent = false;
      for (var i = 0; i < bestOrder.Length; ++i)
        if (bestOrder[i] != i) {
          isDifferent = true;
          break;
        }

      if (isDifferent)
        PngPaletteReorderer.ApplyReorder(result, bestOrder, actualPaletteCount, bitDepth, paletteRgba, palette,
          tRNS, uniqueColors, includeAlpha);
    }
  }

  /// <summary>Quantize pixel data using median-cut algorithm for lossy palette reduction</summary>
  private void _QuantizeWithMedianCut(int width, int height, int maxColors, bool includeAlpha, out byte[] palette,
    out byte[]? tRNS, out int actualPaletteCount, byte[][] result) {
    var quantizer = new MedianCutQuantizer(this._imageBitmapPixelData, width * height, maxColors, includeAlpha);
    var (paletteRgba, actualCount) = quantizer.Quantize();
    actualPaletteCount = actualCount;

    // Build output palette (RGB only)
    palette = new byte[maxColors * 3];
    for (var i = 0; i < actualPaletteCount; ++i) {
      var (r, g, b, _) = paletteRgba[i];
      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
    }

    // Build tRNS chunk if any entry has non-255 alpha
    tRNS = null;
    if (includeAlpha) {
      var lastNonOpaque = -1;
      for (var i = 0; i < actualPaletteCount; ++i)
        if (paletteRgba[i].A < 255)
          lastNonOpaque = i;

      if (lastNonOpaque >= 0) {
        tRNS = new byte[lastNonOpaque + 1];
        for (var i = 0; i <= lastNonOpaque; ++i)
          tRNS[i] = paletteRgba[i].A;
      }
    }

    // Assign palette indices using nearest-neighbor search
    var pixelIndex = 0;
    for (var y = 0; y < height; ++y) {
      var scanline = result[y];
      for (var x = 0; x < width; ++x)
        scanline[x] = (byte)quantizer.FindNearest(this._imageBitmapPixelData[pixelIndex++]);
    }
  }

  /// <summary>Quantize pixel data using FrameworkExtensions quantizer/ditherer pair</summary>
  private void _QuantizeWithFrameworkExtensions(int width, int height, int maxColors, bool includeAlpha,
    QuantizerDithererCombo combo, out byte[] palette, out byte[]? tRNS, out int actualPaletteCount, byte[][] result) {
    using var indexed = _DispatchReduceColors(this._sourceImage, combo.QuantizerName, combo.DithererName, maxColors,
      this._options.IsHighQualityQuantization);

    // Extract palette from indexed bitmap
    var entries = indexed.Palette.Entries;
    actualPaletteCount = Math.Min(entries.Length, maxColors);
    palette = new byte[maxColors * 3];
    var paletteRgba = new List<(byte R, byte G, byte B, byte A)>(actualPaletteCount);
    for (var i = 0; i < actualPaletteCount; ++i) {
      var entry = entries[i];
      palette[i * 3] = entry.R;
      palette[i * 3 + 1] = entry.G;
      palette[i * 3 + 2] = entry.B;
      paletteRgba.Add((entry.R, entry.G, entry.B, entry.A));
    }

    // Build tRNS chunk if any entry has non-255 alpha
    tRNS = null;
    if (includeAlpha) {
      var lastNonOpaque = -1;
      for (var i = 0; i < actualPaletteCount; ++i)
        if (entries[i].A < 255)
          lastNonOpaque = i;

      if (lastNonOpaque >= 0) {
        tRNS = new byte[lastNonOpaque + 1];
        for (var i = 0; i <= lastNonOpaque; ++i)
          tRNS[i] = entries[i].A;
      }
    }

    // Extract pixel indices from indexed bitmap
    var bmpData = indexed.LockBits(
      new Rectangle(0, 0, width, height),
      ImageLockMode.ReadOnly,
      indexed.PixelFormat);
    try {
      unsafe {
        var scan0 = (byte*)bmpData.Scan0;
        var stride = bmpData.Stride;
        for (var y = 0; y < height; ++y) {
          var row = scan0 + y * stride;
          var scanline = result[y];
          for (var x = 0; x < width; ++x)
            scanline[x] = row[x];
        }
      }
    } finally {
      indexed.UnlockBits(bmpData);
    }
  }

  /// <summary>Dispatch to the correct ReduceColors generic instantiation based on quantizer and ditherer names</summary>
  private static Bitmap _DispatchReduceColors(Bitmap source, string quantizerName, string dithererName,
    int colorCount, bool isHighQuality) {
    return quantizerName.ToLowerInvariant() switch {
      "wu" => _WithDitherer(source, new WuQuantizer(), dithererName, colorCount, isHighQuality),
      "octree" => _WithDitherer(source, new OctreeQuantizer(), dithererName, colorCount, isHighQuality),
      "mediancut" => _WithDitherer(source, new Hawkynt.ColorProcessing.Quantization.MedianCutQuantizer(),
        dithererName, colorCount, isHighQuality),
      "neuquant" => _WithDitherer(source, new NeuquantQuantizer(), dithererName, colorCount, isHighQuality),
      "pngquant" => _WithDitherer(source, new PngQuantQuantizer(), dithererName, colorCount, isHighQuality),
      _ => throw new ArgumentException($"Unknown quantizer: {quantizerName}")
    };
  }

  private static Bitmap _WithDitherer<TQ>(Bitmap source, TQ quantizer, string dithererName, int colorCount,
    bool isHighQuality)
    where TQ : struct, IQuantizer {
    return dithererName.ToLowerInvariant() switch {
      "none" => source.ReduceColors<TQ, NoDithering>(quantizer, default, colorCount, isHighQuality),
      "floydsteinberg" => source.ReduceColors(quantizer, ErrorDiffusion.FloydSteinberg,
        colorCount, isHighQuality),
      "atkinson" => source.ReduceColors(quantizer, ErrorDiffusion.Atkinson, colorCount,
        isHighQuality),
      "sierra" => source.ReduceColors(quantizer, ErrorDiffusion.Sierra, colorCount,
        isHighQuality),
      "bayer4x4" => source.ReduceColors(quantizer, OrderedDitherer.Bayer4x4, colorCount,
        isHighQuality),
      _ => throw new ArgumentException($"Unknown ditherer: {dithererName}")
    };
  }

  /// <summary>Sort palette entries: non-opaque first by frequency desc, then opaque by frequency desc</summary>
  private static void _FrequencySortPaletteWithAlpha(ArgbPixel[] pixels, int width, int height, bool includeAlpha,
    Dictionary<long, int> uniqueColors, List<(byte R, byte G, byte B, byte A)> paletteRgba) {
    var paletteCount = paletteRgba.Count;
    var frequencies = new int[paletteCount];
    for (var i = 0; i < width * height; ++i) {
      var pixel = pixels[i];
      var colorKey = includeAlpha
        ? ((long)pixel.A << 24) | ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B
        : ((long)pixel.R << 16) | ((long)pixel.G << 8) | pixel.B;
      if (uniqueColors.TryGetValue(colorKey, out var idx))
        ++frequencies[idx];
    }

    // Two-tier sort: non-opaque first (by freq desc), then opaque (by freq desc)
    var sortedIndices = new int[paletteCount];
    for (var i = 0; i < paletteCount; ++i)
      sortedIndices[i] = i;

    Array.Sort(sortedIndices, (a, b) => {
      var aOpaque = paletteRgba[a].A == 255;
      var bOpaque = paletteRgba[b].A == 255;
      if (aOpaque != bOpaque)
        return aOpaque ? 1 : -1; // non-opaque first

      return frequencies[b].CompareTo(frequencies[a]); // desc frequency
    });

    // Build remap and reorder palette
    var remap = new int[paletteCount];
    var newPalette = new (byte R, byte G, byte B, byte A)[paletteCount];
    for (var newIdx = 0; newIdx < paletteCount; ++newIdx) {
      var oldIdx = sortedIndices[newIdx];
      remap[oldIdx] = newIdx;
      newPalette[newIdx] = paletteRgba[oldIdx];
    }

    // Apply remap to uniqueColors dictionary
    var keys = new List<long>(uniqueColors.Keys);
    foreach (var key in keys)
      uniqueColors[key] = remap[uniqueColors[key]];

    // Update palette list in-place
    for (var i = 0; i < paletteCount; ++i)
      paletteRgba[i] = newPalette[i];
  }

  /// <summary>Find closest palette color with long key (supports ARGB)</summary>
  private static int _FindClosestColorLong(Dictionary<long, int> paletteMap, Dictionary<long, int> nearestCache,
    long colorKey, byte[] palette, List<(byte R, byte G, byte B, byte A)> paletteRgba, int paletteCount,
    bool includeAlpha) {
    if (paletteMap.TryGetValue(colorKey, out var index))
      return index;

    if (nearestCache.TryGetValue(colorKey, out var cached))
      return cached;

    var r = (int)((colorKey >> 16) & 0xFF);
    var g = (int)((colorKey >> 8) & 0xFF);
    var b = (int)(colorKey & 0xFF);
    var a = includeAlpha ? (int)((colorKey >> 24) & 0xFF) : 255;

    var minDistance = int.MaxValue;
    var closestIndex = 0;

    for (var i = 0; i < paletteCount; ++i) {
      var entry = paletteRgba[i];
      var distance = (entry.R - r) * (entry.R - r) + (entry.G - g) * (entry.G - g) +
                     (entry.B - b) * (entry.B - b);
      if (includeAlpha)
        distance += (entry.A - a) * (entry.A - a);

      if (distance >= minDistance)
        continue;

      minDistance = distance;
      closestIndex = i;
    }

    nearestCache[colorKey] = closestIndex;
    return closestIndex;
  }


  /// <summary>Extract Adam7 sub-images from already-converted full-image scanlines</summary>
  private static byte[][][] _ExtractAdam7SubImages(byte[][] fullImageData, int imageWidth, int imageHeight,
    PngColorType colorMode, int bitDepth) {
    var subImages = new byte[Adam7.PassCount][][];
    var samplesPerPixel = GetSamplesPerPixel(colorMode);
    var bitsPerPixel = bitDepth * samplesPerPixel;

    for (var pass = 0; pass < Adam7.PassCount; ++pass) {
      var (subW, subH) = Adam7.GetPassDimensions(pass, imageWidth, imageHeight);
      if (subW == 0 || subH == 0) {
        subImages[pass] = [];
        continue;
      }

      var bytesPerSubScanline = (subW * bitsPerPixel + 7) >> 3;
      var passData = new byte[subH][];

      for (var sy = 0; sy < subH; ++sy) {
        passData[sy] = new byte[bytesPerSubScanline];
        var srcY = Adam7.YStart(pass) + sy * Adam7.YStep(pass);
        var srcScanline = fullImageData[srcY];

        if (bitDepth >= 8) {
          var bytesPerPixel = samplesPerPixel * (bitDepth / 8);
          for (var sx = 0; sx < subW; ++sx) {
            var srcX = Adam7.XStart(pass) + sx * Adam7.XStep(pass);
            Buffer.BlockCopy(srcScanline, srcX * bytesPerPixel, passData[sy], sx * bytesPerPixel,
              bytesPerPixel);
          }
        } else {
          var pixelsPerByte = 8 / bitDepth;
          var mask = (1 << bitDepth) - 1;
          for (var sx = 0; sx < subW; ++sx) {
            var srcX = Adam7.XStart(pass) + sx * Adam7.XStep(pass);
            var srcByteIdx = srcX / pixelsPerByte;
            var srcBitPos = srcX % pixelsPerByte;
            var srcShift = 8 - bitDepth * (srcBitPos + 1);
            var value = (srcScanline[srcByteIdx] >> srcShift) & mask;

            var destByteIdx = sx / pixelsPerByte;
            var destBitPos = sx % pixelsPerByte;
            var destShift = 8 - bitDepth * (destBitPos + 1);
            passData[sy][destByteIdx] |= (byte)((value & mask) << destShift);
          }
        }
      }

      subImages[pass] = passData;
    }

    return subImages;
  }

  /// <summary>Get the minimum bit depth needed for the given color count</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int GetBitDepthForColors(int colorCount) {
    return colorCount switch {
      <= 2 => 1,
      <= 4 => 2,
      <= 16 => 4,
      _ => 8
    };
  }

  /// <summary>Get samples per pixel for a given color mode (PNG spec)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int GetSamplesPerPixel(PngColorType colorMode) {
    return colorMode switch {
      PngColorType.Grayscale => 1,
      PngColorType.GrayscaleAlpha => 2,
      PngColorType.RGB => 3,
      PngColorType.RGBA => 4,
      PngColorType.Palette => 1,
      _ => throw new ArgumentException("Invalid color mode")
    };
  }

  /// <summary>Get filter stride in bytes for a given color mode and bit depth (minimum 1 per PNG spec)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int GetFilterStride(PngColorType colorMode, int bitDepth) {
    return Math.Max(1, GetSamplesPerPixel(colorMode) * bitDepth / 8);
  }

  /// <summary>Count the number of filter type transitions between consecutive scanlines</summary>
  private static int _CountFilterTransitions(byte[][] filteredData) {
    var transitions = 0;
    for (var i = 1; i < filteredData.Length; ++i)
      if (filteredData[i][0] != filteredData[i - 1][0])
        ++transitions;

    return transitions;
  }

  /// <summary>Compress the filtered data into a complete PNG file</summary>
  private byte[] _CompressData(
    byte[][] filteredData,
    DeflateMethod deflateMethod,
    PngInterlaceMethod interlaceMethod,
    PngColorType colorMode,
    int bitDepth,
    byte[]? palette,
    byte[]? tRNS,
    int paletteCount) {
    var totalFilteredLen = 0;
    foreach (var f in filteredData)
      totalFilteredLen += f.Length;

    byte[] compressedIdat;
    using (var idatData = new PooledMemoryStream(1024 + totalFilteredLen)) {
      if (deflateMethod is DeflateMethod.Ultra or DeflateMethod.Hyper) {
        var flatData = new byte[totalFilteredLen];
        var offset = 0;
        foreach (var scanline in filteredData) {
          Buffer.BlockCopy(scanline, 0, flatData, offset, scanline.Length);
          offset += scanline.Length;
        }

        var isHyper = deflateMethod == DeflateMethod.Hyper;
        var compressed = ZopfliDeflater.Compress(flatData, isHyper, this._options.ZopfliIterations);
        idatData.Stream.Write(compressed);
      } else {
        using var deflateStream = _CreateZlibStream(idatData.Stream, deflateMethod);
        foreach (var scanline in filteredData)
          deflateStream.Write(scanline);
      }

      compressedIdat = idatData.AsSpan().ToArray();
    }

    return PngWriter.Assemble(
      this._imageWidth, this._imageHeight, bitDepth, colorMode, interlaceMethod,
      compressedIdat,
      palette, paletteCount, tRNS,
      this._preservedChunks?.BeforePlte,
      this._preservedChunks?.BetweenPlteAndIdat,
      this._preservedChunks?.AfterIdat
    );
  }

  /// <summary>Create a ZLib compression stream with the specified deflate method</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static Stream _CreateZlibStream(Stream baseStream, DeflateMethod method) {
    var level = method switch {
      DeflateMethod.Fastest => CompressionLevel.NoCompression,
      DeflateMethod.Fast => CompressionLevel.Fastest,
      DeflateMethod.Default => CompressionLevel.Optimal,
      DeflateMethod.Maximum => CompressionLevel.SmallestSize,
      _ => throw new ArgumentException($"DeflateMethod {method} uses ZopfliDeflater, not ZLibStream")
    };

    return new ZLibStream(baseStream, level, true);
  }
}
