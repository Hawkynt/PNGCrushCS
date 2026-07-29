using System;

namespace FileFormat.ZxRgb3;

/// <summary>Assembles ZX Spectrum RGB3 image bytes.</summary>
public static class ZxRgb3Writer {

  public static byte[] ToBytes(ZxRgb3File file) {
    var result = new byte[ZxRgb3File.FileSize];
    var data = file.BitmapData ?? [];
    data.AsSpan(0, Math.Min(data.Length, ZxRgb3File.FileSize)).CopyTo(result);

    return result;
  }
}
