using System;

namespace FileFormat.MonoMagic;

/// <summary>Assembles Mono Magic C64 image file bytes.</summary>
public static class MonoMagicWriter {

  public static byte[] ToBytes(MonoMagicFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[MonoMagicFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MonoMagicFile.FileSize)).CopyTo(result);
    return result;
  }
}
