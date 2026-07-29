using System;

namespace FileFormat.VidigPaint;

/// <summary>Assembles Atari 8-bit Vidig Paint (.rap) screens. bytes.</summary>
public static class VidigPaintWriter {

  public static byte[] ToBytes(VidigPaintFile file) {
    var result = new byte[VidigPaintFile.FileSize];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, VidigPaintFile.HeaderSize)).CopyTo(result);

    var screen = file.ScreenData ?? [];
    screen.AsSpan(0, Math.Min(screen.Length, VidigPaintFile.ScreenDataSize))
      .CopyTo(result.AsSpan(VidigPaintFile.HeaderSize));

    result[VidigPaintFile.BackgroundColorOffset] = file.BackgroundColor;

    return result;
  }
}
