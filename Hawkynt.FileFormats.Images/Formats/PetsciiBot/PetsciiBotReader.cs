using System;
using System.IO;

namespace FileFormat.PetsciiBot;

/// <summary>Reads PETSCII BOT pictures from bytes, streams, or file paths.</summary>
public static class PetsciiBotReader {

  public static PetsciiBotFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PETSCII BOT picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PetsciiBotFile FromStream(Stream stream) {
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

  public static PetsciiBotFile FromSpan(ReadOnlySpan<byte> data) {
    // Two shapes, and since each is a colour and a character per cell the length gives both away.
    var (columns, rows) = data.Length switch {
      70 => (PetsciiBotFile.SmallColumns, PetsciiBotFile.SmallRows),
      384 => (PetsciiBotFile.LargeColumns, PetsciiBotFile.LargeRows),
      _ => throw new InvalidDataException($"A PETSCII BOT picture is 70 or 384 bytes, got {data.Length}."),
    };

    return new() { Columns = columns, Rows = rows, Data = data.ToArray() };
  }

  public static PetsciiBotFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
