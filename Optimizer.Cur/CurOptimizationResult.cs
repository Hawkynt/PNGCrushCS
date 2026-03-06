using System;
using FileFormat.Ico;

namespace Optimizer.Cur;

public readonly record struct CurOptimizationResult(
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  IcoImageFormat[] EntryFormats
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, Formats: [{string.Join(", ", this.EntryFormats)}], Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
