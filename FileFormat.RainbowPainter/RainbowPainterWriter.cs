using System;

namespace FileFormat.RainbowPainter;

/// <summary>Assembles Commodore 64 Rainbow Painter file bytes from a RainbowPainterFile.</summary>
public static class RainbowPainterWriter {

  public static byte[] ToBytes(RainbowPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[RainbowPainterFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += RainbowPainterFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, RainbowPainterFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += RainbowPainterFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, RainbowPainterFile.VideoMatrixSize)).CopyTo(result.AsSpan(offset));
    offset += RainbowPainterFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, RainbowPainterFile.ColorRamSize)).CopyTo(result.AsSpan(offset));
    offset += RainbowPainterFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
