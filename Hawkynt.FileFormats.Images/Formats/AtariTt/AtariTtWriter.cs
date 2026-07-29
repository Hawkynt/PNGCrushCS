using System;
using System.Buffers.Binary;

namespace FileFormat.AtariTt;

/// <summary>Assembles Atari TT screen bytes from an <see cref="AtariTtFile"/>.</summary>
public static class AtariTtWriter {

  public static byte[] ToBytes(AtariTtFile file) {
    var resolution = file.Resolution;
    var result = new byte[AtariTtFile.FileSizeFor(resolution)];
    result[1] = (byte)resolution;

    var palette = file.Palette ?? [];
    var count = AtariTtFile.PaletteCountFor(resolution);
    for (var i = 0; i < count; ++i)
      BinaryPrimitives.WriteInt16BigEndian(
        result.AsSpan(AtariTtFile.PaletteOffset + i * 2, 2), i < palette.Length ? palette[i] : (short)0);

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, AtariTtFile.BitmapDataSize))
      .CopyTo(result.AsSpan(AtariTtFile.BitmapOffsetFor(resolution)));

    return result;
  }
}
