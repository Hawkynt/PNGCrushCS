using System;
using System.IO;

namespace FileFormat.JetGraphicsPlanner;

/// <summary>Assembles a Jet Graphics Planner font: the executable header, then the glyphs.</summary>
/// <remarks>
/// The file is an Atari executable whose only content is the glyph data, so the header is two
/// marker bytes and the addresses the data would be loaded between — which have to describe exactly
/// as many bytes as follow, or nothing will recognise it.
/// </remarks>
public static class JetGraphicsPlannerWriter {

  /// <summary>Where the glyphs would sit in the machine's memory.</summary>
  private const int _LoadAddress = 0x8000;

  public static byte[] ToBytes(JetGraphicsPlannerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var glyphs = file.GlyphData ?? new byte[JetGraphicsPlannerFile.GlyphDataSize];
    var result = new byte[JetGraphicsPlannerFile.FileSize];

    result[0] = 0xFF;
    result[1] = 0xFF;
    result[2] = (byte)(_LoadAddress & 0xFF);
    result[3] = (byte)((_LoadAddress >> 8) & 0xFF);

    var end = _LoadAddress + JetGraphicsPlannerFile.GlyphDataSize - 1;
    result[4] = (byte)end;
    result[5] = (byte)(end >> 8);

    glyphs.AsSpan(0, Math.Min(glyphs.Length, JetGraphicsPlannerFile.GlyphDataSize))
      .CopyTo(result.AsSpan(JetGraphicsPlannerFile.HeaderSize));

    return result;
  }

  public static void ToFile(JetGraphicsPlannerFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
