using FileFormat.Jpeg;

namespace Optimizer.Jpeg;

public readonly record struct JpegOptimizationCombo(
  JpegMode Mode,
  bool OptimizeHuffman,
  bool StripMetadata,
  bool IsLossy,
  int Quality,
  JpegSubsampling Subsampling
);
