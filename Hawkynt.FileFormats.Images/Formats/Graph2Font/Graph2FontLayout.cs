namespace FileFormat.Graph2Font;

/// <summary>Where a Graph2Font project's tables sit, which depends on how much it carries.</summary>
public readonly record struct Graph2FontLayout {

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public int Columns { get; init; }

  /// <summary>Offset of the character sets.</summary>
  public int FontsOffset { get; init; }

  /// <summary>Offset of the per-row character set numbers, and of everything that follows them.</summary>
  public int FontNumberOffset { get; init; }

  /// <summary>Whether the character modes draw five colours rather than four.</summary>
  public bool CharacterMode { get; init; }

  /// <summary>Offset of the second inverse table, or -1 when the project has none.</summary>
  public int Inverse2Offset { get; init; }

  /// <summary>Offset of the video upgrade's per-cell colours, or -1 when the project has none.</summary>
  public int VbxeOffset { get; init; }
}
