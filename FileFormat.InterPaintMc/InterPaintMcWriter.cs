using System;

namespace FileFormat.InterPaintMc;

/// <summary>Assembles Commodore 64 InterPaint Multicolor file bytes from an InterPaintMcFile.</summary>
public static class InterPaintMcWriter {

  public static byte[] ToBytes(InterPaintMcFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[InterPaintMcFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += InterPaintMcFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, InterPaintMcFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += InterPaintMcFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, InterPaintMcFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += InterPaintMcFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, InterPaintMcFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += InterPaintMcFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
