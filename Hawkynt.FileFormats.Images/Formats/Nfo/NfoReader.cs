using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Nfo;

public static class NfoReader {

  public static NfoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NFO file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NfoFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static NfoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static NfoFile FromSpan(ReadOnlySpan<byte> data) {
    // Split on CR/LF (handles CRLF, LF, and bare CR). Discard a trailing EOF (0x1A) byte if present.
    var lines = new List<byte[]>();
    var i = 0;
    var len = data.Length;
    if (len > 0 && data[len - 1] == 0x1A) --len;

    while (i < len) {
      var lineStart = i;
      while (i < len && data[i] != (byte)'\r' && data[i] != (byte)'\n') ++i;
      var lineBytes = data.Slice(lineStart, i - lineStart).ToArray();
      lines.Add(lineBytes);
      // Consume the terminator (CRLF as one).
      if (i < len && data[i] == '\r') ++i;
      if (i < len && data[i] == '\n') ++i;
    }

    if (lines.Count == 0)
      return new NfoFile { ColumnCount = 0, RowCount = 0, CellBytes = [] };

    var cols = 0;
    foreach (var l in lines) if (l.Length > cols) cols = l.Length;
    if (cols < NfoFile.DefaultColumnCount) cols = NfoFile.DefaultColumnCount;
    var rows = lines.Count;

    var cells = new byte[cols * rows];
    for (var r = 0; r < rows; ++r) {
      var line = lines[r];
      for (var c = 0; c < cols; ++c)
        cells[r * cols + c] = c < line.Length ? line[c] : (byte)0x20; // pad with space
    }

    return new NfoFile { ColumnCount = cols, RowCount = rows, CellBytes = cells };
  }
}
