using System;

namespace FileFormat.FliDesigner2;

/// <summary>Assembles FLI Designer 2 (enhanced FLI multicolor) image file bytes from a FliDesigner2File.</summary>
public static class FliDesigner2Writer {

  public static byte[] ToBytes(FliDesigner2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var baseSize = FliDesigner2File.LoadAddressSize
      + FliDesigner2File.BitmapDataSize
      + FliDesigner2File.ScreenDataSize
      + FliDesigner2File.ColorRamSize;

    var totalSize = baseSize + file.ExtraData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FliDesigner2File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, FliDesigner2File.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += FliDesigner2File.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    file.ScreenData.AsSpan(0, FliDesigner2File.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += FliDesigner2File.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, FliDesigner2File.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += FliDesigner2File.ColorRamSize;

    // Extra data (variable length)
    if (file.ExtraData.Length > 0)
      file.ExtraData.AsSpan(0, file.ExtraData.Length).CopyTo(result.AsSpan(offset));

    return result;
  }
}
