using System;

namespace FileFormat.AtariGraphics10;

/// <summary>Assembles Atari Graphics 10 (GTIA 9-color) image bytes from an <see cref="AtariGraphics10File"/>.</summary>
public static class AtariGraphics10Writer {

  public static byte[] ToBytes(AtariGraphics10File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGraphics10File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariGraphics10File.FileSize)).CopyTo(result);
    return result;
  }
}
