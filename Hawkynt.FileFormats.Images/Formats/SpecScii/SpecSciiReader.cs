using System;
using System.IO;
using System.Text;

namespace FileFormat.SpecScii;

/// <summary>Reads SpecSCII pictures from bytes, streams, or file paths.</summary>
public static class SpecSciiReader {

  public static SpecSciiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpecSciiFile FromStream(Stream stream) {
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

  public static SpecSciiFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SpecSciiFile.FileSize
        || Encoding.ASCII.GetString(data[..SpecSciiFile.Signature.Length]) != SpecSciiFile.Signature
        || data[8] != 148 || data[9] != 9 || data[10] != 0 || data[11] != 0)
      throw new InvalidDataException("Not a SpecSCII picture.");

    // Every cell must name a character the set actually holds; nothing else identifies the format.
    for (var cell = 0; cell < SpecSciiFile.Columns * SpecSciiFile.Rows; ++cell)
      if (data[SpecSciiFile.ScreenOffset + cell] >= SpecSciiFile.CharacterCount)
        throw new InvalidDataException($"Cell {cell} names a character the set does not hold.");
    var stated = data[SpecSciiFile.LengthOffset]
                 | (data[SpecSciiFile.LengthOffset + 1] << 8)
                 | (data[SpecSciiFile.LengthOffset + 2] << 16)
                 | (data[SpecSciiFile.LengthOffset + 3] << 24);
    if (stated != SpecSciiFile.FileSize)
      throw new InvalidDataException(
        $"A ZX_SSCII screen states its length as {stated} rather than {SpecSciiFile.FileSize}.");


    return new() { Data = data.ToArray() };
  }

  public static SpecSciiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
