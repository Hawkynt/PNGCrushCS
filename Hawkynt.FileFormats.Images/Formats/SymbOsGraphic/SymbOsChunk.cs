namespace FileFormat.SymbOsGraphic;

/// <summary>One tile of a SymbOS graphic, and where in the picture it belongs.</summary>
public readonly record struct SymbOsChunk {

  /// <summary>Offset of the chunk's pixels.</summary>
  public int DataOffset { get; init; }

  /// <summary>Bytes one of the chunk's rows occupies.</summary>
  public int Stride { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Pixels from the left of the picture.</summary>
  public int Left { get; init; }

  /// <summary>Rows from the top of the picture.</summary>
  public int Top { get; init; }

  /// <summary>Whether the chunk draws from sixteen colours rather than four.</summary>
  public bool IsWide { get; init; }
}
