using System;
using System.Collections.Generic;
using FileFormat.Pcx;

namespace Optimizer.Pcx;

public sealed record PcxOptimizationOptions(
  List<PcxColorMode>? ColorModes = null,
  List<PcxPlaneConfig>? PlaneConfigs = null,
  List<PcxPaletteOrder>? PaletteOrders = null,
  bool AutoSelectColorMode = true,
  int MaxParallelTasks = 0
) {
  public List<PcxColorMode> ColorModes { get; init; } = ColorModes ?? [
    PcxColorMode.Original
  ];

  public List<PcxPlaneConfig> PlaneConfigs { get; init; } = PlaneConfigs ?? [
    PcxPlaneConfig.SinglePlane,
    PcxPlaneConfig.SeparatePlanes
  ];

  public List<PcxPaletteOrder> PaletteOrders { get; init; } = PaletteOrders ?? [
    PcxPaletteOrder.Original,
    PcxPaletteOrder.FrequencySorted
  ];

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
