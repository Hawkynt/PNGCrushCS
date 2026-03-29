using System;

namespace FileFormat.AtariAnticMode;

/// <summary>Assembles Atari ANTIC Mode E/F Screen bytes: 7680 data bytes + 1 mode byte.</summary>
public static class AtariAnticModeWriter {

  public static byte[] ToBytes(AtariAnticModeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AtariAnticModeFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, AtariAnticModeFile.ScreenDataSize)).CopyTo(result);
    result[AtariAnticModeFile.ScreenDataSize] = file.Mode;
    return result;
  }
}
