namespace PngOptimizer;

/// <summary>Filter selection strategies</summary>
public enum FilterStrategy {
  SingleFilter, // Use a single filter type for the entire image
  ScanlineAdaptive, // Select the best filter for each scanline independently
  WeightedContinuity, // Use weighted sum approach for continuity between scanlines
  PartitionOptimized, // Partition the image and optimize each partition separately
  BruteForce, // Try all filter types and select by actual compressed output size
  BruteForceAdaptive // Per-scanline filter selection with lookahead compression for ambiguous rows
}
