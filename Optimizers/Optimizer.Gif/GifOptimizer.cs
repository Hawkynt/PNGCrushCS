using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.Gif;
using FileFormat.Core;

namespace Optimizer.Gif;

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
    IProgress<OptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();

    // Two-phase screening: every combo is first tried on the screening LZW mode alone, and the other
    // modes are then run only against the handful of combos that screened best. That keeps each extra
    // LZW mode worth Phase2CandidateCount trials instead of multiplying the whole cartesian product,
    // which is what lets a third mode be on by default.
    var hasModesToScreen = this._options.EnableTwoPhaseOptimization &&
                           combos.Any(c => c.LzwMode != _ScreeningLzwMode);

    List<(GifOptimizationCombo combo, GifOptimizationResult result)>? phase1Results = null;
    GifOptimizationCombo[] finalCombos;
    if (hasModesToScreen) {
      // Phase 1: test every combo on the screening mode only.
      var phase1Combos = combos
        .Select(c => c.LzwMode == _ScreeningLzwMode ? c : c with { LzwMode = _ScreeningLzwMode })
        .Distinct()
        .ToArray();

      phase1Results = await this._RunCombos(phase1Combos, cancellationToken, progress, "Screening");
      cancellationToken.ThrowIfCancellationRequested();
      var topKeys = phase1Results
        .OrderBy(r => r.result.CompressedSize)
        .Take(this._options.Phase2CandidateCount)
        .Select(r => _ComboKey(r.combo))
        .ToHashSet();

      // Phase 2: every screening-mode combo, plus every other mode against the combos that screened best.
      var screened = combos.Where(c => c.LzwMode == _ScreeningLzwMode).ToList();
      var promoted = combos
        .Where(c => c.LzwMode != _ScreeningLzwMode && topKeys.Contains(_ComboKey(c)))
        .ToList();

      finalCombos = [.. screened, .. promoted];
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
    IProgress<OptimizationProgress>? progress = null, string phase = "Optimizing") {
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
        progress?.Report(new OptimizationProgress(done, combos.Length, bestSize, phase));
      } finally {
        semaphore.Release();
      }
    }));

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));
    return results;
  }

  /// <summary>The LZW mode every combo is screened on. It has to be the mode that is never
  /// catastrophic, so that a combo losing the screening round is genuinely unpromising rather than
  /// just unlucky in its dictionary strategy.</summary>
  private const LzwMode _ScreeningLzwMode = LzwMode.Standard;

  /// <summary>Everything about a combo except its LZW mode — the identity phase 1 ranks and phase 2
  /// promotes, so that screening a combo once covers every LZW mode of it.</summary>
  private static (PaletteReorderStrategy PaletteStrategy, bool UseGlobalColorTable, bool OptimizeDisposal,
    bool TrimTransparentMargins, bool ComputeFrameDiffs, bool CompressionAwareDisposal) _ComboKey(
    GifOptimizationCombo combo)
    => (combo.PaletteStrategy, combo.UseGlobalColorTable, combo.OptimizeDisposal,
      combo.TrimTransparentMargins, combo.ComputeFrameDiffs, combo.CompressionAwareDisposal);

  /// <summary>Maps a trial mode onto the codec's dictionary-full strategy. Internal rather than
  /// private because it is the one place a new mode can be wired to the wrong strategy and still
  /// produce plausible-looking output, so it is asserted directly rather than through file sizes.</summary>
  internal static GifLzwCodec.ClearStrategy ToClearStrategy(LzwMode mode) => mode switch {
    LzwMode.DeferredClear => GifLzwCodec.ClearStrategy.Adaptive,
    LzwMode.FrozenDictionary => GifLzwCodec.ClearStrategy.Freeze,
    _ => GifLzwCodec.ClearStrategy.Immediate,
  };

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

    var lzwModes = new List<LzwMode> { _ScreeningLzwMode };
    if (this._options.TryDeferredClear)
      lzwModes.Add(LzwMode.DeferredClear);
    if (this._options.TryFrozenDictionary)
      lzwModes.Add(LzwMode.FrozenDictionary);

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

      var gctColors = PaletteAdapter.ToColors(this._gif.GlobalColorTable);
      for (var i = 0; i < frameCount; ++i) {
        var frame = frames != null ? frames[i] : this._gif.Frames[i];
        var palette = frame.LocalColorTable != null ? PaletteAdapter.ToColors(frame.LocalColorTable) : gctColors;

        if (palette.Length == 0)
          return null;

        var pixels = frame.IndexedPixels;
        var position = frame.Position;
        var size = frame.Size;
        var disposal = optimizedDisposals != null ? optimizedDisposals[i] : frame.DisposalMethod;
        var transparentIndex = frame.TransparentColorIndex;
        Rgba32[]? localColorTable;

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

        // LZW compress. The codec frames the bitstream into GIF sub-blocks and prefixes the LZW
        // minimum code size, so what comes back is the complete image-data block.
        var compressed = GifLzwCodec.Encode(pixels, 8,
          GifLzwCodec.EncodeOptions.StandardCompression(ToClearStrategy(combo.LzwMode)));

        assembledFrames[i] = new AssembledFrame {
          CompressedData = compressed,
          Size = size,
          Position = position,
          LocalColorTable = localColorTable,
          Delay = frame.Delay,
          DisposalMethod = disposal,
          TransparentColorIndex = transparentIndex
        };
      }

      var assembled = new AssembledGif {
        LogicalScreenSize = new Dimensions(this._gif.LogicalScreenDescriptor.Width, this._gif.LogicalScreenDescriptor.Height),
        BackgroundColorIndex = this._gif.LogicalScreenDescriptor.BackgroundColorIndex,
        GlobalColorTable = globalColorTable,
        LoopCount = this._gif.LoopCount,
        Frames = assembledFrames
      };

      return GifAssembler.Assemble(assembled);
    } catch {
      return null;
    }
  }

  private static byte[] _BuildGctRemap(Rgba32[] source, Rgba32[] target) {
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
