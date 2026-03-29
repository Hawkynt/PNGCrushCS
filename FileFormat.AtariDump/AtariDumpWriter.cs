using System;

namespace FileFormat.AtariDump;

/// <summary>Assembles generic Atari 8-bit screen dump bytes from an <see cref="AtariDumpFile"/>.</summary>
public static class AtariDumpWriter {

  public static byte[] ToBytes(AtariDumpFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerLine = file.Width / 8;
    var expectedSize = bytesPerLine * file.Height;
    var result = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(result);
    return result;
  }
}
