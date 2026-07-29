using System;

namespace FileFormat.Commodore64Font;

/// <summary>Assembles Commodore 64 character set bytes.</summary>
public static class Commodore64FontWriter {

  public static byte[] ToBytes(Commodore64FontFile file) {
    var glyphs = file.GlyphData ?? [];
    var result = new byte[Commodore64FontFile.HeaderSize + glyphs.Length];

    if (file.Kind == Commodore64FontKind.SeuckFont)
      result[0] = Commodore64FontFile.SeuckLoadAddressLow;
    else
      // $0800, where a character set conventionally loads; only the zero low byte is checked.
      result[1] = 0x08;

    glyphs.CopyTo(result.AsSpan(Commodore64FontFile.HeaderSize));

    return result;
  }
}
