using FileFormat.Ico;

namespace Optimizer.Ico;

public readonly record struct IcoOptimizationCombo(
  IcoImageFormat[] EntryFormats
);
