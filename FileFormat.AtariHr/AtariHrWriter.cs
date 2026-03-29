using System;

namespace FileFormat.AtariHr;

/// <summary>Assembles Atari 8-bit HR hires screen dump bytes from an <see cref="AtariHrFile"/>.</summary>
public static class AtariHrWriter {

  public static byte[] ToBytes(AtariHrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariHrFile.FileSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, AtariHrFile.FileSize)).CopyTo(result);
    return result;
  }
}
