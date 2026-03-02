using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hawkynt.GifFileFormat;

namespace GifOptimizer;

public sealed class GifOptimizer {
  private readonly GifFile _gif;
  private readonly GifOptimizationOptions _options;

  public GifOptimizer(GifFile gif, GifOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(gif);
    this._options = options ?? new GifOptimizationOptions();
    this._gif = this._options.DeduplicateFrames ? GifFrameOptimizer.DeduplicateFrames(gif) : gif;
  }

  public static GifOptimizer FromFile(FileInfo file, GifOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GIF file not found.", file.FullName);

    GifFile gif;
    try {
      gif = Reader.FromFile(file);
    } catch (Exception ex) when (ex is not FileNotFoundException) {
      throw new InvalidOperationException($"Failed to read GIF file '{file.FullName}': {ex.Message}", ex);
    }

    return new GifOptimizer(gif, options);
  }

  public async ValueTask<GifOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<GifOptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();

    // Determine if two-phase optimization applies
    var hasDeferred = this._options.EnableTwoPhaseOptimization &&
                      combos.Any(c => c.LzwMode == LzwMode.DeferredClear);

    List<(GifOptimizationCombo combo, GifOptimizationResult result)>? phase1Results = null;
    GifOptimizationCombo[] finalCombos;
    if (hasDeferred) {
      // Phase 1: test all combos with Standard only
      var phase1Combos = combos.Select(c =>
        c.LzwMode == LzwMode.DeferredClear ? c with { LzwMode = LzwMode.Standard } : c
      ).Distinct().ToArray();

      phase1Results = await this._RunCombos(phase1Combos, cancellationToken, progress, "Screening");
      cancellationToken.ThrowIfCancellationRequested();
      var topKeys = phase1Results
        .OrderBy(r => r.result.CompressedSize)
        .Take(this._options.Phase2CandidateCount)
        .Select(r => (r.combo.PaletteStrategy, r.combo.UseGlobalColorTable, r.combo.OptimizeDisposal,
          r.combo.TrimTransparentMargins, r.combo.ComputeFrameDiffs, r.combo.CompressionAwareDisposal))
        .ToHashSet();

      var standard = combos.Where(c => c.LzwMode == LzwMode.Standard).ToList();
      var deferred = combos.Where(c =>
        c.LzwMode == LzwMode.DeferredClear &&
        topKeys.Contains((c.PaletteStrategy, c.UseGlobalColorTable, c.OptimizeDisposal,
          c.TrimTransparentMargins, c.ComputeFrameDiffs, c.CompressionAwareDisposal))
      ).ToList();

      finalCombos = [.. standard, .. deferred];
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

  private async ValueTask<List<(GifOptimizationCombo combo, GifOptimizationResult result)>> _RunCombos(
    GifOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<GifOptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(GifOptimizationCombo combo, GifOptimizationResult result)>();
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

        var optimizationResult = new GifOptimizationResult(
          combo.PaletteStrategy,
          combo.UseGlobalColorTable,
          result.Length, this._gif.Frames.Count,
          sw.Elapsed,
          result
        );

        lock (resultsLock) {
          results.Add((combo, optimizationResult));
          if (optimizationResult.CompressedSize < bestSize)
            bestSize = optimizationResult.CompressedSize;
        }

        var done = Interlocked.Increment(ref completedCount);
        progress?.Report(new GifOptimizationProgress(done, combos.Length, bestSize, phase));
      } finally {
        semaphore.Release();
      }
    }));

    await Task.WhenAll(tasks);
    progress?.Report(new GifOptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));
    return results;
  }

  private GifOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<GifOptimizationCombo>();

    var colorTableModes = new List<bool>();
    if (this._options.TryGlobalColorTable && GifFrameOptimizer.TryBuildGlobalColorTable(this._gif) != null)
      colorTableModes.Add(true);
    if (this._options.TryLocalColorTable)
      colorTableModes.Add(false);
    if (colorTableModes.Count == 0)
      colorTableModes.Add(false);

    var disposalModes = new List<(bool optimize, bool compressionAware)> { (false, false) };
    if (this._options.OptimizeDisposal && this._gif.Frames.Count > 1)
      disposalModes.Add((true, false));
    if (this._options.TryCompressionAwareDisposal && this._gif.Frames.Count > 1)
      disposalModes.Add((true, true));

    var trimModes = new List<bool> { false };
    if (this._options.TrimMargins && this._gif.Frames.Any(f => f.TransparentColorIndex.HasValue))
      trimModes.Add(true);

    var lzwModes = new List<LzwMode> { LzwMode.Standard };
    if (this._options.TryDeferredClear)
      lzwModes.Add(LzwMode.DeferredClear);

    var frameDiffModes = new List<bool> { false };
    if (this._options.TryFrameDifferencing && this._gif.Frames.Count > 1)
      frameDiffModes.Add(true);

    foreach (var strategy in this._options.PaletteStrategies)
    foreach (var useGct in colorTableModes)
    foreach (var (optimizeDisposal, compressionAwareDisposal) in disposalModes)
    foreach (var trimMargins in trimModes)
    foreach (var lzwMode in lzwModes)
    foreach (var frameDiffs in frameDiffModes)
      combos.Add(new GifOptimizationCombo(strategy, useGct, optimizeDisposal, trimMargins, lzwMode, frameDiffs,
        compressionAwareDisposal));

    return combos.ToArray();
  }

  private byte[]? _TestCombination(GifOptimizationCombo combo) {
    try {
      // Apply frame differencing if requested
      var frames = combo.ComputeFrameDiffs && this._gif.Frames.Count > 1
        ? GifFrameDifferencer.ComputeDiffs(this._gif)
        : null;

      var globalColorTable = combo.UseGlobalColorTable
        ? GifFrameOptimizer.TryBuildGlobalColorTable(this._gif)
        : null;

      var optimizedDisposals = combo.CompressionAwareDisposal
        ? GifFrameOptimizer.OptimizeDisposalMethodsByCompression(this._gif)
        : combo.OptimizeDisposal
          ? GifFrameOptimizer.OptimizeDisposalMethods(this._gif)
          : null;

      var frameCount = this._gif.Frames.Count;
      var assembledFrames = new AssembledFrame[frameCount];

      for (var i = 0; i < frameCount; ++i) {
        var frame = frames != null ? frames[i] : this._gif.Frames[i];
        var palette = frame.LocalColorTable ?? this._gif.GlobalColorTable;

        if (palette == null)
          return null;

        var pixels = frame.IndexedPixels;
        var position = frame.Position;
        var size = frame.Size;
        var disposal = optimizedDisposals != null ? optimizedDisposals[i] : frame.DisposalMethod;
        var transparentIndex = frame.TransparentColorIndex;
        Color[]? localColorTable;

        // Apply palette reordering
        if (combo.PaletteStrategy != PaletteReorderStrategy.Original) {
          var (newPalette, remapTable) = PaletteReorderer.Reorder(palette, pixels, combo.PaletteStrategy);
          pixels = PaletteReorderer.ApplyRemap(pixels, remapTable);
          palette = newPalette;

          if (transparentIndex.HasValue)
            transparentIndex = remapTable[transparentIndex.Value];
        }

        // Trim margins
        if (combo.TrimTransparentMargins)
          (pixels, position, size) =
            GifFrameOptimizer.TrimTransparentMargins(pixels, size, position, transparentIndex);

        // Determine color table for this frame
        if (combo.UseGlobalColorTable && globalColorTable != null) {
          // Need to remap pixels to global color table indices
          if (palette != globalColorTable) {
            var gctRemap = _BuildGctRemap(palette, globalColorTable);
            pixels = PaletteReorderer.ApplyRemap(pixels, gctRemap);
            if (transparentIndex.HasValue)
              transparentIndex = gctRemap[transparentIndex.Value];
          }

          localColorTable = null;
        } else {
          localColorTable = palette;
        }

        // LZW compress
        var compressed = LzwCompressor.Compress(pixels, 8, combo.LzwMode == LzwMode.DeferredClear);

        assembledFrames[i] = new AssembledFrame {
          CompressedData = compressed,
          Size = size,
          Position = position,
          LocalColorTable = localColorTable,
          Delay = frame.Delay,
          DisposalMethod = disposal,
          TransparentColorIndex = transparentIndex,
          BitsPerPixel = 8
        };
      }

      var assembled = new AssembledGif {
        LogicalScreenSize = this._gif.LogicalScreenSize,
        BackgroundColorIndex = this._gif.BackgroundColorIndex,
        GlobalColorTable = globalColorTable,
        LoopCount = this._gif.LoopCount,
        Frames = assembledFrames
      };

      return GifAssembler.Assemble(assembled);
    } catch {
      return null;
    }
  }

  private static byte[] _BuildGctRemap(Color[] source, Color[] target) {
    var remap = new byte[source.Length];
    for (var i = 0; i < source.Length; ++i) {
      var best = 0;
      var bestDist = int.MaxValue;
      var sc = source[i];
      for (var j = 0; j < target.Length; ++j) {
        var tc = target[j];
        var dr = sc.R - tc.R;
        var dg = sc.G - tc.G;
        var db = sc.B - tc.B;
        var dist = dr * dr + dg * dg + db * db;
        if (dist >= bestDist)
          continue;

        bestDist = dist;
        best = j;
        if (dist == 0) break;
      }

      remap[i] = (byte)best;
    }

    return remap;
  }
}
