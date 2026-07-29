namespace FileFormat.MsxGlYjk;

/// <summary>The two readings of a YJK byte, which the file's extension selects.</summary>
public enum MsxGlYjkMode {

  /// <summary>Every pixel is YJK; the palette plays no part.</summary>
  Screen12,

  /// <summary>An odd luma escapes to a palette entry instead of naming a colour.</summary>
  Screen10,
}
