using System;

namespace FileFormat.DirLogoMaker;

/// <summary>Assembles Dir Logo Maker logo bytes from a <see cref="DirLogoMakerFile"/>.</summary>
public static class DirLogoMakerWriter {

  /// <summary>
  /// Writes the sixteen directory entries, each holding one row of the logo in its name field.
  /// </summary>
  /// <remarks>
  /// The rest of every entry is what a directory would have held — a size, some flags — and none of
  /// it is the picture's. It is left at nought, which is what an empty entry looks like and what
  /// the reader ignores.
  /// </remarks>
  public static byte[] ToBytes(DirLogoMakerFile file) {
    var characters = file.Characters ?? [];
    var data = new byte[DirLogoMakerFile.FileSize];

    for (var row = 0; row < DirLogoMakerFile.Rows; ++row)
    for (var column = 0; column < DirLogoMakerFile.Columns; ++column) {
      var at = row * DirLogoMakerFile.Columns + column;
      var code = at < characters.Length ? characters[at] : (byte)0;
      data[row * DirLogoMakerFile.EntrySize + DirLogoMakerFile.NameOffset + column] = ToAscii(code);
    }

    return data;
  }

  /// <summary>Translates the machine's own character order back into ASCII.</summary>
  /// <remarks>
  /// The exact inverse of the reader's translation. The three blocks of thirty-two rotate back by
  /// one block and everything above 96 was already in place, so every code has an ASCII spelling —
  /// which is why the logo could live in a filename at all.
  /// </remarks>
  public static byte ToAscii(byte code) {
    var inverse = code & 128;

    return (byte)(inverse | ((code & 127) switch {
      >= 64 and <= 95 => (code & 127) - 64,
      <= 63 => (code & 127) + 32,
      _ => code & 127,
    }));
  }
}
