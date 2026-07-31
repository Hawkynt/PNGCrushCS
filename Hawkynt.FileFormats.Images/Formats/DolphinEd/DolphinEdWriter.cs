using System;

namespace FileFormat.DolphinEd;

/// <summary>Assembles Dolphin Ed C64 multicolor file bytes from a DolphinEdFile.</summary>
public static class DolphinEdWriter {

  public static byte[] ToBytes(DolphinEdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DolphinEdFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, DolphinEdFile.BitmapDataSize))
      .CopyTo(result.AsSpan(DolphinEdFile.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, DolphinEdFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(DolphinEdFile.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, DolphinEdFile.ColorRamSize))
      .CopyTo(result.AsSpan(DolphinEdFile.ColorRamOffset));
    result[DolphinEdFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
