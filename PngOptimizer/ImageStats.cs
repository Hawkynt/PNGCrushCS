namespace PngOptimizer;

/// <summary>Image statistics record</summary>
public readonly record struct ImageStats(
  int UniqueColors, 
  bool HasAlpha, 
  bool IsGrayscale
);
