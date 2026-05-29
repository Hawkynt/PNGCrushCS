using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Ani;
using Crush.Core;
using FileFormat.Ico;

namespace Optimizer.Ani;

/// <summary>Optimizes ANI animated cursor files by trying BMP/PNG entry formats per frame.</summary>
public sealed class AniOptimizer {
  private readonly AniFile _sourceFile;
  private readonly byte[] _sourceBytes;
  private readonly AniOptimizationOptions _options;

  public AniOptimizer(AniFile sourceFile, byte[] sourceBytes, AniOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(sourceFile);
    ArgumentNullException.ThrowIfNull(sourceBytes);
    this._sourceFile = sourceFile;
    this._sourceBytes = sourceBytes;
    this._options = options ?? new AniOptimizationOptions();
  }

  public static AniOptimizer FromFile(FileInfo file, AniOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ANI file not found.", file.FullName);

    var bytes = File.ReadAllBytes(file.FullName);
    var aniFile = AniReader.FromBytes(bytes);
    return new AniOptimizer(aniFile, bytes, options);
  }

  public async ValueTask<AniOptimizationResult> OptimizeAsync(
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null
  ) {
    var combos = this._GenerateCombinations();

    var results = new List<(AniOptimizationCombo combo, AniOptimizationResult result)>();
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

        var optimizationResult = new AniOptimizationResult(
          result.Length,
          sw.Elapsed,
          result,
          combo.EntryFormats
        );

        lock (resultsLock) {
          results.Add((combo, optimizationResult));
          if (optimizationResult.CompressedSize < bestSize)
            bestSize = optimizationResult.CompressedSize;
        }

        var done = Interlocked.Increment(ref completedCount);
        progress?.Report(new OptimizationProgress(done, combos.Length, bestSize, "Optimizing"));
      } finally {
        semaphore.Release();
      }
    }, cancellationToken));

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));

    return results.Count == 0 
      ? throw new InvalidOperationException("No valid optimization result was produced.") 
      : results.MinBy(r => r.result.CompressedSize).result
      ;
  }

  private AniOptimizationCombo[] _GenerateCombinations() {
    // Count total images across all frames
    var totalImages = this._sourceFile.Frames.Sum(f => f.Images.Count);
    if (totalImages == 0)
      return [new AniOptimizationCombo([])];

    var totalCombos = 1 << totalImages;
    var maxCombos = Math.Min(totalCombos, 256);

    var combos = new List<AniOptimizationCombo>();
    for (var i = 0; i < maxCombos; ++i) {
      var formats = new IcoImageFormat[totalImages];
      for (var j = 0; j < totalImages; ++j)
        formats[j] = ((i >> j) & 1) == 0 ? IcoImageFormat.Bmp : IcoImageFormat.Png;
      combos.Add(new AniOptimizationCombo(formats));
    }

    return combos.ToArray();
  }

  private byte[]? _TestCombination(AniOptimizationCombo combo) {
    try {
      var formatIndex = 0;
      var optimizedFrames = new List<IcoFile>();

      foreach (var frame in this._sourceFile.Frames) {
        var images = new List<IcoImage>();
        foreach (var image in frame.Images) {
          var targetFormat = formatIndex < combo.EntryFormats.Length
            ? combo.EntryFormats[formatIndex]
            : image.Format;

          images.Add(new IcoImage {
            Width = image.Width,
            Height = image.Height,
            BitsPerPixel = image.BitsPerPixel,
            Format = targetFormat,
            Data = image.Data
          });
          ++formatIndex;
        }

        optimizedFrames.Add(new IcoFile { Images = images });
      }

      var result = new AniFile {
        Header = this._sourceFile.Header,
        Frames = optimizedFrames,
        Rates = this._sourceFile.Rates,
        Sequence = this._sourceFile.Sequence
      };

      return AniWriter.ToBytes(result);
    } catch {
      return null;
    }
  }
}
