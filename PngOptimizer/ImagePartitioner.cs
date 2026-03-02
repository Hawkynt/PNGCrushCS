using System;
using System.Buffers;
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
  private const int _FILTER_TYPE_COUNT = 5;
  private readonly SmartPartitioningParams _partitionParams = partitionParams ?? SmartPartitioningParams.Default;

  /// <summary>Optimize filters for each partition of the image using content-aware approaches</summary>
  public (FilterType[] filters, byte[][] filteredData) OptimizePartitions() {
    if ((isPalette || isGrayscale) && bitDepth < 8)
      return this.OptimizeWithSingleFilter(FilterType.None);

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
    var stats = new FilterStatsPerRow[height][];
    for (var y = 0; y < height; ++y)
      stats[y] = this.CalculateAllFilterScores(y);

    var filters = new FilterType[height];
    var currentFilter = FilterType.None;
    for (var y = 0; y < height; ++y) {
      var filterScores = stats[y];

      if (y > height - this._partitionParams.MinRowsForMinorImprovement) {
        filters[y] = currentFilter;
        continue;
      }

      var bestFilter = FindBestFilter(filterScores);
      if (bestFilter == currentFilter) {
        filters[y] = currentFilter;
        continue;
      }

      var minorImprovementCount = 0;
      var strongImprovementCount = 0;

      for (var ahead = 0; ahead < this._partitionParams.MinRowsForMinorImprovement && y + ahead < height; ++ahead) {
        var futureScores = stats[y + ahead];

        var currentFilterScore = futureScores[(int)currentFilter];
        var newFilterScore = futureScores[(int)bestFilter];
        var improvementRatio = currentFilterScore.Score / (float)newFilterScore.Score;

        if (improvementRatio >= this._partitionParams.StrongImprovementThreshold)
          ++strongImprovementCount;
        else if (improvementRatio >= this._partitionParams.MinorImprovementThreshold)
          ++minorImprovementCount;
      }

      if (
        strongImprovementCount >= this._partitionParams.MinRowsForStrongImprovement ||
        minorImprovementCount >= this._partitionParams.MinRowsForMinorImprovement
      )
        currentFilter = bestFilter;

      filters[y] = currentFilter;
    }

    return filters;

    static FilterType FindBestFilter(FilterStatsPerRow[] scores) {
      var bestFilter = FilterType.None;
      var bestScore = scores[0].Score;
      for (var i = 1; i < _FILTER_TYPE_COUNT; ++i) {
        if (scores[i].Score >= bestScore)
          continue;

        bestScore = scores[i].Score;
        bestFilter = (FilterType)i;
      }

      return bestFilter;
    }
  }

  /// <summary>Calculate scores for all filter types on a given row</summary>
  private FilterStatsPerRow[] CalculateAllFilterScores(int rowIndex) {
    var scanline = imageData[rowIndex];
    var previousScanline = rowIndex > 0 ? imageData[rowIndex - 1] : null;

    var scores = new FilterStatsPerRow[_FILTER_TYPE_COUNT];
    var token = ArrayPool<byte>.Shared.Rent(scanline.Length);
    try {
      var currentLine = token.AsSpan(0, scanline.Length);
      foreach (var filterType in FilterTools.AllFilterTypes) {
        FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, currentLine);
        scores[(int)filterType] = FilterStatsPerRow.FromRow(currentLine);
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    return scores;
  }
}
