using System;

namespace FileFormat.C16Plus4;

/// <summary>Assembles Commodore 16/Plus4 multicolor screen file bytes.</summary>
public static class C16Plus4Writer {

  public static byte[] ToBytes(C16Plus4File file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[C16Plus4File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, C16Plus4File.FileSize)).CopyTo(result);
    return result;
  }
}
