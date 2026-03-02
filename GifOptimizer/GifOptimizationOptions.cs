using System;
using System.Collections.Generic;

namespace GifOptimizer;

public sealed record GifOptimizationOptions(
  List<PaletteReorderStrategy>? PaletteStrategies = null,
  bool TryGlobalColorTable = true,
  bool TryLocalColorTable = true,
  bool OptimizeDisposal = true,
  bool TrimMargins = true,
  bool TryDeferredClear = true,
  bool DeduplicateFrames = true,
  bool TryFrameDifferencing = true,
  bool TryCompressionAwareDisposal = true,
  int MaxParallelTasks = 0,
  bool EnableTwoPhaseOptimization = true,
  int Phase2CandidateCount = 5
) {
  public List<PaletteReorderStrategy> PaletteStrategies { get; init; } = PaletteStrategies ?? [
    PaletteReorderStrategy.Original,
    PaletteReorderStrategy.FrequencySorted,
    PaletteReorderStrategy.LuminanceSorted,
    PaletteReorderStrategy.LzwRunAware
  ];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
