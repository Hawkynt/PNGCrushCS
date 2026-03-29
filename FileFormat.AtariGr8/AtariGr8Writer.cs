using System;

namespace FileFormat.AtariGr8;

/// <summary>Assembles Atari 8-bit Graphics Mode 8 screen dump bytes from an <see cref="AtariGr8File"/>.</summary>
public static class AtariGr8Writer {

  public static byte[] ToBytes(AtariGr8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariGr8File.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, AtariGr8File.FileSize)).CopyTo(result);
    return result;
  }
}
