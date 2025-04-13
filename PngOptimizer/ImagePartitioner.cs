using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace PngOptimizer;

/// <summary>Class to partition an image and optimize filters for each partition</summary>
public sealed partial class ImagePartitioner(
  byte[][] imageData,
  int height,
  int bytesPerPixel,
  bool isPalette,
  bool isGrayscale,
  int bitDepth,
  SmartPartitioningParams? partitionParams = null
) {

  // Default smart partitioning parameters
  private readonly SmartPartitioningParams _partitionParams = partitionParams ?? SmartPartitioningParams.Default;

  /// <summary>Optimize filters for each partition of the image using content-aware approaches</summary>
  public (FilterType[] filters, byte[][] filteredData) OptimizePartitions() {
    // For palette images, just use None filter
    if (isPalette || (isGrayscale && bitDepth < 8))
      return this.OptimizeWithSingleFilter(FilterType.None);

    // Use smart partitioning for other images
    return this.OptimizeWithSmartPartitioning();
  }

  /// <summary>Optimizes using a single filter for the entire image</summary>
  private (FilterType[] filters, byte[][] filteredData) OptimizeWithSingleFilter(FilterType filterType) {
    var filters = Enumerable.Repeat(filterType, height).ToArray();
    var filteredData = FilterTools.ApplyFilters(imageData, filters, bytesPerPixel);
    return (filters, filteredData);
  }

  /// <summary>Optimizes partitions based on smart content analysis</summary>
  private (FilterType[] filters, byte[][] filteredData) OptimizeWithSmartPartitioning() {
    var filters = this.OptimizeFiltersWithSmartPartitioning();
    var filteredData = FilterTools.ApplyFilters(imageData, filters, bytesPerPixel);
    return (filters, filteredData);
  }

  /// <summary>Content-aware partitioning that avoids excessive filter changes</summary>
  private FilterType[] OptimizeFiltersWithSmartPartitioning() {

    // step 1: Calculate filter scores for each row
    var stats = new Dictionary<FilterType, FilterStatsPerRow>[height];
    for (var y = 0; y < height; ++y)
      stats[y] = this.CalculateAllFilterScores(y);

    // step 2: Select the best filter for each row based on the scores
    var filters = new FilterType[height];
    var currentFilter = FilterType.None;
    for (var y = 0; y < height; ++y) {
      var filterScores = stats[y];

      // If we're close to the end of the image, don't change filters
      if (y > height - this._partitionParams.MinRowsForMinorImprovement) {
        filters[y] = currentFilter;
        continue;
      }

      // Find potential better filter
      var bestFilter = filterScores.MinBy(kv => kv.Value.Score).Key;
      if (bestFilter == currentFilter) {
        // Already using the best filter
        filters[y] = currentFilter;
        continue;
      }

      // Look ahead to see if change is beneficial over multiple rows
      var minorImprovementCount = 0;
      var strongImprovementCount = 0;

      // Check the next several rows to see if the new filter would be better
      for (var ahead = 0; ahead < this._partitionParams.MinRowsForMinorImprovement && y + ahead < height; ++ahead) {
        var futureScores = stats[y + ahead];

        var currentFilterScore = futureScores[currentFilter];
        var newFilterScore = futureScores[bestFilter];
        var improvementRatio = currentFilterScore.Score / (float)newFilterScore.Score;

        if (improvementRatio >= this._partitionParams.StrongImprovementThreshold)
          ++strongImprovementCount;
        else if (improvementRatio >= this._partitionParams.MinorImprovementThreshold)
          ++minorImprovementCount;
      }

      // Change filter only if we have sustained improvement
      if (
          strongImprovementCount >= this._partitionParams.MinRowsForStrongImprovement ||
          minorImprovementCount >= this._partitionParams.MinRowsForMinorImprovement
        )
        // Switch to new filter - it shows sustained improvement
        currentFilter = bestFilter;

      filters[y] = currentFilter;
    }

    return filters;
  }

  /// <summary>Calculate scores for all filter types on a given row</summary>
  private Dictionary<FilterType, FilterStatsPerRow> CalculateAllFilterScores(int rowIndex) {
    var scanline = imageData[rowIndex];
    var previousScanline = rowIndex > 0 ? imageData[rowIndex - 1] : null;

    var scores = new Dictionary<FilterType, FilterStatsPerRow>();
    var token = ArrayPool<byte>.Shared.Rent(scanline.Length);
    try {
      var currentLine = token.AsSpan(0, scanline.Length);
      foreach (FilterType filterType in Enum.GetValues(typeof(FilterType))) {
        FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, currentLine);
        scores[filterType] = FilterStatsPerRow.FromRow(currentLine);
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    return scores;
  }


}