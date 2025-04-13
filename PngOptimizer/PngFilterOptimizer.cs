using System;
using System.Buffers;
using System.Collections.Generic;
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
  private readonly PngFilterSelector _filterSelector = new(bytesPerPixel, isGrayscale, isPalette, bitDepth);

  private readonly int _width = width;
  private readonly int _bytesPerScanline = imageData[0].Length;

  /// <summary>Find the optimal filter for each scanline in the image</summary>
  public FilterType[] OptimizeFilters(FilterStrategy strategy) {
    return strategy switch {
      FilterStrategy.SingleFilter => this.OptimizeSingleFilter(),
      FilterStrategy.WeightedContinuity => this.OptimizeWeightedContinuity(),
      FilterStrategy.ScanlineAdaptive=>this.OptimizeScanlineAdaptive(),
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
    var totalSums = new Dictionary<FilterType, int>();

    foreach (FilterType filterType in Enum.GetValues(typeof(FilterType)))
      totalSums[filterType] = 0;

    // Try each filter type on each scanline and accumulate the sums
    var token=ArrayPool<byte>.Shared.Rent(this._bytesPerScanline);
    try {
      var currentLine = token.AsSpan(0, this._bytesPerScanline);
      for (var y = 0; y < height; ++y) {
        var scanline = imageData[y];
        var previousScanline = y > 0 ? imageData[y - 1] : null;

        foreach (FilterType filterType in Enum.GetValues(typeof(FilterType))) {
          FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel,currentLine);
          var sum = PngFilterSelector.CalculateSumOfAbsoluteDifferences(currentLine);
          totalSums[filterType] += sum;
        }
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    // Find the filter with the lowest total sum
    var bestFilter = totalSums.MinBy(pair => pair.Value).Key;

    // Return an array with the same filter for every scanline
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
  
}