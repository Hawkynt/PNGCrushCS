using System.Collections.Generic;

namespace FileFormat.Riff;

/// <summary>A RIFF LIST containing a list type and sub-elements (chunks and nested lists).</summary>
public sealed class RiffList {
  public required FourCC ListType { get; init; }
  public List<RiffChunk> Chunks { get; init; } = [];
  public List<RiffList> SubLists { get; init; } = [];
}
