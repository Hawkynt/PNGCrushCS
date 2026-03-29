using System;

namespace FileFormat.Dragon;

/// <summary>Assembles Dragon 32/64 PMODE 4 screen file bytes.</summary>
public static class DragonWriter {

  public static byte[] ToBytes(DragonFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[DragonFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DragonFile.FileSize)).CopyTo(result);
    return result;
  }
}
