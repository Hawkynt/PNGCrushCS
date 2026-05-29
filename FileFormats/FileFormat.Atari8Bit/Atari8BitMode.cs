namespace FileFormat.Atari8Bit;

/// <summary>Atari 8-bit ANTIC graphics modes.</summary>
public enum Atari8BitMode {
  /// <summary>160x96, 2bpp, 4 colors (ANTIC mode E, OS GR.7).</summary>
  Gr7 = 0,
  /// <summary>320x192, 1bpp, monochrome (ANTIC mode F, OS GR.8).</summary>
  Gr8 = 1,
  /// <summary>80x192, 4bpp, 16 grayscale shades (ANTIC mode F with GTIA, OS GR.9).</summary>
  Gr9 = 2,
  /// <summary>160x192, 2bpp, 4 colors (ANTIC mode E, OS GR.15).</summary>
  Gr15 = 3,
}
