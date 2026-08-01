using FileFormat.Sgi;

namespace Optimizer.Sgi;

/// <summary>One set of choices to try: how to store the samples, and whether to compress them.</summary>
public readonly record struct SgiOptimizationCombo(
  SgiCompression Compression,
  int Channels,
  int BytesPerChannel,
  bool KeepImageName
);
