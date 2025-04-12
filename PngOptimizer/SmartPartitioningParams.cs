namespace PngOptimizer;

/// <summary>Parameters for smart partitioning</summary>
public readonly record struct SmartPartitioningParams(
  int MinRowsForMinorImprovement = 5,
  int MinRowsForStrongImprovement = 2,
  double MinorImprovementThreshold = 1.1,
  double StrongImprovementThreshold = 1.3);
