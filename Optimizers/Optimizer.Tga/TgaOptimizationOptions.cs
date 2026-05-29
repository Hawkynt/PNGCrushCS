using System;
using System.Collections.Generic;
using FileFormat.Tga;

namespace Optimizer.Tga;

public sealed record TgaOptimizationOptions(
  List<TgaColorMode>? ColorModes = null,
  List<TgaCompression>? Compressions = null,
  List<TgaOrigin>? Origins = null,
  bool AutoSelectColorMode = true,
  int MaxParallelTasks = 0
) {
  public List<TgaColorMode> ColorModes { get; init; } = ColorModes ?? [
    TgaColorMode.Original
  ];

  public List<TgaCompression> Compressions { get; init; } = Compressions ?? [
    TgaCompression.None,
    TgaCompression.Rle
  ];

  public List<TgaOrigin> Origins { get; init; } = Origins ?? [
    TgaOrigin.BottomLeft,
    TgaOrigin.TopLeft
  ];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
