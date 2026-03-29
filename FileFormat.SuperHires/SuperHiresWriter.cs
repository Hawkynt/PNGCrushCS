using System;

namespace FileFormat.SuperHires;

/// <summary>Assembles Super Hires (C64 interlace hires) file bytes from a SuperHiresFile.</summary>
public static class SuperHiresWriter {

  public static byte[] ToBytes(SuperHiresFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var paddingLength = file.Padding.Length > 0 ? file.Padding.Length : SuperHiresFile.PaddingSize;
    var totalSize = SuperHiresFile.LoadAddressSize
      + SuperHiresFile.BitmapDataSize + SuperHiresFile.ScreenDataSize
      + SuperHiresFile.BitmapDataSize + SuperHiresFile.ScreenDataSize
      + paddingLength;

    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += SuperHiresFile.LoadAddressSize;

    // Frame 1: Bitmap data (8000 bytes)
    file.BitmapData1.AsSpan(0, SuperHiresFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresFile.BitmapDataSize;

    // Frame 1: Screen RAM (1000 bytes)
    file.ScreenData1.AsSpan(0, SuperHiresFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresFile.ScreenDataSize;

    // Frame 2: Bitmap data (8000 bytes)
    file.BitmapData2.AsSpan(0, SuperHiresFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresFile.BitmapDataSize;

    // Frame 2: Screen RAM (1000 bytes)
    file.ScreenData2.AsSpan(0, SuperHiresFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresFile.ScreenDataSize;

    // Padding/extra data
    if (file.Padding.Length > 0)
      file.Padding.AsSpan(0, file.Padding.Length).CopyTo(result.AsSpan(offset));

    return result;
  }
}
