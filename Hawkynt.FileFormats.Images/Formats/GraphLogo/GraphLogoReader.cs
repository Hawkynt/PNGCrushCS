using System;
using System.IO;

namespace FileFormat.GraphLogo;

/// <summary>Reads Graph pictures from bytes, streams, or file paths.</summary>
public static class GraphLogoReader {

  public static GraphLogoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraphLogoFile FromStream(Stream stream) {
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

  public static GraphLogoFile FromSpan(ReadOnlySpan<byte> data) {
    // The character sets are the only variable part, so a valid file is always a whole number of
    // them plus the fixed head and tail.
    if ((data.Length & (GraphLogoFile.FontSize - 1)) != GraphLogoFile.LengthRemainder
        || data.Length < GraphLogoFile.LengthRemainder + GraphLogoFile.FontSize)
      throw new InvalidDataException($"Not a Graph picture: {data.Length} bytes.");

    var screenOffset = data.Length - GraphLogoFile.TrailerSize;
    for (var row = 0; row < GraphLogoFile.CharacterRows; ++row)
      if (GraphLogoFile.FontOffset + data[row] * GraphLogoFile.FontSize >= screenOffset)
        throw new InvalidDataException($"Row {row} names a character set the file does not hold.");

    return new() { Data = data.ToArray() };
  }

  public static GraphLogoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
