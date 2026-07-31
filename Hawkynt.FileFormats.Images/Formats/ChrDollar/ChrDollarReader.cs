using System;
using System.IO;

namespace FileFormat.ChrDollar;

/// <summary>Reads CHR$ character sets from bytes, streams, or file paths.</summary>
public static class ChrDollarReader {

  public static ChrDollarFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CHR$ font not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ChrDollarFile FromStream(Stream stream) {
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

  public static ChrDollarFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 15 || data[0] != 'c' || data[1] != 'h' || data[2] != 'r' || data[3] != '$')
      throw new InvalidDataException("Not a CHR$ font.");

    int columns = data[4], rows = data[5], bytesPerCell = data[6];
    var frames = bytesPerCell / ChrDollarFile.BytesPerCell;
    if (bytesPerCell != frames * ChrDollarFile.BytesPerCell || frames is < 1 or > 2)
      throw new InvalidDataException($"A CHR$ cell is {bytesPerCell} bytes, which is neither one field nor two.");

    // The size is not stored, so a file whose length does not follow from its dimensions is either
    // truncated or not a font at all; there is nothing to fall back on.
    if (data.Length != ChrDollarFile.HeaderSize + rows * columns * bytesPerCell)
      throw new InvalidDataException($"A CHR$ font of {columns}x{rows} cells is not {data.Length} bytes.");

    return new() {
      Columns = columns,
      Rows = rows,
      Frames = frames,
      Cells = data[ChrDollarFile.HeaderSize..].ToArray(),
    };
  }

  public static ChrDollarFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
