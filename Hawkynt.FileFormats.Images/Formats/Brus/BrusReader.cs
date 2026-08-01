using System;
using System.IO;
using System.Text;

namespace FileFormat.Brus;

/// <summary>Reads BRUS pictures from bytes, streams, or file paths.</summary>
public static class BrusReader {

  public static BrusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BrusFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static BrusFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 20
        || Encoding.ASCII.GetString(data.Slice(2, BrusFile.Signature.Length)) != BrusFile.Signature
        || data[6] != 4 || data[10] != 1 || data[11] != 2)
      throw new InvalidDataException("Not a BRUS picture.");

    int columns = data[12];
    if (columns == 0 || columns > BrusFile.MaxColumns)
      throw new InvalidDataException($"A BRUS picture is 1 to {BrusFile.MaxColumns} columns, not {columns}.");

    var height = data[13] | (data[14] << 8);
    if (height == 0 || height > BrusFile.MaxHeight)
      throw new InvalidDataException($"A BRUS picture is 1 to {BrusFile.MaxHeight} rows, not {height}.");

    var offset = BrusFile.StreamOffset;
    var bitmap = _Unpack(data, ref offset, height * columns);

    // The colour chunk is optional, and a file without one is simply black on white.
    byte[]? colors = null;
    if (offset + 4 < data.Length
        && Encoding.ASCII.GetString(data.Slice(offset, 4)) == "COLR") {
      offset += 4;

      var bands = (height + 7) >> 3;
      colors = new byte[bands * (columns << 1)];
      for (var band = 0; band < bands; ++band) {
        var chunk = _Unpack(data, ref offset, columns << 1);
        chunk.CopyTo(colors.AsSpan(band * (columns << 1)));
      }
    }

    return new() { Columns = columns, Height = height, Bitmap = bitmap, Colors = colors };
  }

  /// <summary>
  /// Unpacks one run-length coded block: a byte under 128 introduces that many literals, one above
  /// it repeats the next byte that many times less 128.
  /// </summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> data, ref int offset, int length) {
    var unpacked = new byte[length];
    var written = 0;

    while (written < length) {
      if (offset >= data.Length)
        throw new InvalidDataException("A BRUS picture's packed stream ends early.");

      var command = data[offset++];
      if (command < 128) {
        for (var i = 0; i < command && written < length; ++i) {
          if (offset >= data.Length)
            throw new InvalidDataException("A BRUS picture's packed stream ends early.");

          unpacked[written++] = data[offset++];
        }

        continue;
      }

      if (offset >= data.Length)
        throw new InvalidDataException("A BRUS picture's packed stream ends early.");

      var value = data[offset++];
      for (var i = 0; i < command - 128 && written < length; ++i)
        unpacked[written++] = value;
    }

    return unpacked;
  }

  public static BrusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
