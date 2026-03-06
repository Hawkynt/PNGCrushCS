using System.Collections.Generic;

namespace FileFormat.Cur;

/// <summary>In-memory representation of a CUR file.</summary>
public sealed class CurFile {
  public IReadOnlyList<CurImage> Images { get; init; } = [];
}
