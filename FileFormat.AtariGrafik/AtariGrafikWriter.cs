using System;
using System.Buffers.Binary;

namespace FileFormat.AtariGrafik;

/// <summary>Assembles Atari Grafik PCP file bytes from an AtariGrafikFile.</summary>
public static class AtariGrafikWriter {

  public static byte[] ToBytes(AtariGrafikFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGrafikFile.ExpectedFileSize];
    var span = result.AsSpan();

    BinaryPrimitives.WriteInt16BigEndian(span, file.Resolution);

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span[(2 + i * 2)..], file.Palette[i]);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariGrafikFile.PixelDataSize)).CopyTo(result.AsSpan(AtariGrafikFile.HeaderSize));

    return result;
  }
}
