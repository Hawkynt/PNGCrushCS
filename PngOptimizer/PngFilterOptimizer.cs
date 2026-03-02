using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PngOptimizer;

/// <summary>Class for optimizing PNG filters for an entire image</summary>
public sealed class PngFilterOptimizer(
  int width,
  int height,
  int bytesPerPixel,
  bool isGrayscale,
  bool isPalette,
  int bitDepth,
  byte[][] imageData
) {
  private readonly int _bytesPerScanline = imageData[0].Length;
  private readonly PngFilterSelector _filterSelector = new(bytesPerPixel, isGrayscale, isPalette, bitDepth);

  /// <summary>Find the optimal filter for each scanline in the image</summary>
  public FilterType[] OptimizeFilters(FilterStrategy strategy) {
    return strategy switch {
      FilterStrategy.SingleFilter => this.OptimizeSingleFilter(),
      FilterStrategy.WeightedContinuity => this.OptimizeWeightedContinuity(),
      FilterStrategy.ScanlineAdaptive => this.OptimizeScanlineAdaptive(),
      FilterStrategy.BruteForce => this.OptimizeBruteForce(),
      FilterStrategy.BruteForceAdaptive => this.OptimizeBruteForceAdaptive(),
      _ => this.OptimizeScanlineAdaptive()
    };
  }

  /// <summary>Optimize filters with weighted continuity approach</summary>
  private FilterType[] OptimizeWeightedContinuity() {
    this._filterSelector.UseWeightedSumHeuristic = true;
    return this.OptimizeScanlineAdaptive();
  }

  /// <summary>Finds a single filter type that works best for the entire image</summary>
  private FilterType[] OptimizeSingleFilter() {
    if ((isPalette || isGrayscale) && bitDepth < 8)
      return Enumerable.Repeat(FilterType.None, height).ToArray();

    const int filterCount = 5;
    Span<long> totalSums = stackalloc long[filterCount];

    var token = ArrayPool<byte>.Shared.Rent(this._bytesPerScanline);
    try {
      var currentLine = token.AsSpan(0, this._bytesPerScanline);
      for (var y = 0; y < height; ++y) {
        var scanline = imageData[y];
        var previousScanline = y > 0 ? imageData[y - 1] : null;

        foreach (var filterType in FilterTools.AllFilterTypes) {
          FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, currentLine);
          totalSums[(int)filterType] += PngFilterSelector.CalculateDeflateAwareScore(currentLine);
        }
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    var bestFilter = FilterType.None;
    var bestSum = totalSums[0];
    for (var i = 1; i < filterCount; ++i) {
      if (totalSums[i] >= bestSum)
        continue;

      bestSum = totalSums[i];
      bestFilter = (FilterType)i;
    }

    return Enumerable.Repeat(bestFilter, height).ToArray();
  }

  /// <summary>Optimizes each scanline independently for the best filter</summary>
  private FilterType[] OptimizeScanlineAdaptive() {
    var selectedFilters = new FilterType[height];
    for (var y = 0; y < height; ++y) {
      var scanline = imageData[y];
      var previousScanline = y > 0 ? imageData[y - 1] : null;

      selectedFilters[y] = this._filterSelector.SelectFilterForScanline(scanline, previousScanline);
    }

    return selectedFilters;
  }

  /// <summary>Per-scanline adaptive filter selection with lookahead compression for ambiguous rows</summary>
  private FilterType[] OptimizeBruteForceAdaptive() {
    if ((isPalette || isGrayscale) && bitDepth < 8)
      return Enumerable.Repeat(FilterType.None, height).ToArray();

    const int LOOKAHEAD_ROWS = 16;
    const double AMBIGUITY_THRESHOLD = 0.15;

    var selectedFilters = new FilterType[height];
    var filteredLine = new byte[this._bytesPerScanline];

    for (var y = 0; y < height; ++y) {
      var scanline = imageData[y];
      var previousScanline = y > 0 ? imageData[y - 1] : null;

      // Score all 5 filters
      Span<int> scores = stackalloc int[5];
      foreach (var filterType in FilterTools.AllFilterTypes) {
        FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, filteredLine);
        scores[(int)filterType] = PngFilterSelector.CalculateDeflateAwareScore(filteredLine);
      }

      // Find best and second-best
      var best = 0;
      var secondBest = 1;
      if (scores[1] < scores[0]) {
        best = 1;
        secondBest = 0;
      }

      for (var i = 2; i < 5; ++i)
        if (scores[i] < scores[best]) {
          secondBest = best;
          best = i;
        } else if (scores[i] < scores[secondBest]) {
          secondBest = i;
        }

      // Check if ambiguous (top-2 within threshold)
      var bestScore = scores[best];
      var secondScore = scores[secondBest];
      var isAmbiguous = bestScore != 0 && secondScore != 0 &&
                        Math.Abs(secondScore - bestScore) < Math.Abs(bestScore) * AMBIGUITY_THRESHOLD;

      if (isAmbiguous) {
        // Compress a lookahead block with each candidate, pick the smaller result
        var endRow = Math.Min(y + LOOKAHEAD_ROWS, height);
        var bestCompressedSize = long.MaxValue;
        var bestCandidate = (FilterType)best;

        foreach (var candidateIdx in new[] { best, secondBest }) {
          var candidate = (FilterType)candidateIdx;
          using var ms = new MemoryStream();
          using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest, true)) {
            for (var row = y; row < endRow; ++row) {
              var rowScanline = imageData[row];
              var rowPrev = row > 0 ? imageData[row - 1] : null;
              var rowFilter = row == y
                ? candidate
                : this._filterSelector.SelectFilterForScanline(rowScanline, rowPrev);

              var filtered = FilterTools.ApplyFilter(rowFilter, rowScanline, rowPrev, bytesPerPixel);
              zlib.WriteByte((byte)rowFilter);
              zlib.Write(filtered);
            }
          }

          if (ms.Length < bestCompressedSize) {
            bestCompressedSize = ms.Length;
            bestCandidate = candidate;
          }
        }

        selectedFilters[y] = bestCandidate;
      } else {
        selectedFilters[y] = (FilterType)best;
      }
    }

    return selectedFilters;
  }

  /// <summary>Finds the best single filter type by actual compression measurement</summary>
  private FilterType[] OptimizeBruteForce() {
    if ((isPalette || isGrayscale) && bitDepth < 8)
      return Enumerable.Repeat(FilterType.None, height).ToArray();

    var bestFilter = FilterType.None;
    var bestSize = long.MaxValue;

    foreach (var filterType in FilterTools.AllFilterTypes) {
      var filters = Enumerable.Repeat(filterType, height).ToArray();
      var filtered = FilterTools.ApplyFilters(imageData, filters, bytesPerPixel);

      using var ms = new MemoryStream();
      using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in filtered)
          zlib.Write(scanline);
      }

      if (ms.Length < bestSize) {
        bestSize = ms.Length;
        bestFilter = filterType;
      }
    }

    return Enumerable.Repeat(bestFilter, height).ToArray();
  }
}
