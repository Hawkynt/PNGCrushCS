using FileFormat.Bmp;

namespace Optimizer.Bmp;

public readonly record struct BmpOptimizationCombo(
  BmpColorMode ColorMode,
  BmpCompression Compression,
  BmpRowOrder RowOrder
);
