using System;
using System.Collections.Generic;
using FileFormat.Bmp;

namespace Optimizer.Bmp;

public sealed record BmpOptimizationOptions(
  List<BmpColorMode>? ColorModes = null,
  List<BmpCompression>? Compressions = null,
  List<BmpRowOrder>? RowOrders = null,
  bool AutoSelectColorMode = true,
  int MaxParallelTasks = 0
) {
  public List<BmpColorMode> ColorModes { get; init; } = ColorModes ?? [
    BmpColorMode.Original
  ];

  public List<BmpCompression> Compressions { get; init; } = Compressions ?? [
    BmpCompression.None,
    BmpCompression.Rle8,
    BmpCompression.Rle4
  ];

  public List<BmpRowOrder> RowOrders { get; init; } = RowOrders ?? [
    BmpRowOrder.TopDown,
    BmpRowOrder.BottomUp
  ];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
