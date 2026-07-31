using System;

namespace FileFormat.AtariFontMaker;

/// <summary>Assembles a FontMaker double character set from an <see cref="AtariFontMakerFile"/>.</summary>
public static class AtariFontMakerWriter {

  /// <summary>Writes the two character sets one after the other, which is the whole file.</summary>
  public static byte[] ToBytes(AtariFontMakerFile file) {
    var glyphs = file.GlyphData ?? [];
    var data = new byte[AtariFontMakerFile.FileSize];
    glyphs.AsSpan(0, Math.Min(glyphs.Length, data.Length)).CopyTo(data);

    return data;
  }
}
