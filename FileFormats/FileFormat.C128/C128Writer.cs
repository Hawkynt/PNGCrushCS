using System;

namespace FileFormat.C128;

/// <summary>Assembles Commodore 128 VDC screen file bytes.</summary>
public static class C128Writer {

  public static byte[] ToBytes(C128File file) {
    ArgumentNullException.ThrowIfNull(file);
    var result = new byte[C128File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, C128File.FileSize)).CopyTo(result);
    return result;
  }
}
