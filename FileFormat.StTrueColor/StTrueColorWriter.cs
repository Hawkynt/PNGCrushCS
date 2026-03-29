using System;

namespace FileFormat.StTrueColor;

/// <summary>Assembles ST True Color file bytes from an in-memory representation.</summary>
public static class StTrueColorWriter {

  public static byte[] ToBytes(StTrueColorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[StTrueColorFile.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, StTrueColorFile.FileSize)).CopyTo(result.AsSpan(0));

    return result;
  }
}
