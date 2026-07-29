using System;

namespace FileFormat.AtariGfb;

/// <summary>Assembles Atari 8-bit GFB screen dump bytes from an <see cref="AtariGfbFile"/>.</summary>
public static class AtariGfbWriter {

  public static byte[] ToBytes(AtariGfbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGfbFile.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, AtariGfbFile.FileSize)).CopyTo(result);
    return result;
  }
}
