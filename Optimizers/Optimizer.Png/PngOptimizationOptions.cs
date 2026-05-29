using System;
using System.Collections.Generic;

namespace Optimizer.Png;

/// <summary>Configuration options for PNG optimization</summary>
public sealed record PngOptimizationOptions(
  bool AutoSelectColorMode = true,
  bool TryInterlacing = true,
  bool TryPartitioning = true,
  bool AllowLossyPalette = false,
  bool UseDithering = false,
  bool IsHighQualityQuantization = false,
  int MaxPaletteColors = 256,
  int PartitionCount = 4,
  List<FilterStrategy>? FilterStrategies = null,
  List<DeflateMethod>? DeflateMethods = null,
  List<string>? QuantizerNames = null,
  List<string>? DithererNames = null,
  bool PreserveAncillaryChunks = false,
  int MaxParallelTasks = 0,
  int ZopfliIterations = 15,
  bool EnableTwoPhaseOptimization = true,
  int Phase2CandidateCount = 5,
  bool OptimizePaletteOrder = true) {
  public int ZopfliIterations { get; init; } = ZopfliIterations < 1 ? 1 : ZopfliIterations;

  public List<FilterStrategy> FilterStrategies { get; init; } = FilterStrategies ?? [
    FilterStrategy.SingleFilter,
    FilterStrategy.ScanlineAdaptive,
    FilterStrategy.WeightedContinuity,
    FilterStrategy.PartitionOptimized
  ];

  public List<DeflateMethod> DeflateMethods { get; init; } =
    DeflateMethods ?? [DeflateMethod.Default, DeflateMethod.Ultra];

  public List<string> QuantizerNames { get; init; } = QuantizerNames ?? ["Wu", "Octree", "Median Cut"];

  public List<string> DithererNames { get; init; } = DithererNames ?? ["NoDithering_Instance", "ErrorDiffusion_FloydSteinberg"];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
