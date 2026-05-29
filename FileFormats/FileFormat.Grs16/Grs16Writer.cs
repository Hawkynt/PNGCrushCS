using System;

namespace FileFormat.Grs16;

/// <summary>Assembles raw 16-bit grayscale file bytes from a Grs16File.</summary>
public static class Grs16Writer {

  public static byte[] ToBytes(Grs16File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.PixelData.Length];
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(0));

    return result;
  }
}
