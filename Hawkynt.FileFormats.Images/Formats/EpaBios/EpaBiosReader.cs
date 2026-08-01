using System;
using System.IO;

namespace FileFormat.EpaBios;

/// <summary>Reads Award BIOS Logo (.epa) files from bytes, streams, or file paths.</summary>
public static class EpaBiosReader {

  public static EpaBiosFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EpaBios file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EpaBiosFile FromStream(Stream stream) {
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

  public static EpaBiosFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      throw new InvalidDataException("Too short to state its cell counts.");

    int columns = data[0], rows = data[1];
    if (columns is 0 or > EpaBiosFile.MaxColumns || rows is 0 or > EpaBiosFile.MaxRows)
      throw new InvalidDataException($"A BIOS logo is at most {EpaBiosFile.MaxColumns} by {EpaBiosFile.MaxRows} cells; this one says {columns} by {rows}.");

    // The length follows from the cell counts, so it is what tells a logo apart from anything else
    // whose first two bytes happen to be small numbers.
    var expected = EpaBiosFile.SizeOf(columns, rows);
    if (data.Length != expected)
      throw new InvalidDataException($"{columns} by {rows} cells is {expected} bytes; this file is {data.Length}.");

    var cells = columns * rows;
    return new() {
      Columns = columns,
      Rows = rows,
      Attributes = data.Slice(2, cells).ToArray(),
      Glyphs = data.Slice(2 + cells, cells * EpaBiosFile.CellHeight).ToArray(),
    };
  }

  public static EpaBiosFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
