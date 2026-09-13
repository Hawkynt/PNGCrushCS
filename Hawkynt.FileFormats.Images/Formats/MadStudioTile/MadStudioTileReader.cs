using System;
using System.IO;

namespace FileFormat.MadStudioTile;

/// <summary>Reads Mad Studio ANTIC 4 tile sets from bytes, streams, or file paths.</summary>
public static class MadStudioTileReader {

  public static MadStudioTileFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Tile set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MadStudioTileFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MadStudioTileFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MadStudioTileFile.TileOffset + MadStudioTileFile.TileLength)
      throw new InvalidDataException($"Not a Mad Studio tile set: {data.Length} bytes.");

    int columns = data[0], rows = data[1];
    if (columns == 0 || columns > MadStudioTileFile.MaxColumns || rows == 0 || rows > MadStudioTileFile.MaxRows
        || data.Length != MadStudioTileFile.TileOffset + columns * rows * MadStudioTileFile.TileLength)
      throw new InvalidDataException($"Not a Mad Studio tile set: {columns}x{rows} tiles in {data.Length} bytes.");

    return new() { Data = data.ToArray(), Columns = columns, Rows = rows };
  }

  public static MadStudioTileFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
