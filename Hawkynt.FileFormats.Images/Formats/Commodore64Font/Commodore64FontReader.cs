using System;
using System.IO;

namespace FileFormat.Commodore64Font;

/// <summary>Reads Commodore 64 character sets from bytes, streams, or file paths.</summary>
public static class Commodore64FontReader {

  public static Commodore64FontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Character set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Commodore64FontFile FromStream(Stream stream) {
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

  public static Commodore64FontFile FromSpan(ReadOnlySpan<byte> data) {
    // The load address is the only header there is, and it is what tells the two sets apart: a
    // SEUCK set is exactly 64 glyphs loading at $0042, anything else is a plain character set
    // whose low byte is zero.
    if (data.Length == Commodore64FontFile.SeuckFileSize
        && data[0] == Commodore64FontFile.SeuckLoadAddressLow && data[1] == 0)
      return _Build(data, Commodore64FontKind.SeuckFont);

    if (data.Length < Commodore64FontFile.MinFileSize || data.Length > Commodore64FontFile.MaxFileSize)
      throw new InvalidDataException(
        $"A character set is between {Commodore64FontFile.MinFileSize} and {Commodore64FontFile.MaxFileSize} bytes, got {data.Length}.");
    if (data[0] != 0)
      throw new InvalidDataException("Not a character set: the load address does not start on a page boundary.");

    return _Build(data, Commodore64FontKind.CharacterSet);
  }

  private static Commodore64FontFile _Build(ReadOnlySpan<byte> data, Commodore64FontKind kind)
    => new() { Kind = kind, GlyphData = data[Commodore64FontFile.HeaderSize..].ToArray() };

  public static Commodore64FontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
