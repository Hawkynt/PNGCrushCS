using System;

namespace FileFormat.Cheese;

/// <summary>Assembles Commodore 64 Cheese paint file bytes from a CheeseFile.</summary>
public static class CheeseWriter {

  public static byte[] ToBytes(CheeseFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CheeseFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, CheeseFile.BitmapDataSize))
      .CopyTo(result.AsSpan(CheeseFile.BitmapOffset));
    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, CheeseFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(CheeseFile.VideoMatrixOffset));
    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, CheeseFile.ColorRamSize))
      .CopyTo(result.AsSpan(CheeseFile.ColorRamOffset));
    result[CheeseFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
