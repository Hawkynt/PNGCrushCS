using System;
using FileFormat.Tga;

namespace Optimizer.Tga;

public readonly record struct TgaOptimizationResult(
  TgaColorMode ColorMode,
  TgaCompression Compression,
  TgaOrigin Origin,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, " +
    $"Color: {this.ColorMode}, " +
    $"Compression: {this.Compression}, " +
    $"Origin: {this.Origin}, " +
    $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
