using System;
using System.Collections.Generic;
using System.Linq;

namespace PngOptimizer;

/// <summary>Class to partition an image and optimize filters for each partition</summary>
public sealed class ImagePartitioner {
  private readonly byte[][] _imageData;
  private readonly int _width;
  private readonly int _height;
  private readonly int _bytesPerPixel;
  private readonly bool _isPalette;
  private readonly bool _isGrayscale;
  private readonly int _bitDepth;
  private readonly PngFilterSelector _filterSelector;

  // Default smart partitioning parameters
  private readonly SmartPartitioningParams _partitionParams;

  public ImagePartitioner(
    byte[][] imageData,
    int width,
    int height,
    int bytesPerPixel,
    bool isPalette,
    bool isGrayscale,
    int bitDepth,
    SmartPartitioningParams? partitionParams = null) {
    this._imageData = imageData;
    this._width = width;
    this._height = height;
    this._bytesPerPixel = bytesPerPixel;
    this._isPalette = isPalette;
    this._isGrayscale = isGrayscale;
    this._bitDepth = bitDepth;

    if (partitionParams != null) {
      this._partitionParams = partitionParams.Value;
    }

    this._filterSelector = new PngFilterSelector(bytesPerPixel, isGrayscale, isPalette, bitDepth);
  }

  /// <summary>Optimize filters for each partition of the image using content-aware approaches</summary>
  public (FilterType[] filters, byte[][] filteredData) OptimizePartitions() {
    // For palette images, just use None filter
    if (this._isPalette || (this._isGrayscale && this._bitDepth < 8)) {
      return this.OptimizeWithSingleFilter(FilterType.None);
    }

    // Use smart partitioning for other images
    return this.OptimizeWithSmartPartitioning();
  }

  /// <summary>Optimizes using a single filter for the entire image</summary>
  private (FilterType[] filters, byte[][] filteredData) OptimizeWithSingleFilter(FilterType filterType) {
    var filters = Enumerable.Repeat(filterType, this._height).ToArray();
    var filteredData = this.ApplyFilters(filters);
    return (filters, filteredData);
  }

  /// <summary>Optimizes partitions based on smart content analysis</summary>
  private (FilterType[] filters, byte[][] filteredData) OptimizeWithSmartPartitioning() {
    var filters = this.OptimizeFiltersWithSmartPartitioning();
    var filteredData = this.ApplyFilters(filters);
    return (filters, filteredData);
  }

  /// <summary>Content-aware partitioning that avoids excessive filter changes</summary>
  private FilterType[] OptimizeFiltersWithSmartPartitioning() {
    var filters = new FilterType[this._height];
    var currentFilter = FilterType.None; // Start with None as default

    for (var y = 0; y < this._height; y++) {
      var filterScores = this.CalculateAllFilterScores(y);

      // If we're close to the end of the image, don't change filters
      if (y > this._height - this._partitionParams.MinRowsForMinorImprovement) {
        filters[y] = currentFilter;
        continue;
      }

      // Find potential better filter
      var bestFilter = filterScores.MinBy(kv => kv.Value).Key;
      if (bestFilter == currentFilter) {
        // Already using the best filter
        filters[y] = currentFilter;
        continue;
      }

      // Look ahead to see if change is beneficial over multiple rows
      var minorImprovementCount = 0;
      var strongImprovementCount = 0;

      // Check the next several rows to see if the new filter would be better
      for (var ahead = 0; ahead < this._partitionParams.MinRowsForMinorImprovement && y + ahead < this._height; ahead++) {
        var futureScores = this.CalculateAllFilterScores(y + ahead);

        var currentFilterScore = futureScores[currentFilter];
        var newFilterScore = futureScores[bestFilter];

        // Calculate improvement ratio (lower score is better)
        var improvementRatio = currentFilterScore / (double)newFilterScore;

        if (improvementRatio >= this._partitionParams.StrongImprovementThreshold) {
          strongImprovementCount++;
        } else if (improvementRatio >= this._partitionParams.MinorImprovementThreshold) {
          minorImprovementCount++;
        }
      }

      // Change filter only if we have sustained improvement
      if (strongImprovementCount >= this._partitionParams.MinRowsForStrongImprovement ||
          minorImprovementCount >= this._partitionParams.MinRowsForMinorImprovement) {
        // Switch to new filter - it shows sustained improvement
        currentFilter = bestFilter;
      }

      filters[y] = currentFilter;
    }

    return filters;
  }

  /// <summary>Calculate scores for all filter types on a given row</summary>
  private Dictionary<FilterType, int> CalculateAllFilterScores(int rowIndex) {
    var scanline = this._imageData[rowIndex];
    var previousScanline = rowIndex > 0 ? this._imageData[rowIndex - 1] : null;

    var scores = new Dictionary<FilterType, int>();

    foreach (FilterType filterType in Enum.GetValues(typeof(FilterType))) {
      var filtered = this._filterSelector.TestFilter(filterType, scanline, previousScanline);
      scores[filterType] = this._filterSelector.CalculateSumOfAbsoluteDifferences(filtered);
    }

    return scores;
  }

  /// <summary>Apply chosen filters to all scanlines and return filtered data</summary>
  private byte[][] ApplyFilters(FilterType[] filters) {
    var filteredData = new byte[this._height][];

    for (var y = 0; y < this._height; y++) {
      var scanline = this._imageData[y];
      var previousScanline = y > 0 ? this._imageData[y - 1] : null;

      // Create filtered scanline with a type byte at the beginning
      var filteredScanline = new byte[scanline.Length + 1];
      filteredScanline[0] = (byte)filters[y]; // Filter type byte

      var filtered = this._filterSelector.TestFilter(filters[y], scanline, previousScanline);
      Array.Copy(filtered, 0, filteredScanline, 1, filtered.Length);

      filteredData[y] = filteredScanline;
    }

    return filteredData;
  }

  /// <summary>Analyze image content for potential partitioning strategies</summary>
  private Dictionary<FilterType, double> AnalyzeImageContent() {
    // Calculate statistics for each filter type
    var filterStats = new Dictionary<FilterType, double>();

    // Count instances where each filter performs best
    var bestFilterCounts = new Dictionary<FilterType, int>();
    foreach (FilterType filter in Enum.GetValues(typeof(FilterType))) {
      bestFilterCounts[filter] = 0;
    }

    // Sample rows throughout the image
    var sampleCount = Math.Min(100, this._height);
    var stride = Math.Max(1, this._height / sampleCount);

    for (var y = 0; y < this._height; y += stride) {
      var scores = this.CalculateAllFilterScores(y);
      var bestFilter = scores.MinBy(kv => kv.Value).Key;
      bestFilterCounts[bestFilter]++;
    }

    // Convert counts to percentages
    var totalSamples = bestFilterCounts.Values.Sum();
    foreach (var filter in bestFilterCounts.Keys.ToList()) {
      filterStats[filter] = bestFilterCounts[filter] / (double)totalSamples;
    }

    return filterStats;
  }
}