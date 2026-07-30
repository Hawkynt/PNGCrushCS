using System;
using System.IO;

namespace FileFormat.Atari16x16Font;

/// <summary>Reads Atari 8-bit 16x16 fonts from bytes, streams, or file paths.</summary>
public static class Atari16x16FontReader {

  public static Atari16x16FontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Atari16x16FontFile FromStream(Stream stream) {
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

  public static Atari16x16FontFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != Atari16x16FontFile.FileSize)
      throw new InvalidDataException($"A 16x16 font is {Atari16x16FontFile.FileSize} bytes, got {data.Length}.");

    // An Atari executable header, and the segment it declares has to be exactly the glyph data —
    // which is most of what separates this from any other 1030-byte file.
    if (data[0] != 0xFF || data[1] != 0xFF)
      throw new InvalidDataException("Not a 16x16 font: the executable header is missing.");

    var start = data[2] | (data[3] << 8);
    var end = data[4] | (data[5] << 8);
    if (end - start + 1 != Atari16x16FontFile.GlyphDataSize)
      throw new InvalidDataException(
        $"Not a 16x16 font: the header declares {end - start + 1} bytes rather than {Atari16x16FontFile.GlyphDataSize}.");

    var glyphs = new byte[Atari16x16FontFile.GlyphDataSize];
    data.Slice(Atari16x16FontFile.HeaderSize, Atari16x16FontFile.GlyphDataSize).CopyTo(glyphs);

    return new() { GlyphData = glyphs };
  }

  public static Atari16x16FontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
