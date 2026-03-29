using System;

namespace FileFormat.MicroIllustratorA8;

/// <summary>Assembles Micro Illustrator Atari 8-bit bytes from a <see cref="MicroIllustratorA8File"/>.</summary>
public static class MicroIllustratorA8Writer {

  public static byte[] ToBytes(MicroIllustratorA8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MicroIllustratorA8File.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MicroIllustratorA8File.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
