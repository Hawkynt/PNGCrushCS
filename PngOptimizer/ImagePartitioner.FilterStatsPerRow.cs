using System;

namespace PngOptimizer;

partial class ImagePartitioner {
  private readonly record struct FilterStatsPerRow(
    int DeflateAwareScore,
    int Min,
    int Max,
    int Distinct,
    int Changes,
    int ZeroRuns,
    int RepeatedRuns) {
    private int Range => this.Max - this.Min;

    /// <summary>Heuristic score for compressibility. Lower means potentially better compressible.</summary>
    public int Score => this.CalculateCompressibilityScore();

    private int CalculateCompressibilityScore() {
      const int DistinctWeight = 5;
      const int ChangesWeight = 3;
      const int RangeWeight = 2;
      const int ZeroRunWeight = 8;
      const int RepeatedRunWeight = 4;

      return this.DeflateAwareScore
             + this.Distinct * DistinctWeight
             + this.Changes * ChangesWeight
             + this.Range * RangeWeight
             - this.ZeroRuns * ZeroRunWeight
             - this.RepeatedRuns * RepeatedRunWeight;
    }

    public static FilterStatsPerRow FromRow(Span<byte> scanline) {
      if (scanline.IsEmpty)
        return new FilterStatsPerRow();

      Span<bool> seen = stackalloc bool[256];
      var lastValue = scanline[0];
      var min = lastValue;
      var max = lastValue;
      seen[lastValue] = true;
      var distinctCount = 1;
      var changes = 0;
      var zeroRuns = 0;
      var repeatedRuns = 0;
      var inZeroRun = lastValue == 0;
      var inRepeatedRun = lastValue != 0;

      for (var i = 1; i < scanline.Length; ++i) {
        var value = scanline[i];

        if (value != lastValue) {
          if (inZeroRun)
            ++zeroRuns;
          else if (inRepeatedRun)
            ++repeatedRuns;

          ++changes;
          lastValue = value;
          inZeroRun = value == 0;
          inRepeatedRun = value != 0;
        }

        if (value < min)
          min = value;

        if (value > max)
          max = value;

        if (!seen[value]) {
          seen[value] = true;
          ++distinctCount;
        }
      }

      if (inZeroRun)
        ++zeroRuns;
      else if (inRepeatedRun)
        ++repeatedRuns;

      var score = PngFilterSelector.CalculateDeflateAwareScore(scanline);
      return new FilterStatsPerRow(score, min, max, distinctCount, changes, zeroRuns, repeatedRuns);
    }
  }
}
