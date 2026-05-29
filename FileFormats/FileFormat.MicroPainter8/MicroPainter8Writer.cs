using System;

namespace FileFormat.MicroPainter8;

/// <summary>Assembles Micro Painter (Atari 8-bit) image bytes from a <see cref="MicroPainter8File"/>.</summary>
public static class MicroPainter8Writer {

  public static byte[] ToBytes(MicroPainter8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MicroPainter8File.FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MicroPainter8File.FileSize)).CopyTo(result);
    return result;
  }
}
