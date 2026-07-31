using System;

namespace FileFormat.ChrDollar;

/// <summary>Assembles CHR$ font bytes from a <see cref="ChrDollarFile"/>.</summary>
public static class ChrDollarWriter {

  /// <summary>Writes the signature, the three dimensions and then the cells.</summary>
  public static byte[] ToBytes(ChrDollarFile file) {
    var cells = file.Cells ?? [];
    var data = new byte[ChrDollarFile.HeaderSize + cells.Length];

    "chr$"u8.CopyTo(data);
    data[4] = (byte)file.Columns;
    data[5] = (byte)file.Rows;
    data[6] = (byte)(file.Frames * ChrDollarFile.BytesPerCell);
    cells.CopyTo(data, ChrDollarFile.HeaderSize);

    return data;
  }
}
