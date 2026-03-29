using System;

namespace FileFormat.AtariGraphics11;

/// <summary>Assembles Atari Graphics 11 (GTIA 16-luminance) image bytes from an <see cref="AtariGraphics11File"/>.</summary>
public static class AtariGraphics11Writer {

  public static byte[] ToBytes(AtariGraphics11File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGraphics11File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariGraphics11File.FileSize)).CopyTo(result);
    return result;
  }
}
