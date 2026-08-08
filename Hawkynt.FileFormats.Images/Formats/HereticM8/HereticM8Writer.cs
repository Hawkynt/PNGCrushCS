using System;
using System.Buffers.Binary;

namespace FileFormat.HereticM8;

/// <summary>Assembles a Heretic II texture: the version, the level tables, the palette, the pixels.</summary>
/// <remarks>
/// The three tables each hold sixteen entries — a width, a height and an offset per mipmap level.
/// Only level zero is filled; the rest stay at nought, which states that the file holds no smaller
/// copies rather than that it holds empty ones.
/// </remarks>
public static class HereticM8Writer {

  public static byte[] ToBytes(HereticM8File file) {
    var pixels = file.PixelData ?? [];
    var palette = file.Palette ?? [];
    var needed = file.Width * file.Height;
    var pixelsAt = HereticM8File.PaletteOffset + 768;

    var result = new byte[pixelsAt + needed];
    BinaryPrimitives.WriteInt32LittleEndian(result, HereticM8File.Version);

    var heightsAt = HereticM8File.WidthsOffset + HereticM8File.Levels * 4;
    var offsetsAt = HereticM8File.WidthsOffset + HereticM8File.Levels * 8;
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HereticM8File.WidthsOffset), file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(heightsAt), file.Height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offsetsAt), pixelsAt);

    palette.AsSpan(0, Math.Min(palette.Length, 768)).CopyTo(result.AsSpan(HereticM8File.PaletteOffset));
    pixels.AsSpan(0, Math.Min(pixels.Length, needed)).CopyTo(result.AsSpan(pixelsAt));

    return result;
  }
}
