using System.Collections.Generic;

namespace FileFormat.Pdf.Fonts;

/// <summary>An embedded font program, reduced to what drawing text with it needs.</summary>
internal interface IPdfFontProgram {

  /// <summary>How many glyphs the program holds.</summary>
  int GlyphCount { get; }

  /// <summary>
  /// The outline of a glyph in text space, an em being one unit and y pointing up — already
  /// through the font's own matrix. Null when there is no such glyph; empty for one that draws
  /// nothing, such as a space.
  /// </summary>
  GlyphOutline? Outline(int glyphId);

  /// <summary>The glyph id a PostScript glyph name refers to, or -1.</summary>
  int GlyphIdOfName(string name);

  /// <summary>How far the glyph advances the pen, in text space, or NaN when the program does not say.</summary>
  double AdvanceWidth(int glyphId);

  /// <summary>
  /// The program's own encoding from codes 0 to 255 to glyph names, or null when it has none of
  /// its own (TrueType, CID-keyed).
  /// </summary>
  IReadOnlyDictionary<int, string>? BuiltInEncoding { get; }
}
