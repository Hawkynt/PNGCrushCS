using System;

namespace Optimizer.Cur;

public sealed record CurOptimizationOptions(
  int MaxParallelTasks = 0
) {
  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
