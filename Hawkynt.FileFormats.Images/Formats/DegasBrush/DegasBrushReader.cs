using System;
using System.IO;

namespace FileFormat.DegasBrush;

/// <summary>Reads DEGAS Elite brushes from bytes, streams, or file paths.</summary>
public static class DegasBrushReader {

  public static DegasBrushFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Brush not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DegasBrushFile FromStream(Stream stream) {
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

  public static DegasBrushFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != DegasBrushFile.FileSize)
      throw new InvalidDataException($"A brush is {DegasBrushFile.FileSize} bytes, got {data.Length}.");

    // Being made only of zeroes and ones is the whole of the signature.
    foreach (var b in data)
      if (b > 1)
        throw new InvalidDataException("Not a DEGAS Elite brush: a pixel is neither set nor clear.");

    return new() { Shape = data.ToArray() };
  }

  public static DegasBrushFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
