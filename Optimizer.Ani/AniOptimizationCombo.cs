using FileFormat.Ico;

namespace Optimizer.Ani;

/// <summary>One optimization combination: entry format per frame.</summary>
public readonly record struct AniOptimizationCombo(
  IcoImageFormat[] EntryFormats
);
