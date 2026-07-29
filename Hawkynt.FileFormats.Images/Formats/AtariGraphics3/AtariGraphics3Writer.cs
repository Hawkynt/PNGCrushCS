using System;

namespace FileFormat.AtariGraphics3;

/// <summary>Assembles Atari 8-bit Graphics 3 screen bytes.</summary>
public static class AtariGraphics3Writer {

  public static byte[] ToBytes(AtariGraphics3File file) {
    var size = file.HasStoredColors ? AtariGraphics3File.ColoredFileSize : AtariGraphics3File.PlainFileSize;
    var result = new byte[size];

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, AtariGraphics3File.ScreenDataSize)).CopyTo(result);

    if (!file.HasStoredColors)
      return result;

    var colors = file.Colors ?? [];
    colors.AsSpan(0, Math.Min(colors.Length, AtariGraphics3File.ColorCount))
      .CopyTo(result.AsSpan(AtariGraphics3File.ScreenDataSize));

    return result;
  }
}
