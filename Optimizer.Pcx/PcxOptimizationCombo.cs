using FileFormat.Pcx;

namespace Optimizer.Pcx;

public readonly record struct PcxOptimizationCombo(
  PcxColorMode ColorMode,
  PcxPlaneConfig PlaneConfig,
  PcxPaletteOrder PaletteOrder
);
