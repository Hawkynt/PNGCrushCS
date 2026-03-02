using System;
using System.Collections.Generic;

namespace TiffOptimizer;

public sealed record TiffOptimizationOptions(
  List<TiffCompression>? Compressions = null,
  List<TiffPredictor>? Predictors = null,
  List<int>? StripRowCounts = null,
  bool AutoSelectColorMode = true,
  bool DynamicStripSizing = true,
  bool TryTiles = false,
  List<int>? TileSizes = null,
  int MaxParallelTasks = 0,
  int ZopfliIterations = 15,
  bool EnableTwoPhaseOptimization = true,
  int Phase2CandidateCount = 5
) {
  public List<TiffCompression> Compressions { get; init; } = Compressions ?? [
    TiffCompression.None,
    TiffCompression.PackBits,
    TiffCompression.Lzw,
    TiffCompression.Deflate,
    TiffCompression.DeflateUltra
  ];

  public List<TiffPredictor> Predictors { get; init; } = Predictors ?? [
    TiffPredictor.None,
    TiffPredictor.HorizontalDifferencing
  ];

  public List<int> StripRowCounts { get; init; } = StripRowCounts ?? [1, 8, 16, 64];

  public List<int> TileSizes { get; init; } = TileSizes ?? [64, 128, 256];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;

  public int ZopfliIterations { get; init; } = ZopfliIterations < 1 ? 1 : ZopfliIterations;
}
