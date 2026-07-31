using System;

namespace FileFormat.OdFontEditor;

/// <summary>Assembles an OD Font Editor character set from an <see cref="OdFontEditorFile"/>.</summary>
public static class OdFontEditorWriter {

  /// <summary>Writes the glyph data, ten bytes a glyph and nothing around it.</summary>
  public static byte[] ToBytes(OdFontEditorFile file) {
    var glyphs = file.GlyphData ?? [];
    var data = new byte[OdFontEditorFile.FileSize];
    glyphs.AsSpan(0, Math.Min(glyphs.Length, data.Length)).CopyTo(data);

    return data;
  }
}
