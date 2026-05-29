using System;

namespace Optimizer.Ani;

/// <summary>Configuration options for ANI optimization.</summary>
public sealed record AniOptimizationOptions(
  int MaxParallelTasks = 0
) {
  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
