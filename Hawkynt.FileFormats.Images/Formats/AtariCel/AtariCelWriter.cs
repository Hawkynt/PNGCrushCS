using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.AtariCel;

/// <summary>Assembles Atari ST CEL picture bytes.</summary>
public static class AtariCelWriter {

  public static byte[] ToBytes(AtariCelFile file) {
    var stride = file.Stride;
    var result = new byte[AtariCelFile.HeaderSize + stride * file.Height];

    result[0] = 0xFF;
    result[1] = 0xFF;
    result[2] = 0;
    result[3] = 0;

    AtariStGraphics.WritePalette(
      file.Palette ?? [], AtariCelFile.PaletteColors, result.AsSpan(AtariCelFile.PaletteOffset));

    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(AtariCelFile.WidthOffset), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(AtariCelFile.HeightOffset), (ushort)file.Height);

    AtariStGraphics
      .PackBitplanes(file.PixelData ?? [], stride, AtariCelFile.Planes, file.Width, file.Height)
      .CopyTo(result, AtariCelFile.HeaderSize);

    return result;
  }
}
