using System;
using System.Buffers.Binary;

namespace FileFormat.AtariPi5;

/// <summary>Assembles a .pi5 picture: the mode word, the palette, then the four planes.</summary>
public static class AtariPi5Writer {

  public static byte[] ToBytes(AtariPi5File file) {
    var result = new byte[AtariPi5File.FileSize];
    BinaryPrimitives.WriteUInt16BigEndian(result, file.Mode);

    var palette = file.Palette ?? [];
    for (var i = 0; i < AtariPi5File.ColorCount; ++i)
      BinaryPrimitives.WriteUInt16BigEndian(
        result.AsSpan(AtariPi5File.PaletteOffset + i * 2), i < palette.Length ? palette[i] : (ushort)0);

    var bitmap = file.BitmapData ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, AtariPi5File.BitmapSize))
      .CopyTo(result.AsSpan(AtariPi5File.BitmapOffset));

    return result;
  }
}
