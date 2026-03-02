namespace PngOptimizer;

/// <summary>Progress report for PNG optimization</summary>
public readonly record struct OptimizationProgress(
  int CombosCompleted,
  int CombosTotal,
  long BestSizeSoFar,
  string Phase);
