using System;

namespace FileFormat.Atari16x16Font;

/// <summary>Assembles a 16x16 font from an <see cref="Atari16x16FontFile"/>.</summary>
public static class Atari16x16FontWriter {

  /// <summary>Writes the executable header and the character set it declares.</summary>
  /// <remarks>
  /// The header is two marker bytes and the address range the segment occupies, and the range has
  /// to come out exactly the size of the glyph data — a reader tells this format from any other
  /// 1030-byte file by checking that, so getting the end address wrong makes the file unreadable
  /// rather than merely odd.
  /// </remarks>
  public static byte[] ToBytes(Atari16x16FontFile file) {
    var glyphs = file.GlyphData ?? [];
    var data = new byte[Atari16x16FontFile.FileSize];

    var start = file.LoadAddress;
    var end = start + Atari16x16FontFile.GlyphDataSize - 1;

    data[0] = data[1] = 0xFF;
    data[2] = (byte)start;
    data[3] = (byte)(start >> 8);
    data[4] = (byte)end;
    data[5] = (byte)(end >> 8);

    glyphs
      .AsSpan(0, Math.Min(glyphs.Length, Atari16x16FontFile.GlyphDataSize))
      .CopyTo(data.AsSpan(Atari16x16FontFile.HeaderSize));

    return data;
  }
}
