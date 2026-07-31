using System;

namespace FileFormat.Artist64;

/// <summary>Assembles Commodore 64 Artist 64 file bytes from an Artist64File.</summary>
public static class Artist64Writer {

  public static byte[] ToBytes(Artist64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Artist64File.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, Artist64File.BitmapDataSize))
      .CopyTo(result.AsSpan(Artist64File.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, Artist64File.VideoMatrixSize))
      .CopyTo(result.AsSpan(Artist64File.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, Artist64File.ColorRamSize))
      .CopyTo(result.AsSpan(Artist64File.ColorRamOffset));
    result[Artist64File.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
