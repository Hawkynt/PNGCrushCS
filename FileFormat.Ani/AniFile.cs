using System.Collections.Generic;
using FileFormat.Ico;

namespace FileFormat.Ani;

/// <summary>In-memory representation of an ANI animated cursor file.</summary>
public sealed class AniFile {
  public required AniHeader Header { get; init; }
  public IReadOnlyList<IcoFile> Frames { get; init; } = [];
  public int[]? Rates { get; init; }
  public int[]? Sequence { get; init; }
}
