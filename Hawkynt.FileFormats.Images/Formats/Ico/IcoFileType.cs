namespace FileFormat.Ico;

/// <summary>Which of the two formats sharing this layout a file says it is.</summary>
public enum IcoFileType : ushort {

  /// <summary>An icon: the two bytes after the reserved one are the plane count and the depth.</summary>
  Icon = 1,

  /// <summary>A cursor: those same two bytes are the hotspot instead.</summary>
  Cursor = 2
}
