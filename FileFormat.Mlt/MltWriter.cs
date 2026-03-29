using System;

namespace FileFormat.Mlt;

/// <summary>Assembles Mlt (.mlt) file bytes from a MltFile.</summary>
public static class MltWriter {

  public static byte[] ToBytes(MltFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + Color(1000) + BackgroundColor(1) + TrailingData
    var totalSize = MltFile.MinFileSize + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += MltFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, MltFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += MltFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, MltFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += MltFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorData.AsSpan(0, MltFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += MltFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
