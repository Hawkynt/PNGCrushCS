using System;
using System.Collections.Generic;
using FileFormat.Jpeg;

namespace Optimizer.Jpeg;

public sealed record JpegOptimizationOptions(
  List<JpegMode>? Modes = null,
  bool AllowLossy = false,
  int MinQuality = 75,
  List<int>? Qualities = null,
  List<JpegSubsampling>? Subsamplings = null,
  bool StripMetadata = true,
  int MaxParallelTasks = 0
) {
  public List<JpegMode> Modes { get; init; } = Modes ?? [
    JpegMode.Baseline,
    JpegMode.Progressive
  ];

  public List<int> Qualities { get; init; } = Qualities ?? [75, 80, 85, 90, 95];

  public List<JpegSubsampling> Subsamplings { get; init; } = Subsamplings ?? [
    JpegSubsampling.Chroma444,
    JpegSubsampling.Chroma420
  ];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;

  public int MinQuality { get; init; } = MinQuality < 1 ? 1 : MinQuality > 100 ? 100 : MinQuality;
}
