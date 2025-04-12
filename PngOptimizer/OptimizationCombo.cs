namespace PngOptimizer;

/// <summary>Optimization combination record</summary>
public readonly record struct OptimizationCombo(
  ColorMode ColorMode,
  int BitDepth,
  InterlaceMethod InterlaceMethod,
  FilterStrategy FilterStrategy,
  DeflateMethod DeflateMethod
);
