using System;

namespace Optimizer.Gif;

public readonly record struct GifOptimizationResult(
  PaletteReorderStrategy PaletteStrategy,
  bool UsedGlobalColorTable,
  long CompressedSize,
  int FrameCount,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() {
    return $"Size: {this.CompressedSize} bytes, " +
           $"Palette: {this.PaletteStrategy}, " +
           $"GCT: {this.UsedGlobalColorTable}, " +
           $"Frames: {this.FrameCount}, " +
           $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
  }
}
