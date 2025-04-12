namespace PngOptimizer;

/// <summary>Parameters for smart partitioning</summary>
public readonly record struct SmartPartitioningParams(
  int MinRowsForMinorImprovement = 5,
  int MinRowsForStrongImprovement = 2,
  double MinorImprovementThreshold = 1.1,
  double StrongImprovementThreshold = 1.3
) {
  public static SmartPartitioningParams Default => new SmartPartitioningParams(5, 2, 1.1, 1.3);

}
