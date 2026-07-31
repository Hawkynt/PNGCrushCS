using System;

namespace FileFormat.PaintMagic;

/// <summary>Assembles Paint Magic picture bytes from a <see cref="PaintMagicFile"/>.</summary>
public static class PaintMagicWriter {

  public static byte[] ToBytes(PaintMagicFile file) {
    var result = new byte[PaintMagicFile.ExpectedFileSize];

    var bitmap = file.BitmapData ?? [];
    var matrix = file.VideoMatrix ?? [];

    bitmap.AsSpan(0, Math.Min(bitmap.Length, PaintMagicFile.BitmapDataSize))
      .CopyTo(result.AsSpan(PaintMagicFile.BitmapOffset));
    matrix.AsSpan(0, Math.Min(matrix.Length, PaintMagicFile.VideoMatrixSize))
      .CopyTo(result.AsSpan(PaintMagicFile.VideoMatrixOffset));

    result[PaintMagicFile.BackgroundOffset] = file.BackgroundColor;
    result[PaintMagicFile.SharedColorOffset] = file.SharedColor;

    return result;
  }
}
