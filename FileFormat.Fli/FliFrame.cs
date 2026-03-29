using System.Collections.Generic;

namespace FileFormat.Fli;

/// <summary>A single frame within a FLI/FLC animation.</summary>
public sealed class FliFrame {
  public IReadOnlyList<FliFrameChunk> Chunks { get; init; } = [];
}
