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
using FileFormat.Jpeg;

namespace Optimizer.Jpeg;

public sealed class JpegOptimizer {
  private readonly byte[]? _inputJpegBytes;
  private readonly bool _isGrayscale;
  private readonly JpegOptimizationOptions _options;
  private readonly byte[]? _rgbPixelData;
  private readonly int _width;
  private readonly int _height;

  public JpegOptimizer(Bitmap image, JpegOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(image);
    this._options = options ?? new JpegOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._rgbPixelData, out this._isGrayscale);
  }

  private JpegOptimizer(byte[] jpegBytes, Bitmap image, JpegOptimizationOptions? options) {
    ArgumentNullException.ThrowIfNull(image);
    this._inputJpegBytes = jpegBytes;
    this._options = options ?? new JpegOptimizationOptions();
    this._width = image.Width;
    this._height = image.Height;

    _ExtractPixelData(image, out this._rgbPixelData, out this._isGrayscale);
  }

  public static JpegOptimizer FromFile(FileInfo file, JpegOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG file not found.", file.FullName);

    var jpegBytes = File.ReadAllBytes(file.FullName);
    using var bmp = new Bitmap(file.FullName);
    return new JpegOptimizer(jpegBytes, bmp, options);
  }

  public async ValueTask<JpegOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null) {
    var combos = this._GenerateCombinations();
    var results = await this._RunCombos(combos, cancellationToken, progress);

    if (results.Count == 0)
      throw new InvalidOperationException("No valid optimization result was produced.");

    return results.MinBy(r => r.result.CompressedSize).result;
  }

  private async ValueTask<List<(JpegOptimizationCombo combo, JpegOptimizationResult result)>> _RunCombos(
    JpegOptimizationCombo[] combos, CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null, string phase = "Optimizing") {
    var results = new List<(JpegOptimizationCombo combo, JpegOptimizationResult result)>();
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

        var optimizationResult = new JpegOptimizationResult(
          combo.Mode,
          combo.OptimizeHuffman,
          combo.StripMetadata,
          combo.IsLossy,
          combo.Quality,
          combo.Subsampling,
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

  private JpegOptimizationCombo[] _GenerateCombinations() {
    var combos = new List<JpegOptimizationCombo>();

    // Lossless combos (only if we have original JPEG bytes)
    if (this._inputJpegBytes != null)
      foreach (var mode in this._options.Modes)
      foreach (var strip in new[] { false, true }) {
        if (!this._options.StripMetadata && strip)
          continue;

        // Always include OptimizeHuffman=true; optionally include false
        combos.Add(new JpegOptimizationCombo(mode, true, strip, false, 0, JpegSubsampling.Chroma444));
        combos.Add(new JpegOptimizationCombo(mode, false, strip, false, 0, JpegSubsampling.Chroma444));
      }

    // Lossy combos (opt-in)
    if (this._options.AllowLossy && this._rgbPixelData != null)
      foreach (var mode in this._options.Modes)
      foreach (var quality in this._options.Qualities) {
        if (quality < this._options.MinQuality)
          continue;

        if (this._isGrayscale) {
          // Grayscale: no subsampling axis
          combos.Add(new JpegOptimizationCombo(mode, true, true, true, quality, JpegSubsampling.Chroma444));
        } else {
          foreach (var sub in this._options.Subsamplings)
            combos.Add(new JpegOptimizationCombo(mode, true, true, true, quality, sub));
        }
      }

    return combos.Distinct().ToArray();
  }

  private byte[]? _TestCombination(JpegOptimizationCombo combo) {
    try {
      if (combo.IsLossy) {
        if (this._rgbPixelData == null)
          return null;

        return JpegWriter.LossyEncode(
          this._rgbPixelData, this._width, this._height,
          combo.Quality, combo.Mode, combo.Subsampling,
          combo.OptimizeHuffman, this._isGrayscale
        );
      }

      if (this._inputJpegBytes == null)
        return null;

      return JpegWriter.LosslessTranscode(
        this._inputJpegBytes,
        combo.Mode,
        combo.OptimizeHuffman,
        combo.StripMetadata
      );
    } catch {
      return null;
    }
  }

  private static void _ExtractPixelData(Bitmap image, out byte[] rgbPixelData, out bool isGrayscale) {
    var width = image.Width;
    var height = image.Height;
    rgbPixelData = new byte[width * height * 3];

    var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
      PixelFormat.Format32bppArgb);
    try {
      isGrayscale = true;
      unsafe {
        for (var y = 0; y < height; ++y) {
          var row = (byte*)data.Scan0 + y * data.Stride;
          for (var x = 0; x < width; ++x) {
            var b = row[x * 4];
            var g = row[x * 4 + 1];
            var r = row[x * 4 + 2];
            var dstIdx = (y * width + x) * 3;
            rgbPixelData[dstIdx] = r;
            rgbPixelData[dstIdx + 1] = g;
            rgbPixelData[dstIdx + 2] = b;
            if (r != g || g != b)
              isGrayscale = false;
          }
        }
      }
    } finally {
      image.UnlockBits(data);
    }
  }
}
