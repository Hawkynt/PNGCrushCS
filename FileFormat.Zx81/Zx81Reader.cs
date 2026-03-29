using System;
using System.IO;

namespace FileFormat.Zx81;

/// <summary>Reads Sinclair ZX81 display file files from bytes, streams, or file paths.</summary>
public static class Zx81Reader {

  public static Zx81File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Zx81 file not found.", file.FullName);

    if (file.Length != Zx81File.FileSize)
      throw new InvalidDataException($"Invalid Zx81 data size: expected exactly {Zx81File.FileSize} bytes, got {file.Length}.");

    var buffer = new byte[Zx81File.FileSize];
    using var fs = file.OpenRead();
    fs.ReadExactly(buffer);
    return new() { PixelData = buffer };
  }

  public static Zx81File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var buffer = new byte[Zx81File.FileSize];
    try {
      stream.ReadExactly(buffer);
    } catch (EndOfStreamException) {
      throw new InvalidDataException($"Invalid Zx81 data size: expected at least {Zx81File.FileSize} bytes.");
    }

    return new() { PixelData = buffer };
  }

  public static Zx81File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Zx81File.FileSize)
      throw new InvalidDataException($"Invalid Zx81 data size: expected exactly {Zx81File.FileSize} bytes, got {data.Length}.");

    return new() { PixelData = data[..Zx81File.FileSize] };
  }
}
