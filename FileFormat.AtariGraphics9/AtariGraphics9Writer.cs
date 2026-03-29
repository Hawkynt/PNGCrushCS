using System;

namespace FileFormat.AtariGraphics9;

/// <summary>Assembles Atari Graphics 9 (GTIA 16-shade) image bytes from an <see cref="AtariGraphics9File"/>.</summary>
public static class AtariGraphics9Writer {

  public static byte[] ToBytes(AtariGraphics9File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGraphics9File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariGraphics9File.FileSize)).CopyTo(result);
    return result;
  }
}
