using System;

namespace FileFormat.HighResAtari;

/// <summary>Assembles Atari Hi-Res Paint image bytes from a <see cref="HighResAtariFile"/>.</summary>
public static class HighResAtariWriter {

  public static byte[] ToBytes(HighResAtariFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HighResAtariFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, HighResAtariFile.FileSize)).CopyTo(result);
    return result;
  }
}
