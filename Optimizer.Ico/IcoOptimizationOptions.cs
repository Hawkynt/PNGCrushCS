using System;

namespace Optimizer.Ico;

public sealed record IcoOptimizationOptions(
  int MaxParallelTasks = 0
) {
  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
