using System;
using System.IO;

namespace FileFormat.ArtStudioWindow;

/// <summary>Reads Art Studio windows from bytes, streams, or file paths.</summary>
public static class ArtStudioWindowReader {

  public static ArtStudioWindowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Window not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArtStudioWindowFile FromStream(Stream stream) {
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

  public static ArtStudioWindowFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ArtStudioWindowFile.CellsOffset + 2)
      throw new InvalidDataException($"Not an Art Studio window: {data.Length} bytes.");

    // The stored width counts multicolour pixels, each of which is drawn two screen pixels wide.
    var width = data[3] << 1;
    var height = data[4];

    var left = (data[1] & 3) << 1;
    var cellsPerRow = ((width + 7) >> 3) + (left != 0 ? 1 : 0);

    var top = data[2] & 7;
    var rows = ((height + 7) >> 3) + (top != 0 ? 1 : 0);

    if (data.Length != ArtStudioWindowFile.CellsOffset + rows * cellsPerRow * ArtStudioWindowFile.CellLength)
      throw new InvalidDataException($"A {width}x{height} window does not occupy {data.Length} bytes.");

    return new() { Data = data.ToArray(), Width = width, Height = height, CellsPerRow = cellsPerRow, Left = left, Top = top };
  }

  public static ArtStudioWindowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
