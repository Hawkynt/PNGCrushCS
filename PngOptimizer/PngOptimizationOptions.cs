using System;
using System.Collections.Generic;

namespace PngOptimizer;

/// <summary>Configuration options for PNG optimization</summary>
public sealed record PngOptimizationOptions(
  bool AutoSelectColorMode = true,
  bool TryInterlacing = true,
  bool TryPartitioning = true,
  int MaxPaletteColors = 256,
  int PartitionCount = 4,
  List<FilterStrategy>? FilterStrategies = null,
  List<DeflateMethod>? DeflateMethods = null,
  int MaxParallelTasks = 0) {

  public List<FilterStrategy> FilterStrategies { get; init; } = FilterStrategies ?? [
    FilterStrategy.SingleFilter, 
    FilterStrategy.ScanlineAdaptive, 
    FilterStrategy.WeightedContinuity, 
    FilterStrategy.PartitionOptimized
  ];

  public List<DeflateMethod> DeflateMethods { get; init; } = DeflateMethods ?? [DeflateMethod.Default, DeflateMethod.Maximum];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;

}
