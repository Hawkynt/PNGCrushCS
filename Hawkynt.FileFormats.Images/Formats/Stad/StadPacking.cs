namespace FileFormat.Stad;

/// <summary>Traversal order of the compressed STAD screen bytes.</summary>
public enum StadPacking : byte {
  /// <summary>Bytes are encoded in normal row-major screen order (<c>pM85</c>).</summary>
  Horizontal,

  /// <summary>Bytes are encoded byte-column first across all 400 scanlines (<c>pM86</c>).</summary>
  Vertical,
}
