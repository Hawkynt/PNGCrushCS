using System;

namespace FileFormat.BugBitmap;

/// <summary>Assembles Commodore 64 Bug Bitmap file bytes from a BugBitmapFile.</summary>
public static class BugBitmapWriter {

  public static byte[] ToBytes(BugBitmapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[BugBitmapFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += BugBitmapFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, BugBitmapFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += BugBitmapFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, BugBitmapFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += BugBitmapFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, BugBitmapFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += BugBitmapFile.ColorRamSize;

    // Border color (1 byte)
    result[offset] = file.BorderColor;
    ++offset;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Padding (14 bytes)
    file.Padding.AsSpan(0, BugBitmapFile.PaddingSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
