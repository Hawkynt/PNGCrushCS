using System;

namespace FileFormat.BbcMicro;

/// <summary>Assembles BBC Micro screen memory dump bytes from a <see cref="BbcMicroFile"/>.</summary>
public static class BbcMicroWriter {

  public static byte[] ToBytes(BbcMicroFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Convert from linear scanline order back to character-block layout
    return BbcMicroLayoutConverter.LinearToCharacterBlock(
      file.PixelData,
      file.Width,
      file.Height,
      file.Mode
    );
  }
}
