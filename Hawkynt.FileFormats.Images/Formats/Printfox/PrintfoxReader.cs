using System;
using System.IO;

namespace FileFormat.Printfox;

/// <summary>Reads Printfox pictures from bytes, streams, or file paths.</summary>
public static class PrintfoxReader {

  /// <summary>The byte that introduces a run.</summary>
  private const int _ESCAPE = 155;

  public static PrintfoxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PrintfoxFile FromStream(Stream stream) {
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

  public static PrintfoxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("Not a Printfox picture: too short for a header.");

    var at = 1;
    int columns, rows;

    switch (data[0]) {
      case (byte)'B':
        columns = 40;
        rows = 25;
        break;

      case (byte)'G':
        columns = 80;
        rows = 50;
        break;

      case (byte)'P':
        columns = data[2];
        rows = data[1];

        // A named block: the name follows the size and ends where the picture starts.
        while (at < data.Length && data[at++] != 0) { }

        if (at >= data.Length)
          throw new InvalidDataException("A Printfox block's name never ends.");

        break;

      default:
        throw new InvalidDataException($"'{(char)data[0]}' is not a kind of Printfox picture.");
    }

    if (columns == 0 || rows == 0)
      throw new InvalidDataException($"A Printfox picture of {columns}x{rows} cells is empty.");

    // A block counts its runs in one byte and the two fixed sizes in two, so the unpacker has to
    // know which it is reading rather than only what it is reading into.
    var block = data[0] == 'P';
    var cells = new byte[rows * columns * PrintfoxFile.CellSize];
    var count = 0;
    var value = 0;

    for (var i = 0; i < cells.Length; ++i) {
      while (count == 0) {
        if (at >= data.Length)
          throw new InvalidDataException("A Printfox picture ends before its cells do.");

        value = data[at++];
        if (value != _ESCAPE) {
          count = 1;
          continue;
        }

        if (at >= data.Length)
          throw new InvalidDataException("A Printfox run has no length.");

        count = data[at++];
        if (block) {
          // A single byte, so a length of zero has to mean the longest run rather than none.
          if (count == 0)
            count = 256;
        } else {
          if (at >= data.Length)
            throw new InvalidDataException("A Printfox run has half a length.");

          count += data[at++] << 8;
        }

        if (at >= data.Length)
          throw new InvalidDataException("A Printfox run has no value.");

        value = data[at++];
      }

      --count;
      cells[i] = (byte)value;
    }

    return new() { Columns = columns, Rows = rows, Cells = cells };
  }

  public static PrintfoxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
