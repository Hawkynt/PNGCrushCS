using System;
using System.Linq;
using FileFormat.Ico;

namespace Optimizer.Ico;

public readonly record struct IcoOptimizationResult(
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  IcoImageFormat[] EntryFormats
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, Formats: [{string.Join(", ", this.EntryFormats)}], Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
