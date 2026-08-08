using System;
using System.IO;

namespace FileFormat.SamarHiresMap;

/// <summary>Assembles SAMAR Hi-res Interlace with Map of Colours (.shc) file bytes.</summary>
public static class SamarHiresMapWriter {

  public static byte[] ToBytes(SamarHiresMapFile file) {
    var data = file.Data ?? [];
    if (data.Length != SamarHiresMapFile.FileSize)
      throw new InvalidDataException($"A SAMAR picture is {SamarHiresMapFile.FileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
