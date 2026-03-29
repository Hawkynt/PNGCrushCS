using System;

namespace FileFormat.DoomFlat;

/// <summary>Assembles Doom flat texture lump file bytes.</summary>
public static class DoomFlatWriter {

  public static byte[] ToBytes(DoomFlatFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[DoomFlatFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DoomFlatFile.FileSize)).CopyTo(result);
    return result;
  }
}
