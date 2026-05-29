using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.WebP;

namespace Optimizer.WebP;

/// <summary>WebP optimization engine. Phase 2: metadata stripping and RIFF container rewriting.</summary>
public sealed class WebPOptimizer {
  private readonly WebPFile _file;
  private readonly WebPOptimizationOptions _options;

  public WebPOptimizer(WebPFile file, WebPOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    this._file = file;
    this._options = options ?? new WebPOptimizationOptions();
  }

  public static WebPOptimizer FromFile(FileInfo file, WebPOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WebP file not found.", file.FullName);

    var webp = WebPReader.FromFile(file);
    return new WebPOptimizer(webp, options);
  }

  public async ValueTask<WebPOptimizationResult> OptimizeAsync(
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null
  ) {
    var combos = _GenerateCombinations();
    var results = new List<WebPOptimizationResult>();
    var resultsLock = new object();
    var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);
    var completedCount = 0;
    var bestSize = long.MaxValue;

    var tasks = new List<Task>();
    foreach (var combo in combos) {
      tasks.Add(Task.Run(async () => {
        await semaphore.WaitAsync(cancellationToken);
        try {
          cancellationToken.ThrowIfCancellationRequested();

          var sw = Stopwatch.StartNew();
          var bytes = _TestCombination(combo);
          sw.Stop();

          if (bytes == null)
            return;

          var result = new WebPOptimizationResult(bytes.Length, sw.Elapsed, bytes, combo.StripMetadata);

          lock (resultsLock) {
            results.Add(result);
            if (result.CompressedSize < bestSize)
              bestSize = result.CompressedSize;
          }

          var done = Interlocked.Increment(ref completedCount);
          progress?.Report(new OptimizationProgress(done, combos.Length, bestSize, "Optimizing"));
        } finally {
          semaphore.Release();
        }
      }));
    }

    await Task.WhenAll(tasks);
    progress?.Report(new OptimizationProgress(combos.Length, combos.Length, bestSize, "Complete"));

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    WebPOptimizationResult best = results[0];
    for (var i = 1; i < results.Count; ++i)
      if (results[i].CompressedSize < best.CompressedSize)
        best = results[i];

    return best;
  }

  private WebPOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<WebPOptimizationCombo>();

    // Always try keeping metadata (original structure rewritten)
    combos.Add(new WebPOptimizationCombo(StripMetadata: false));

    // Try stripping metadata if there is metadata to strip
    if (this._file.MetadataChunks.Count > 0)
      combos.Add(new WebPOptimizationCombo(StripMetadata: true));

    return combos.ToArray();
  }

  private byte[]? _TestCombination(WebPOptimizationCombo combo) {
    try {
      var output = new WebPFile {
        Features = this._file.Features,
        ImageData = this._file.ImageData,
        IsLossless = this._file.IsLossless,
        MetadataChunks = combo.StripMetadata ? [] : this._file.MetadataChunks
      };

      // When stripping metadata, update features if alpha flag came from metadata presence
      if (combo.StripMetadata && this._file.MetadataChunks.Count > 0)
        output = new WebPFile {
          Features = this._file.Features,
          ImageData = this._file.ImageData,
          IsLossless = this._file.IsLossless,
          MetadataChunks = []
        };

      return WebPWriter.ToBytes(output);
    } catch {
      return null;
    }
  }
}
