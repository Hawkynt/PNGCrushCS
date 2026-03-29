using System;

namespace FileFormat.FacePainter;

/// <summary>Assembles Commodore 64 Face Painter file bytes from a FacePainterFile.</summary>
public static class FacePainterWriter {

  public static byte[] ToBytes(FacePainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FacePainterFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FacePainterFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FacePainterFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += FacePainterFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, FacePainterFile.VideoMatrixSize)).CopyTo(result.AsSpan(offset));
    offset += FacePainterFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, FacePainterFile.ColorRamSize)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
