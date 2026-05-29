namespace Optimizer.Gif;

public readonly record struct GifOptimizationCombo(
  PaletteReorderStrategy PaletteStrategy,
  bool UseGlobalColorTable,
  bool OptimizeDisposal,
  bool TrimTransparentMargins,
  LzwMode LzwMode = LzwMode.Standard,
  bool ComputeFrameDiffs = false,
  bool CompressionAwareDisposal = false
);
