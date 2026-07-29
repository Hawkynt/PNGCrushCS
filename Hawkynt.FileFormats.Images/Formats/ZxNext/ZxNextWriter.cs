using System;

namespace FileFormat.ZxNext;

/// <summary>Assembles ZX Spectrum Next (.nxt) file bytes from a <see cref="ZxNextFile"/>.</summary>
public static class ZxNextWriter {

  public static byte[] ToBytes(ZxNextFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ZxNextReader.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ZxNextReader.FileSize)).CopyTo(result.AsSpan(0));

    return result;
  }
}
