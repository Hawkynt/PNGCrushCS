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

  public static DragonFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static DragonFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != DragonFile.FileSize)
      throw new InvalidDataException($"Invalid Dragon data size: expected exactly {DragonFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DragonFile.FileSize];
    data.Slice(0, DragonFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
    }

  public static DragonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != DragonFile.FileSize)
      throw new InvalidDataException($"Invalid Dragon data size: expected exactly {DragonFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[DragonFile.FileSize];
    data.AsSpan(0, DragonFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
