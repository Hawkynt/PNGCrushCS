using System;

namespace FileFormat.PaintMagic;

/// <summary>Assembles Paint Magic C64 multicolor file bytes from a PaintMagicFile.</summary>
public static class PaintMagicWriter {

  public static byte[] ToBytes(PaintMagicFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[PaintMagicFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += PaintMagicFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, PaintMagicFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += PaintMagicFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, PaintMagicFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += PaintMagicFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, PaintMagicFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += PaintMagicFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
