namespace GifOptimizer;

/// <summary>Progress report for GIF optimization</summary>
public readonly record struct GifOptimizationProgress(
  int CombosCompleted,
  int CombosTotal,
  long BestSizeSoFar,
  string Phase);
