using System;

namespace FileFormat.Din;

/// <summary>Assembles Din (.din) file bytes from a DinFile.</summary>
public static class DinWriter {

  public static byte[] ToBytes(DinFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + Color(1000) + BackgroundColor(1) + TrailingData
    var totalSize = DinFile.LoadAddressSize + DinFile.MinPayloadSize + 1 + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += DinFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, DinFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += DinFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, DinFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += DinFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorData.AsSpan(0, DinFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += DinFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
