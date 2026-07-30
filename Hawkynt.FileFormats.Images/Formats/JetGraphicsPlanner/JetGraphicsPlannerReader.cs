using System;
using System.IO;

namespace FileFormat.JetGraphicsPlanner;

/// <summary>Reads Jet Graphics Planner fonts from bytes, streams, or file paths.</summary>
public static class JetGraphicsPlannerReader {

  public static JetGraphicsPlannerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JetGraphicsPlannerFile FromStream(Stream stream) {
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

  public static JetGraphicsPlannerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != JetGraphicsPlannerFile.FileSize)
      throw new InvalidDataException($"A Jet Graphics Planner font is {JetGraphicsPlannerFile.FileSize} bytes, got {data.Length}.");
    if (data[0] != 0xFF || data[1] != 0xFF)
      throw new InvalidDataException("Not a Jet Graphics Planner font: the executable header is missing.");

    // The declared segment has to be exactly the glyph data; the header alone is weak evidence.
    var start = data[2] | (data[3] << 8);
    var end = data[4] | (data[5] << 8);
    if (end - start + 1 != JetGraphicsPlannerFile.GlyphDataSize)
      throw new InvalidDataException(
        $"Not a Jet Graphics Planner font: the header declares {end - start + 1} bytes rather than {JetGraphicsPlannerFile.GlyphDataSize}.");

    var glyphs = new byte[JetGraphicsPlannerFile.GlyphDataSize];
    data.Slice(JetGraphicsPlannerFile.HeaderSize, JetGraphicsPlannerFile.GlyphDataSize).CopyTo(glyphs);

    return new() { GlyphData = glyphs };
  }

  public static JetGraphicsPlannerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
