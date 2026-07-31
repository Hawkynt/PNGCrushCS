using System;

namespace FileFormat.RainbowPainter;

/// <summary>Assembles Rainbow Painter picture bytes from a <see cref="RainbowPainterFile"/>.</summary>
public static class RainbowPainterWriter {

  public static byte[] ToBytes(RainbowPainterFile file) {
    var result = new byte[RainbowPainterFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];
    var colors = file.ColorRam ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, RainbowPainterFile.BitmapDataSize))
      .CopyTo(result.AsSpan(RainbowPainterFile.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, RainbowPainterFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(RainbowPainterFile.VideoMatrixOffset));
    colors.AsSpan(0, Math.Min(colors.Length, RainbowPainterFile.ColorRamSize))
      .CopyTo(result.AsSpan(RainbowPainterFile.ColorRamOffset));

    if (RainbowPainterFile.BackgroundOffset >= 0)
      result[RainbowPainterFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
