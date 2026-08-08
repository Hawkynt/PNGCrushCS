using System;
using System.IO;

namespace FileFormat.SuperHiresStudio;

/// <summary>Assembles Super Hires Studio (.shs) file bytes.</summary>
public static class SuperHiresStudioWriter {

  public static byte[] ToBytes(SuperHiresStudioFile file) {
    var data = file.Data ?? [];
    if (data.Length != SuperHiresStudioFile.FileSize)
      throw new InvalidDataException(
        $"A Super Hires Studio picture is {SuperHiresStudioFile.FileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
