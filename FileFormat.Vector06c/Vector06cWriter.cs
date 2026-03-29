using System;

namespace FileFormat.Vector06c;

/// <summary>Assembles Vector-06C screen file bytes.</summary>
public static class Vector06cWriter {

  public static byte[] ToBytes(Vector06cFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[Vector06cFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, Vector06cFile.FileSize)).CopyTo(result);
    return result;
  }
}
