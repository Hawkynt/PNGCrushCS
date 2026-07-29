using System;

namespace FileFormat.ZxFont;

/// <summary>Assembles ZX Spectrum character-set bytes.</summary>
public static class ZxFontWriter {

  public static byte[] ToBytes(ZxFontFile file) {
    var data = file.GlyphData ?? [];
    var result = new byte[data.Length];
    data.CopyTo(result, 0);
    return result;
  }
}
