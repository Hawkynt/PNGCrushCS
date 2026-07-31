using System;

namespace FileFormat.Picasso64;

/// <summary>Assembles Picasso 64 picture bytes from a <see cref="Picasso64File"/>.</summary>
public static class Picasso64Writer {

  public static byte[] ToBytes(Picasso64File file) {
    var result = new byte[Picasso64File.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];
    var colors = file.ColorRam ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, Picasso64File.BitmapDataSize))
      .CopyTo(result.AsSpan(Picasso64File.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, Picasso64File.VideoMatrixSize))
      .CopyTo(result.AsSpan(Picasso64File.VideoMatrixOffset));
    colors.AsSpan(0, Math.Min(colors.Length, Picasso64File.ColorRamSize))
      .CopyTo(result.AsSpan(Picasso64File.ColorRamOffset));

    if (Picasso64File.BackgroundOffset >= 0)
      result[Picasso64File.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
