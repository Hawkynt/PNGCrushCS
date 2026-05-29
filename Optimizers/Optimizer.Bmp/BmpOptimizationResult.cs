using System;
using FileFormat.Bmp;

namespace Optimizer.Bmp;

public readonly record struct BmpOptimizationResult(
  BmpColorMode ColorMode,
  BmpCompression Compression,
  BmpRowOrder RowOrder,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, " +
    $"Color: {this.ColorMode}, " +
    $"Compression: {this.Compression}, " +
    $"RowOrder: {this.RowOrder}, " +
    $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
