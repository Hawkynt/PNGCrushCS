using System.Collections.Generic;

namespace FileFormat.Riff;

/// <summary>A complete RIFF file with a form type and top-level elements.</summary>
public sealed class RiffFile {
  public required FourCC FormType { get; init; }
  public List<RiffChunk> Chunks { get; init; } = [];
  public List<RiffList> Lists { get; init; } = [];
}
