using System;
using System.Linq;
using FileFormat.Ico;

namespace Optimizer.Ani;

/// <summary>Result of an ANI optimization run.</summary>
public readonly record struct AniOptimizationResult(
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  IcoImageFormat[] EntryFormats
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, Formats: [{string.Join(", ", this.EntryFormats)}], Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
