namespace TiffOptimizer;

/// <summary>Progress report for TIFF optimization</summary>
public readonly record struct TiffOptimizationProgress(
  int CombosCompleted,
  int CombosTotal,
  long BestSizeSoFar,
  string Phase);
