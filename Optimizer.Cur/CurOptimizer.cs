using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.Cur;
using FileFormat.Ico;

namespace Optimizer.Cur;

public sealed class CurOptimizer {
  private readonly CurFile _sourceFile;
  private readonly byte[] _sourceBytes;
  private readonly CurOptimizationOptions _options;

  public CurOptimizer(CurFile sourceFile, byte[] sourceBytes, CurOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(sourceFile);
    ArgumentNullException.ThrowIfNull(sourceBytes);
    this._sourceFile = sourceFile;
    this._sourceBytes = sourceBytes;
    this._options = options ?? new CurOptimizationOptions();
  }

  public static CurOptimizer FromFile(FileInfo file, CurOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CUR file not found.", file.FullName);

    var bytes = File.ReadAllBytes(file.FullName);
    var curFile = CurReader.FromBytes(bytes);
    return new CurOptimizer(curFile, bytes, options);
  }

  public async ValueTask<CurOptimizationResult> OptimizeAsync(
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null
  ) {
    var combos = this._GenerateCombinations();

    var results = new List<(CurOptimizationCombo combo, CurOptimizationResult result)>();
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

        var optimizationResult = new CurOptimizationResult(
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
    }));

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return results.MinBy(r => r.result.CompressedSize).result;
  }

  private CurOptimizationCombo[] _GenerateCombinations() {
    var imageCount = this._sourceFile.Images.Count;
    if (imageCount == 0)
      return [new CurOptimizationCombo([])];

    var totalCombos = 1 << imageCount;
    var maxCombos = Math.Min(totalCombos, 256);

    var combos = new List<CurOptimizationCombo>();
    for (var i = 0; i < maxCombos; ++i) {
      var formats = new IcoImageFormat[imageCount];
      for (var j = 0; j < imageCount; ++j)
        formats[j] = ((i >> j) & 1) == 0 ? IcoImageFormat.Bmp : IcoImageFormat.Png;
      combos.Add(new CurOptimizationCombo(formats));
    }

    return combos.ToArray();
  }

  private byte[]? _TestCombination(CurOptimizationCombo combo) {
    try {
      var images = new List<CurImage>();
      for (var i = 0; i < this._sourceFile.Images.Count; ++i) {
        var source = this._sourceFile.Images[i];
        images.Add(new CurImage {
          Width = source.Width,
          Height = source.Height,
          BitsPerPixel = source.BitsPerPixel,
          Format = combo.EntryFormats[i],
          Data = source.Data,
          HotspotX = source.HotspotX,
          HotspotY = source.HotspotY
        });
      }

      var result = new CurFile { Images = images };
      return CurWriter.ToBytes(result);
    } catch {
      return null;
    }
  }
}
