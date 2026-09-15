using System;
using System.IO;

namespace FileFormat.SeuckSprites;

/// <summary>Reads SEUCK sprite sets from bytes, streams, or file paths.</summary>
public static class SeuckSpritesReader {

  public static SeuckSpritesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sprite set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SeuckSpritesFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static SeuckSpritesFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SeuckSpritesFile.FileSize || data[0] != 66 || data[1] != 0)
      throw new InvalidDataException("Not a SEUCK sprite set.");

    return new() { Data = data.ToArray() };
  }

  public static SeuckSpritesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
