using System;
using System.IO;

namespace FileFormat.StarPainter;

/// <summary>Reads Star Painter pictures from bytes, streams, or file paths.</summary>
public static class StarPainterReader {

  public static StarPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Star Painter picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StarPainterFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static StarPainterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 10)
      throw new InvalidDataException($"A Star Painter picture is at least 10 bytes, got {data.Length}.");

    var columns = data[0];
    var rows = data[1];
    var bitmap = columns * rows * StarPainterFile.CellSize;

    // The header is in cells, so the length it implies is exact — nothing rounds and nothing pads.
    if (columns < 1 || rows < 1 || data.Length != StarPainterFile.HeaderSize + bitmap)
      throw new InvalidDataException(
        $"Not a Star Painter picture: {columns}x{rows} cells needs {StarPainterFile.HeaderSize + bitmap} bytes, got {data.Length}.");

    var pixels = new byte[bitmap];
    data.Slice(StarPainterFile.HeaderSize, bitmap).CopyTo(pixels);

    return new() { Columns = columns, Rows = rows, BitmapData = pixels };
  }

  public static StarPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
