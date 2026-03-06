using System;

namespace Optimizer.WebP;

/// <summary>Configuration for WebP optimization.</summary>
public sealed record WebPOptimizationOptions(
  int MaxParallelTasks = 0,
  bool StripMetadata = true
) {
  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
