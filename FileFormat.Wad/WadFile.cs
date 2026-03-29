using System.Collections.Generic;

namespace FileFormat.Wad;

/// <summary>In-memory representation of a Doom WAD (Where's All the Data) file.</summary>
public sealed class WadFile {
  /// <summary>Whether this is an IWAD or PWAD.</summary>
  public WadType Type { get; init; }

  /// <summary>Named lumps contained in the WAD.</summary>
  public IReadOnlyList<WadLump> Lumps { get; init; } = [];
}
