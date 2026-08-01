using System;

namespace FileFormat.Blazon;

/// <summary>Assembles Blazon picture bytes from a <see cref="BlazonFile"/>.</summary>
public static class BlazonWriter {

  public static byte[] ToBytes(BlazonFile file) {
    var result = new byte[BlazonFile.ExpectedFileSize];

    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];
    var colors = file.ColorRam ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, BlazonFile.BitmapDataSize)).CopyTo(result.AsSpan(BlazonFile.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, BlazonFile.VideoMatrixSize)).CopyTo(result.AsSpan(BlazonFile.VideoMatrixOffset));
    colors.AsSpan(0, Math.Min(colors.Length, BlazonFile.ColorRamSize)).CopyTo(result.AsSpan(BlazonFile.ColorRamOffset));
    result[BlazonFile.BackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
