using System;

namespace FileFormat.LastWordFont;

/// <summary>Assembles The Last Word font (.f80) file bytes.</summary>
public static class LastWordFontWriter {

  public static byte[] ToBytes(LastWordFontFile file) {
    var result = new byte[LastWordFontFile.FileSize];

    var data = file.GlyphData ?? [];
    data.AsSpan(0, Math.Min(data.Length, LastWordFontFile.FileSize)).CopyTo(result);

    return result;
  }
}
