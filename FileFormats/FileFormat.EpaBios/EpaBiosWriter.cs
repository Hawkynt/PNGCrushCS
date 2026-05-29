using System;

namespace FileFormat.EpaBios;

/// <summary>Assembles Award BIOS Logo (.epa) file bytes.</summary>
public static class EpaBiosWriter {

  public static byte[] ToBytes(EpaBiosFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[EpaBiosFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, EpaBiosFile.FileSize)).CopyTo(result);
    return result;
  }
}
