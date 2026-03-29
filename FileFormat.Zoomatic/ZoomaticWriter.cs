using System;

namespace FileFormat.Zoomatic;

/// <summary>Assembles Zoomatic (.zom) file bytes from a ZoomaticFile.</summary>
public static class ZoomaticWriter {

  public static byte[] ToBytes(ZoomaticFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + Color(1000) + BackgroundColor(1) + TrailingData
    var totalSize = ZoomaticFile.LoadAddressSize + ZoomaticFile.MinPayloadSize + 1 + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += ZoomaticFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, ZoomaticFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += ZoomaticFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, ZoomaticFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += ZoomaticFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorData.AsSpan(0, ZoomaticFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += ZoomaticFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
