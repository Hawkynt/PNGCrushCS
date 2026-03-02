using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace PngOptimizer;

/// <summary>Class for handling PNG filtering for specific scanlines</summary>
public sealed class PngFilterSelector(
  int bytesPerPixel,
  bool isGrayscale,
  bool isPalette,
  int bitDepth) {
  private const int _FILTER_TYPE_COUNT = 5;
  private const int _STICKY_THRESHOLD_ROWS = 3;
  private const int _STICKY_REUSE_ROWS = 4;
  private readonly double _weightingFactor = 0.9; // Experimental value

  // For weighted sum heuristic
  private FilterType _lastUsedFilter = FilterType.None;

  // For stickiness optimization
  private FilterType _stickyFilter = FilterType.None;
  private int _stickyReuseRemaining;
  private int _stickyWinCount;

  /// <summary>Property to enable/disable the experimental weighted sum heuristic</summary>
  public bool UseWeightedSumHeuristic { get; set; } = false;

  /// <summary>Selects the best filter for a scanline based on the described heuristics</summary>
  public FilterType SelectFilterForScanline(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    if ((isPalette || isGrayscale) && bitDepth < 8)
      return FilterType.None;

    return this.SelectDynamicFilter(scanline, previousScanline);
  }

  /// <summary>Applies the deflate-aware filter scoring heuristic with early break and stickiness</summary>
  private FilterType SelectDynamicFilter(ReadOnlySpan<byte> scanline, ReadOnlySpan<byte> previousScanline) {
    // Stickiness: reuse filter if it has been dominant for consecutive rows
    if (this._stickyReuseRemaining > 0) {
      --this._stickyReuseRemaining;
      if (this.UseWeightedSumHeuristic)
        this._lastUsedFilter = this._stickyFilter;
      return this._stickyFilter;
    }

    Span<int> filterSums = stackalloc int[_FILTER_TYPE_COUNT];

    var token = ArrayPool<byte>.Shared.Rent(scanline.Length);
    try {
      var currentLine = token.AsSpan(0, scanline.Length);
      var earlyBreakThreshold = -scanline.Length;

      foreach (var filterType in FilterTools.AllFilterTypes) {
        FilterTools.ApplyFilterTo(filterType, scanline, previousScanline, bytesPerPixel, currentLine);
        var score = CalculateDeflateAwareScore(currentLine);
        filterSums[(int)filterType] = score;

        // Early break: if score is extremely good (all zeros bonus), skip remaining filters
        if (score <= earlyBreakThreshold) {
          // Fill remaining with max int so this filter wins
          for (var j = (int)filterType + 1; j < _FILTER_TYPE_COUNT; ++j)
            filterSums[j] = int.MaxValue;
          break;
        }
      }
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }

    var winner = this.UseWeightedSumHeuristic
      ? this.SelectFilterByWeightedSum(filterSums)
      : SelectMinFilter(filterSums);

    // Update stickiness tracking
    this._UpdateStickiness(filterSums, winner);

    return winner;
  }

  /// <summary>Track filter stickiness — when a filter wins by > 2x margin for consecutive rows, reuse it</summary>
  private void _UpdateStickiness(ReadOnlySpan<int> filterSums, FilterType winner) {
    // Find second-best score
    var bestScore = filterSums[(int)winner];
    var secondBest = int.MaxValue;
    for (var i = 0; i < _FILTER_TYPE_COUNT; ++i)
      if (i != (int)winner && filterSums[i] < secondBest)
        secondBest = filterSums[i];

    // Check if winner dominates by > 2x margin
    var dominates = secondBest != int.MaxValue && bestScore < 0
      ? bestScore < secondBest * 2
      : bestScore != 0 && secondBest > bestScore * 2;

    if (dominates && winner == this._stickyFilter) {
      ++this._stickyWinCount;
      if (this._stickyWinCount >= _STICKY_THRESHOLD_ROWS)
        this._stickyReuseRemaining = _STICKY_REUSE_ROWS;
    } else {
      this._stickyFilter = winner;
      this._stickyWinCount = 1;
    }
  }

  /// <summary>Reset stickiness state (call when entering a new partition)</summary>
  public void ResetStickiness() {
    this._stickyFilter = FilterType.None;
    this._stickyWinCount = 0;
    this._stickyReuseRemaining = 0;
  }

  /// <summary>Selects a filter based on the weighted sum heuristic</summary>
  private FilterType SelectFilterByWeightedSum(ReadOnlySpan<int> filterSums) {
    var bestFilter = FilterType.None;
    var bestScore = double.MaxValue;

    foreach (var filterType in FilterTools.AllFilterTypes) {
      var score = (double)filterSums[(int)filterType];
      if (filterType == this._lastUsedFilter)
        score *= this._weightingFactor;

      if (score < bestScore) {
        bestScore = score;
        bestFilter = filterType;
      }
    }

    return this._lastUsedFilter = bestFilter;
  }

  private static FilterType SelectMinFilter(ReadOnlySpan<int> filterSums) {
    var bestFilter = FilterType.None;
    var bestSum = filterSums[0];

    for (var i = 1; i < _FILTER_TYPE_COUNT; ++i) {
      if (filterSums[i] >= bestSum)
        continue;

      bestSum = filterSums[i];
      bestFilter = (FilterType)i;
    }

    return bestFilter;
  }

  /// <summary>Calculates the sum of absolute differences, treating bytes as signed values</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int CalculateSumOfAbsoluteDifferences(ReadOnlySpan<byte> filtered) {
    var sum = 0;
    for (var index = 0; index < filtered.Length - 1; ++index)
      sum += (filtered[index + 1] - filtered[index]).Abs();

    return sum;
  }

  /// <summary>
  ///   Calculates a deflate-aware score that rewards repeated byte runs, byte-pair repetition,
  ///   and penalizes high-diversity segments. Lower scores indicate better compressibility.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static int CalculateDeflateAwareScore(ReadOnlySpan<byte> filtered) {
    if (filtered.IsEmpty)
      return 0;

    var score = 0;
    var runLength = 1;
    var runByte = filtered[0];
    var pairCount = 0;

    for (var i = 1; i < filtered.Length; ++i) {
      if (filtered[i] == runByte) {
        ++runLength;
      } else {
        score += _ScoreRun(runByte, runLength);
        runByte = filtered[i];
        runLength = 1;
      }

      // Count byte-pair repetitions (adjacent identical bytes) for LZ77 matching bonus
      if (filtered[i] == filtered[i - 1])
        ++pairCount;
    }

    score += _ScoreRun(runByte, runLength);

    // Byte-pair bonus: frequent adjacent identical bytes compress well via LZ77
    score -= pairCount >> 1;

    // High-diversity penalty: check 16-byte windows for high unique-value density
    if (filtered.Length >= 16) {
      var diversityPenalty = 0;
      Span<bool> seen = stackalloc bool[256];
      for (var windowStart = 0; windowStart <= filtered.Length - 16; windowStart += 16) {
        seen.Clear();
        var unique = 0;
        for (var j = 0; j < 16; ++j) {
          ref var s = ref seen[filtered[windowStart + j]];
          if (!s) {
            s = true;
            ++unique;
          }
        }

        if (unique > 14)
          diversityPenalty += unique - 14;
      }

      score += diversityPenalty;
    }

    return score;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _ScoreRun(byte value, int runLength) {
    if (value == 0)
      return -(runLength * runLength);

    var circularDistance = value < 128 ? value : 256 - value;
    if (runLength >= 4)
      // Long runs (4+): extra bonus for deflate efficiency — use runLength^1.5 approximation
      return circularDistance - (int)(runLength * Math.Sqrt(runLength));

    if (runLength > 1)
      return circularDistance - ((runLength * runLength) >> 1);

    return circularDistance;
  }
}
