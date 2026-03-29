using System;

namespace FileFormat.HiresBitmap;

/// <summary>Assembles Hires Bitmap (.hbm) file bytes from a HiresBitmapFile.</summary>
public static class HiresBitmapWriter {

  public static byte[] ToBytes(HiresBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + TrailingData
    var totalSize = HiresBitmapFile.LoadAddressSize + HiresBitmapFile.BitmapDataSize + HiresBitmapFile.ScreenDataSize + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += HiresBitmapFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, HiresBitmapFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += HiresBitmapFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, HiresBitmapFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += HiresBitmapFile.ScreenDataSize;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
