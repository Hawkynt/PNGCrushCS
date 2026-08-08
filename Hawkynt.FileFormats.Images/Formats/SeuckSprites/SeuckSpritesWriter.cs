using System;
using System.IO;

namespace FileFormat.SeuckSprites;

/// <summary>Assembles SEUCK sprite set (.a) file bytes.</summary>
public static class SeuckSpritesWriter {

  public static byte[] ToBytes(SeuckSpritesFile file) {
    var data = file.Data ?? [];
    if (data.Length != SeuckSpritesFile.FileSize)
      throw new InvalidDataException(
        $"A SEUCK sprite set is {SeuckSpritesFile.FileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
