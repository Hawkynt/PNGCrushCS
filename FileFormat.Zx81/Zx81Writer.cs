using System;

namespace FileFormat.Zx81;

/// <summary>Assembles Sinclair ZX81 display file file bytes.</summary>
public static class Zx81Writer {

  public static byte[] ToBytes(Zx81File file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[Zx81File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, Zx81File.FileSize)).CopyTo(result);
    return result;
  }
}
