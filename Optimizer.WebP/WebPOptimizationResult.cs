using System;

namespace Optimizer.WebP;

/// <summary>Result of a WebP optimization attempt.</summary>
public readonly record struct WebPOptimizationResult(
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  bool MetadataStripped
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, MetadataStripped: {this.MetadataStripped}, Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
