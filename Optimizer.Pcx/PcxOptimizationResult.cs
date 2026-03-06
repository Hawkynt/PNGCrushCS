using System;
using FileFormat.Pcx;

namespace Optimizer.Pcx;

public readonly record struct PcxOptimizationResult(
  PcxColorMode ColorMode,
  PcxPlaneConfig PlaneConfig,
  PcxPaletteOrder PaletteOrder,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, " +
    $"Color: {this.ColorMode}, " +
    $"Planes: {this.PlaneConfig}, " +
    $"Palette: {this.PaletteOrder}, " +
    $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
