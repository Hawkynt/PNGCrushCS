using System;
using System.IO;

namespace FileFormat.Dragon;

/// <summary>Reads Dragon 32/64 PMODE 4 screen files from bytes, streams, or file paths.</summary>
public static class DragonReader {

  public static DragonFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dragon file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DragonFile FromStream(Stream stream) {
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

  public static DragonFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DragonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != DragonFile.FileSize)
      throw new InvalidDataException($"Invalid Dragon data size: expected exactly {DragonFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DragonFile.FileSize];
    data.AsSpan(0, DragonFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
