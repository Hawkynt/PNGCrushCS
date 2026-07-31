using System;

namespace FileFormat.FacePainter;

/// <summary>Assembles Commodore 64 Face Painter file bytes from a FacePainterFile.</summary>
public static class FacePainterWriter {

  public static byte[] ToBytes(FacePainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FacePainterFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FacePainterFile.BitmapDataSize))
      .CopyTo(result.AsSpan(FacePainterFile.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, FacePainterFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(FacePainterFile.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, FacePainterFile.ColorRamSize))
      .CopyTo(result.AsSpan(FacePainterFile.ColorRamOffset));
    result[FacePainterFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
