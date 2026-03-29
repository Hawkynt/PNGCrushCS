namespace FileFormat.AtariAgp;

/// <summary>Graphics mode detected from AGP file size.</summary>
public enum AtariAgpMode {
  /// <summary>Graphics 8: 320x192, 1bpp monochrome (7680 bytes raw).</summary>
  Graphics8 = 0,
  /// <summary>Graphics 7: 160x96, 2bpp, 4 colors (3840 bytes raw).</summary>
  Graphics7 = 1,
  /// <summary>Graphics 8 with 2 appended color bytes (7682 bytes).</summary>
  Graphics8WithColors = 2,
}
