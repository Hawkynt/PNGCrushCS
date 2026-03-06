using FileFormat.Ico;

namespace Optimizer.Cur;

public readonly record struct CurOptimizationCombo(
  IcoImageFormat[] EntryFormats
);
