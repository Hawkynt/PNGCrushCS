using System;

namespace FileFormat.CDUPaint;

/// <summary>Assembles Commodore 64 CDU-Paint file bytes from a CDUPaintFile.</summary>
public static class CDUPaintWriter {

  public static byte[] ToBytes(CDUPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CDUPaintFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += CDUPaintFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, CDUPaintFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += CDUPaintFile.BitmapDataSize;

    file.VideoMatrix.AsSpan(0, Math.Min(file.VideoMatrix.Length, CDUPaintFile.VideoMatrixSize)).CopyTo(result.AsSpan(offset));
    offset += CDUPaintFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, CDUPaintFile.ColorRamSize)).CopyTo(result.AsSpan(offset));
    offset += CDUPaintFile.ColorRamSize;

    result[offset] = file.BackgroundColor;

    return result;
  }
}
