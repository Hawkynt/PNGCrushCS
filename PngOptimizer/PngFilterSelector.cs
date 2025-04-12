using System;
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
    foreach (FilterType filterType in Enum.GetValues(typeof(FilterType))) {
      var filtered = this.TestFilter(filterType, scanline, previousScanline);
      var sum = this.CalculateSumOfAbsoluteDifferences(filtered);
      filterSums[filterType] = sum;
    }

    // Use the weighted sum heuristic or standard approach
    return this.UseWeightedSumHeuristic
      ? this.SelectFilterByWeightedSum(filterSums)
      : filterSums.MinBy(pair => pair.Value).Key;
  }

  /// <summary>Selects a filter based on the weighted sum heuristic</summary>
  private FilterType SelectFilterByWeightedSum(Dictionary<FilterType, int> filterSums) {
    // Apply weighting to favor the most recently used filter
    var weightedSums = new Dictionary<FilterType, double>();

    foreach (var (filterType, sum) in filterSums) {
      var weightedSum = sum;

      // Apply discount to the most recently used filter
      if (filterType == this._lastUsedFilter) {
        weightedSum = (int)(weightedSum * this._weightingFactor);
      }

      weightedSums[filterType] = weightedSum;
    }

    // Select the filter with the minimum weighted sum
    var selected = weightedSums.MinBy(pair => pair.Value).Key;
    this._lastUsedFilter = selected; // Update the last used filter

    return selected;
  }

  /// <summary>Calculates the sum of absolute differences, treating bytes as signed values</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int CalculateSumOfAbsoluteDifferences(ReadOnlySpan<byte> filtered) {
    var sum = 0;
    for (var index = 0; index < filtered.Length-1; ++index)
      sum += (filtered[index+1]- filtered[index]).Abs();

    return sum;
  }

  /// <summary>Test a specific filter type without changing the last used filter</summary>
  public byte[] TestFilter(FilterType filterType, ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    return this.ApplyFilter(filterType, scanline, previousScanline);
  }

  /// <summary>Applies the specified filter to the scanline</summary>
  private byte[] ApplyFilter(FilterType filterType, ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    var result = new byte[scanline.Length];

    switch (filterType) {
      case FilterType.None:
        // No filtering, just copy the original scanline
        scanline.CopyTo(result);
        break;

      case FilterType.Sub:
        this.ApplySubFilter(scanline, result);
        break;

      case FilterType.Up:
        this.ApplyUpFilter(scanline, previousScanline, result);
        break;

      case FilterType.Average:
        this.ApplyAverageFilter(scanline, previousScanline, result);
        break;

      case FilterType.Paeth:
        this.ApplyPaethFilter(scanline, previousScanline, result);
        break;
    }

    return result;
  }

  /// <summary>Applies the Sub filter (type 1)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void ApplySubFilter(ReadOnlySpan<byte> scanline, Span<byte> result) {
    // First bytesPerPixel bytes have no left neighbor
    scanline[..bytesPerPixel].CopyTo(result);

    // For remaining bytes, subtract the value of the byte to the left
    for (var i = bytesPerPixel; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - scanline[i - bytesPerPixel]);
  }

  /// <summary>Applies the Up filter (type 2)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void ApplyUpFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result) {
    // If this is the first scanline, previous is assumed to be all zeros
    if (previousScanline.IsEmpty) {
      scanline.CopyTo(result);
      return;
    }

    // Subtract the value of the byte above
    for (var i = 0; i < scanline.Length; ++i)
      result[i] = (byte)(scanline[i] - previousScanline[i]);
  }

  /// <summary>Applies the Average filter (type 3)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void ApplyAverageFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result) {
    // First bytesPerPixel bytes have no left neighbor
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - (above >> 1)); // Divide by 2 with bit shift
    }

    // For remaining bytes, use the average of left and above
    for (var i = bytesPerPixel; i < scanline.Length; ++i) {
      var left = scanline[i - bytesPerPixel] & 0xFF;
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i] & 0xFF;
      var average = (left + above) >> 1; // Divide by 2 with bit shift
      result[i] = (byte)(scanline[i] - average);
    }
  }

  /// <summary>Applies the Paeth filter (type 4)</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void ApplyPaethFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline, Span<byte> result) {
    // First bytesPerPixel bytes have no left neighbor
    for (var i = 0; i < bytesPerPixel; ++i) {
      var above = previousScanline.IsEmpty ? 0 : previousScanline[i];
      result[i] = (byte)(scanline[i] - above);
    }

    // For remaining bytes, use the Paeth predictor
    for (var i = bytesPerPixel; i < scanline.Length; ++i) {
      var a = scanline[i - bytesPerPixel] & 0xFF; // Left
      var b = previousScanline.IsEmpty ? 0 : previousScanline[i] & 0xFF; // Above
      var c = previousScanline.IsEmpty ? 0 : previousScanline[i - bytesPerPixel] & 0xFF; // Upper left

      var p = a + b - c; // Initial estimate
      var pa = Math.Abs(p - a); // Distance to a
      var pb = Math.Abs(p - b); // Distance to b
      var pc = Math.Abs(p - c); // Distance to c

      // Return nearest of a, b, c, breaking ties in order a, b, c
      var pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;

      result[i] = (byte)(scanline[i] - pr);
    }
  }
}
