using System;

namespace FileFormat.StarPainter;

/// <summary>Assembles Star Painter picture bytes.</summary>
public static class StarPainterWriter {

  public static byte[] ToBytes(StarPainterFile file) {
    var data = file.BitmapData ?? [];
    var result = new byte[StarPainterFile.HeaderSize + data.Length];
    result[0] = (byte)file.Columns;
    result[1] = (byte)file.Rows;
    data.CopyTo(result.AsSpan(StarPainterFile.HeaderSize));

    return result;
  }
}
