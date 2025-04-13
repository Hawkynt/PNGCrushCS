namespace PngOptimizer;

using System;
using System.Collections.Generic;

partial class ImagePartitioner {

  private readonly record struct FilterStatsPerRow(int SumOfAbsoluteDifferences, int Min, int Max, int Distinct, int Changes) {

    private int Range => this.Max - this.Min;

    /// <summary>Heuristic score for compressibility. Lower means potentially better compressible.</summary>
    public int Score => CalculateCompressibilityScore();

    private int CalculateCompressibilityScore() {
      // A heuristic score combining multiple factors to estimate compressibility
      // Lower distinct values, fewer changes, and smaller range are favorable for compression
      const int DistinctWeight = 5;
      const int ChangesWeight = 3;
      const int RangeWeight = 2;

      return (this.SumOfAbsoluteDifferences)
             + (this.Distinct * DistinctWeight)
             + (this.Changes * ChangesWeight)
             + (this.Range * RangeWeight);
    }

    public static FilterStatsPerRow FromRow(Span<byte> scanline) {
      if (scanline.IsEmpty)
        return new FilterStatsPerRow();

      var lastValue = scanline[0];
      var min = lastValue;
      var max = lastValue;
      var distinctValues = new HashSet<byte>(256) { lastValue };
      var changes = 0;

      for (var i = 1; i < scanline.Length; i++) {
        var value = scanline[i];

        if (value != lastValue) {
          ++changes;
          lastValue = value;
        }

        if (value < min)
          min = value;

        if (value > max)
          max = value;

        distinctValues.Add(value);
      }

      var sum = PngFilterSelector.CalculateSumOfAbsoluteDifferences(scanline);
      return new(sum, min, max, distinctValues.Count, changes);
    }
  }

}