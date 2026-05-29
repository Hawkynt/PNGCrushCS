using System;

namespace FileFormat.ArtStudio8;

/// <summary>Assembles Art Studio (Atari 8-bit) image bytes from an <see cref="ArtStudio8File"/>.</summary>
public static class ArtStudio8Writer {

  public static byte[] ToBytes(ArtStudio8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ArtStudio8File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ArtStudio8File.FileSize)).CopyTo(result);
    return result;
  }
}
