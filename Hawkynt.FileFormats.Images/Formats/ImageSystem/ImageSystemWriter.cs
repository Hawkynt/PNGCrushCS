using System;

namespace FileFormat.ImageSystem;

/// <summary>Assembles Image System picture bytes from an <see cref="ImageSystemFile"/>.</summary>
public static class ImageSystemWriter {

  public static byte[] ToBytes(ImageSystemFile file) {
    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];

    var result = new byte[file.IsHires ? ImageSystemFile.HiresFileSize : ImageSystemFile.MulticolorFileSize];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    var bitmapOffset = file.IsHires ? ImageSystemFile.HiresBitmapOffset : ImageSystemFile.MulticolorBitmapOffset;
    var matrixOffset = file.IsHires
      ? ImageSystemFile.HiresVideoMatrixOffset
      : ImageSystemFile.MulticolorVideoMatrixOffset;

    bitmap.AsSpan(0, Math.Min(bitmap.Length, ImageSystemFile.BitmapDataSize)).CopyTo(result.AsSpan(bitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, ImageSystemFile.VideoMatrixSize)).CopyTo(result.AsSpan(matrixOffset));

    if (file.IsHires)
      return result;

    var colors = file.ColorRam ?? [];
    colors.AsSpan(0, Math.Min(colors.Length, ImageSystemFile.ColorRamSize))
      .CopyTo(result.AsSpan(ImageSystemFile.MulticolorColorRamOffset));
    result[ImageSystemFile.MulticolorBackgroundOffset] = file.BackgroundColor;

    return result;
  }
}
