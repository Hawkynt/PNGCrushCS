using System;

namespace FileFormat.AtariFont;

/// <summary>Assembles Atari 8-bit character set bytes from an <see cref="AtariFontFile"/>.</summary>
public static class AtariFontWriter {

  public static byte[] ToBytes(AtariFontFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariFontFile.FileSize];
    file.FontData.AsSpan(0, Math.Min(file.FontData.Length, AtariFontFile.FileSize)).CopyTo(result);
    return result;
  }
}
