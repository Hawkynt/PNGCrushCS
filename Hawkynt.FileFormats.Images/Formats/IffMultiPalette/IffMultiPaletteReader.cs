using System;
using System.IO;

namespace FileFormat.IffMultiPalette;

/// <summary>Reads IFF Multi-Palette images from bytes, streams, or file paths.</summary>
public static class IffMultiPaletteReader {

  public static IffMultiPaletteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Multi-Palette file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffMultiPaletteFile FromStream(Stream stream) {
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

  public static IffMultiPaletteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>
  /// Reads a Multi-Palette picture.
  /// </summary>
  /// <remarks>
  /// This used to take anything at all that was twelve bytes or longer: there was no check that the
  /// file is an IFF one, and where no bitmap header could be found it invented a size and returned a
  /// picture of it. So an unrelated 129-byte file opened as a blank 320 by 200 page and counted as a
  /// decode, while the format that could have read it never saw it.
  /// <para/>
  /// One of these is an IFF file, so it begins with FORM, and it carries its size in a BMHD chunk.
  /// Without both there is nothing here to read.
  /// </remarks>
  public static IffMultiPaletteFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IffMultiPaletteFile.MinFileSize)
      throw new InvalidDataException($"Invalid Multi-Palette data: expected at least {IffMultiPaletteFile.MinFileSize} bytes, got {data.Length}.");

    if (!data[..4].SequenceEqual("FORM"u8))
      throw new InvalidDataException("Not a Multi-Palette picture: an IFF file begins with FORM.");

    if (!_TryParseBmhd(data, out var width, out var height))
      throw new InvalidDataException("Not a Multi-Palette picture: it carries no BMHD chunk to state its size.");

    return new() {
      Width = width,
      Height = height,
      RawData = data.ToArray(),
    };
  }

  /// <returns>Whether a bitmap header was found and stated a size.</returns>
  private static bool _TryParseBmhd(ReadOnlySpan<byte> data, out int width, out int height) {
    width = IffMultiPaletteFile.DefaultWidth;
    height = IffMultiPaletteFile.DefaultHeight;

    for (var i = 0; i < data.Length - 24; ++i) {
      if (data[i] != 0x42 || data[i + 1] != 0x4D || data[i + 2] != 0x48 || data[i + 3] != 0x44)
        continue;

      var offset = i + 8;
      if (offset + 4 > data.Length)
        return false;

      width = (data[offset] << 8) | data[offset + 1];
      height = (data[offset + 2] << 8) | data[offset + 3];

      // A header stating nothing usable is not a size, and inventing one in its place is what put
      // blank pages where a refusal belonged.
      return width > 0 && height > 0;
    }

    return false;
  }
}
