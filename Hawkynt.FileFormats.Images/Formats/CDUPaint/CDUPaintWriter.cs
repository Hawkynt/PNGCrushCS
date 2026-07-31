using System;

namespace FileFormat.CDUPaint;

/// <summary>Assembles Commodore 64 CDU-Paint file bytes from a CDUPaintFile.</summary>
public static class CDUPaintWriter {

  public static byte[] ToBytes(CDUPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CDUPaintFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, CDUPaintFile.BitmapDataSize))
      .CopyTo(result.AsSpan(CDUPaintFile.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, CDUPaintFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(CDUPaintFile.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, CDUPaintFile.ColorRamSize))
      .CopyTo(result.AsSpan(CDUPaintFile.ColorRamOffset));
    result[CDUPaintFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
