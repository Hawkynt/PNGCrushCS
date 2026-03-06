namespace Crush.Core;

/// <summary>Progress report for image format optimization.</summary>
public readonly record struct OptimizationProgress(
  int CombosCompleted,
  int CombosTotal,
  long BestSizeSoFar,
  string Phase);
