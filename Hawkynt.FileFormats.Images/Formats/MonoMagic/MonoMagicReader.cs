using System;
using System.IO;

namespace FileFormat.MonoMagic;

/// <summary>Reads Mono Magic C64 image files from bytes, streams, or file paths.</summary>
public static class MonoMagicReader {

  public static MonoMagicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MonoMagic file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MonoMagicFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MonoMagicFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != MonoMagicFile.FileSize)
      throw new InvalidDataException($"Invalid MonoMagic data size: expected exactly {MonoMagicFile.FileSize} bytes, got {data.Length}.");

    return new() { PixelData = MonoMagicFile.CellsToRows(data.Slice(MonoMagicFile.ScreenOffset, MonoMagicFile.ScreenSize)) };
    }

  public static MonoMagicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != MonoMagicFile.FileSize)
      throw new InvalidDataException($"Invalid MonoMagic data size: expected exactly {MonoMagicFile.FileSize} bytes, got {data.Length}.");

    return new() { PixelData = MonoMagicFile.CellsToRows(data.AsSpan(MonoMagicFile.ScreenOffset, MonoMagicFile.ScreenSize)) };
  }
}
