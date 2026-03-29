using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>A complete IFF file with a form type and top-level chunks.</summary>
public sealed class IffFile {
  public required FourCC FormType { get; init; }
  public required IReadOnlyList<IffChunk> Chunks { get; init; }
}
