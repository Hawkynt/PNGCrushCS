using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PngOptimizer;

/// <summary>Class for handling PNG filtering for specific scanlines</summary>
public sealed class PngFilterSelector(
  int bytesPerPixel,
  bool isGrayscale,
  bool isPalette,
  int bitDepth) {
  
  // For weighted sum heuristic
  private FilterType _lastUsedFilter = FilterType.None;
  private readonly double _weightingFactor = 0.9; // Experimental value

  /// <summary>Selects the best filter for a scanline based on the described heuristics</summary>
  public FilterType SelectFilterForScanline(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    // Rule 1: For palette images, don't use filtering
    if (isPalette)
      return FilterType.None;

    // Rule 2: For low-bit-depth grayscale images, filtering is rarely useful
    if (isGrayscale && bitDepth < 8)
      return FilterType.None;

    // For 8+ bit grayscale and truecolor, use dynamic filtering
    return this.SelectDynamicFilter(scanline, previousScanline);
  }

  /// <summary>Property to enable/disable the experimental weighted sum heuristic</summary>
  public bool UseWeightedSumHeuristic { get; set; } = false;

  /// <summary>Applies the minimum sum of absolute differences heuristic</summary>
  private FilterType SelectDynamicFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    // Dictionary to hold sum of absolute differences for each filter type
    var filterSums = new Dictionary<FilterType, int>();

    // Apply each filter type and calculate the sum of absolute differences
    var token= ArrayPool<byte>.Shared.Rent(scanline.Length);
    try {
      var currentLine = token.AsSpan(0, scanline.Length);
      foreach (FilterType filterType in Enum.GetValues(typeof(FilterType))) {
        FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel,currentLine);
        var sum = CalculateSumOfAbsoluteDifferences(currentLine);
        filterSums[filterType] = sum;
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    // Use the weighted sum heuristic or standard approach
    return this.UseWeightedSumHeuristic
      ? this.SelectFilterByWeightedSum(filterSums)
      : filterSums.MinBy(pair => pair.Value).Key;
  }

  /// <summary>Selects a filter based on the weighted sum heuristic</summary>
  private FilterType SelectFilterByWeightedSum(Dictionary<FilterType, int> filterSums) {
    var weightedSums = new Dictionary<FilterType, double>();
    foreach (var (filterType, sum) in filterSums) {
      var weightedSum = sum;
      if (filterType == this._lastUsedFilter)
        weightedSum = (int)(weightedSum * this._weightingFactor);

      weightedSums[filterType] = weightedSum;
    }

    return this._lastUsedFilter = weightedSums.MinBy(pair => pair.Value).Key;
  }

  /// <summary>Calculates the sum of absolute differences, treating bytes as signed values</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int CalculateSumOfAbsoluteDifferences(ReadOnlySpan<byte> filtered) {
    var sum = 0;
    for (var index = 0; index < filtered.Length - 1; ++index)
      sum += (filtered[index + 1] - filtered[index]).Abs();

    return sum;
  }

}
