namespace FileFormat.Commodore64Font;

/// <summary>The two character sets that share this layout, told apart by their load address.</summary>
public enum Commodore64FontKind {

  /// <summary>A .64c character set, up to 256 glyphs.</summary>
  CharacterSet,

  /// <summary>A SEUCK .g character set, always 64 glyphs.</summary>
  SeuckFont,
}
