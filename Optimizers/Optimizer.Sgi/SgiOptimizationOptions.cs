using System;
using System.Collections.Generic;
using FileFormat.Sgi;

namespace Optimizer.Sgi;

/// <summary>
/// What the optimizer is allowed to vary while leaving the picture untouched.
/// </summary>
/// <remarks>
/// Every one of these is lossless: the decoded pixels are identical whichever is chosen, so there is
/// nothing to trade off against size and the smallest result simply wins.
/// </remarks>
public sealed record SgiOptimizationOptions(
  List<SgiCompression>? Compressions = null,
  bool ReduceChannels = true,
  bool ReduceDepth = true,
  bool DropImageName = true,
  int MaxParallelTasks = 0
) {
  /// <summary>
  /// Which storage schemes to try. RLE is not always smaller — it costs a scanline offset table, and
  /// on noisy data the runs do not pay for it — so both are encoded and measured rather than guessed
  /// at.
  /// </summary>
  public List<SgiCompression> Compressions { get; init; } = Compressions ?? [
    SgiCompression.None,
    SgiCompression.Rle,
  ];

  /// <summary>
  /// Whether to drop channels that carry nothing: an image whose three channels are equal
  /// everywhere is a grey one written three times, and an alpha channel that is opaque everywhere
  /// says nothing at all.
  /// </summary>
  public bool ReduceChannels { get; init; } = ReduceChannels;

  /// <summary>
  /// Whether to store 16-bit samples in 8 bits when every one of them would survive the trip — which
  /// is the case whenever each sample's two bytes are equal, as they are in anything promoted from an
  /// 8-bit source.
  /// </summary>
  public bool ReduceDepth { get; init; } = ReduceDepth;

  /// <summary>Whether to drop the 80-byte name field, which no decoder needs to draw the picture.</summary>
  public bool DropImageName { get; init; } = DropImageName;

  public int MaxParallelTasks { get; init; } = MaxParallelTasks <= 0 ? Environment.ProcessorCount : MaxParallelTasks;
}
