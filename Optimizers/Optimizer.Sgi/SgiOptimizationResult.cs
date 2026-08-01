using System;

namespace Optimizer.Sgi;

/// <summary>The smallest encoding found, and what produced it.</summary>
public readonly record struct SgiOptimizationResult(
  SgiOptimizationCombo Combo,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, "
    + $"Compression: {this.Combo.Compression}, "
    + $"Channels: {this.Combo.Channels}, "
    + $"Depth: {this.Combo.BytesPerChannel * 8}-bit, "
    + $"Name: {(this.Combo.KeepImageName ? "kept" : "dropped")}, "
    + $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
