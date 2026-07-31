using System;
using System.IO;

namespace FileFormat.ZxBigFont;

/// <summary>Reads ZX Spectrum big fonts from bytes, streams, or file paths.</summary>
public static class ZxBigFontReader {

  public static ZxBigFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Big font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxBigFontFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static ZxBigFontFile FromSpan(ReadOnlySpan<byte> data) {
    // Five bytes of signature and 256 two-byte offsets have to be there before anything else can be
    // read, and every offset has to be checked before the sheet's size is known.
    if (data.Length < ZxBigFontFile.OffsetTableStart + ZxBigFontFile.CharacterCount * 2
        || data[0] != 'C' || data[1] != 'H' || data[2] != 'X' || data[3] != 0 || data[4] != 0)
      throw new InvalidDataException("Not a big font.");

    int maxColumns = 0, maxRows = 0;
    for (var character = 0; character < ZxBigFontFile.CharacterCount; ++character) {
      var offset = ZxBigFontFile.TileOffset(data, character);
      if (offset == 0)
        continue;

      if (offset + 2 >= data.Length)
        throw new InvalidDataException($"Character {character} starts past the end of the font.");

      var transparent = data[offset];
      if (transparent > 1)
        throw new InvalidDataException($"Character {character} is neither opaque nor transparent.");

      int columns = data[offset + 1], rows = data[offset + 2];

      // A transparent character stores eight bytes a cell rather than nine: it has no attributes.
      if (offset + 3 + rows * columns * (9 - transparent) > data.Length)
        throw new InvalidDataException($"Character {character} runs past the end of the font.");

      maxColumns = Math.Max(maxColumns, columns);
      maxRows = Math.Max(maxRows, rows);
    }

    if (maxColumns == 0 || maxRows == 0)
      throw new InvalidDataException("A big font with no characters in it has no picture.");

    return new() { Data = data.ToArray(), MaxColumns = maxColumns, MaxRows = maxRows };
  }

  public static ZxBigFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
