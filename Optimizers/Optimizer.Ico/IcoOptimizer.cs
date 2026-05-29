using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.Ico;

namespace Optimizer.Ico;

public sealed class IcoOptimizer {
  private readonly IcoFile _sourceFile;
  private readonly byte[] _sourceBytes;
  private readonly IcoOptimizationOptions _options;

  public IcoOptimizer(IcoFile sourceFile, byte[] sourceBytes, IcoOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(sourceFile);
    ArgumentNullException.ThrowIfNull(sourceBytes);
    this._sourceFile = sourceFile;
    this._sourceBytes = sourceBytes;
    this._options = options ?? new IcoOptimizationOptions();
  }

  public static IcoOptimizer FromFile(FileInfo file, IcoOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICO file not found.", file.FullName);

    var bytes = File.ReadAllBytes(file.FullName);
    var icoFile = IcoReader.FromBytes(bytes);
    return new IcoOptimizer(icoFile, bytes, options);
  }

  public async ValueTask<IcoOptimizationResult> OptimizeAsync(
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null
  ) {
    var combos = this._GenerateCombinations();

    var results = new List<(IcoOptimizationCombo combo, IcoOptimizationResult result)>();
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

        var optimizationResult = new IcoOptimizationResult(
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

  private IcoOptimizationCombo[] _GenerateCombinations() {
    var imageCount = this._sourceFile.Images.Count;
    if (imageCount == 0)
      return [new IcoOptimizationCombo([])];

    var formatOptions = new[] { IcoImageFormat.Bmp, IcoImageFormat.Png };
    var totalCombos = 1 << imageCount;
    var maxCombos = Math.Min(totalCombos, 256);

    var combos = new List<IcoOptimizationCombo>();
    for (var i = 0; i < maxCombos; ++i) {
      var formats = new IcoImageFormat[imageCount];
      for (var j = 0; j < imageCount; ++j)
        formats[j] = ((i >> j) & 1) == 0 ? IcoImageFormat.Bmp : IcoImageFormat.Png;
      combos.Add(new IcoOptimizationCombo(formats));
    }

    return combos.ToArray();
  }

  private byte[]? _TestCombination(IcoOptimizationCombo combo) {
    try {
      var images = new List<IcoImage>();
      for (var i = 0; i < this._sourceFile.Images.Count; ++i) {
        var source = this._sourceFile.Images[i];
        images.Add(new IcoImage {
          Width = source.Width,
          Height = source.Height,
          BitsPerPixel = source.BitsPerPixel,
          Format = combo.EntryFormats[i],
          Data = source.Data
        });
      }

      var result = new IcoFile { Images = images };
      return IcoWriter.ToBytes(result);
    } catch {
      return null;
    }
  }
}
