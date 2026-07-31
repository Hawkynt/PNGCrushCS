using System;

namespace FileFormat.SaracenPaint;

/// <summary>Assembles Saracen Paint picture bytes from a <see cref="SaracenPaintFile"/>.</summary>
public static class SaracenPaintWriter {

  public static byte[] ToBytes(SaracenPaintFile file) {
    var result = new byte[SaracenPaintFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];
    var colors = file.ColorRam ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, SaracenPaintFile.BitmapDataSize))
      .CopyTo(result.AsSpan(SaracenPaintFile.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, SaracenPaintFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(SaracenPaintFile.VideoMatrixOffset));
    colors.AsSpan(0, Math.Min(colors.Length, SaracenPaintFile.ColorRamSize))
      .CopyTo(result.AsSpan(SaracenPaintFile.ColorRamOffset));

    if (SaracenPaintFile.BackgroundOffset >= 0)
      result[SaracenPaintFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
