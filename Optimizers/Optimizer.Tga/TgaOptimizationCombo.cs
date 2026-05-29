using FileFormat.Tga;

namespace Optimizer.Tga;

public readonly record struct TgaOptimizationCombo(
  TgaColorMode ColorMode,
  TgaCompression Compression,
  TgaOrigin Origin
);
