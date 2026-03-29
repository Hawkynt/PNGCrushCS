using System;

namespace FileFormat.EggPaint;

/// <summary>Assembles Commodore 64 Egg Paint file bytes from an EggPaintFile.</summary>
public static class EggPaintWriter {

  public static byte[] ToBytes(EggPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EggPaintFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += EggPaintFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, EggPaintFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += EggPaintFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, EggPaintFile.VideoMatrixSize)).CopyTo(result.AsSpan(offset));
    offset += EggPaintFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, EggPaintFile.ColorRamSize)).CopyTo(result.AsSpan(offset));
    offset += EggPaintFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
