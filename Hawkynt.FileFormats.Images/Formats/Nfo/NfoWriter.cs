using System;
using System.IO;

namespace FileFormat.Nfo;

public static class NfoWriter {

  /// <summary>Serialise as CRLF-terminated CP437 lines (the scene-standard line ending for NFO files).</summary>
  public static byte[] ToBytes(NfoFile file) {
    ArgumentNullException.ThrowIfNull(file.CellBytes);
    if (file.ColumnCount == 0 || file.RowCount == 0) return [];

    using var ms = new MemoryStream();
    for (var r = 0; r < file.RowCount; ++r) {
      var rowOff = r * file.ColumnCount;
      // Trim trailing spaces from each line (matches handcrafted NFO conventions).
      var lastNonSpace = file.ColumnCount - 1;
      while (lastNonSpace >= 0 && file.CellBytes[rowOff + lastNonSpace] == 0x20) --lastNonSpace;
      ms.Write(file.CellBytes, rowOff, lastNonSpace + 1);
      ms.WriteByte((byte)'\r');
      ms.WriteByte((byte)'\n');
    }
    return ms.ToArray();
  }
}
