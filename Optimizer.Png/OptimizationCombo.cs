using FileFormat.Png;

namespace Optimizer.Png;

/// <summary>Optimization combination record</summary>
public readonly record struct OptimizationCombo(
  PngColorType ColorMode,
  int BitDepth,
  PngInterlaceMethod InterlaceMethod,
  FilterStrategy FilterStrategy,
  DeflateMethod DeflateMethod,
  QuantizerDithererCombo? LossyPaletteCombo = null
);
