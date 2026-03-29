using System;
using System.IO;

namespace FileFormat.Pkm;

/// <summary>Assembles PKM file bytes from a PkmFile data model.</summary>
public static class PkmWriter {

  public static byte[] ToBytes(PkmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var header = new PkmHeader(
      Magic1: (byte)'P',
      Magic2: (byte)'K',
      Magic3: (byte)'M',
      Magic4: (byte)' ',
      Version1: (byte)file.Version[0],
      Version2: (byte)file.Version[1],
      Format: (ushort)file.Format,
      PaddedWidth: (ushort)file.PaddedWidth,
      PaddedHeight: (ushort)file.PaddedHeight,
      Width: (ushort)file.Width,
      Height: (ushort)file.Height
    );

    var result = new byte[PkmHeader.StructSize + file.CompressedData.Length];
    header.WriteTo(result.AsSpan());
    file.CompressedData.AsSpan(0, file.CompressedData.Length).CopyTo(result.AsSpan(PkmHeader.StructSize));

    return result;
  }
}
