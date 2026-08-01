using System;

namespace FileFormat.MadStudioTile;

/// <summary>Assembles a Mad Studio tile set from a <see cref="MadStudioTileFile"/>.</summary>
public static class MadStudioTileWriter {

  public static byte[] ToBytes(MadStudioTileFile file) {
    var data = file.Data ?? [];
    var size = MadStudioTileFile.TileOffset + file.Columns * file.Rows * MadStudioTileFile.TileLength;
    var result = new byte[size];
    data.AsSpan(0, Math.Min(data.Length, size)).CopyTo(result);

    result[0] = (byte)file.Columns;
    result[1] = (byte)file.Rows;

    return result;
  }
}
