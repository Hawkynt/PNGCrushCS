using System;

namespace FileFormat.HinterGrundBild;

/// <summary>Assembles HinterGrundBild (.hgb) file bytes from a HinterGrundBildFile.</summary>
public static class HinterGrundBildWriter {

  public static byte[] ToBytes(HinterGrundBildFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + Color(1000) + BackgroundColor(1) + TrailingData
    var totalSize = HinterGrundBildFile.LoadAddressSize + HinterGrundBildFile.MinPayloadSize + 1 + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += HinterGrundBildFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, HinterGrundBildFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += HinterGrundBildFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, HinterGrundBildFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += HinterGrundBildFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorData.AsSpan(0, HinterGrundBildFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += HinterGrundBildFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
