using System;

namespace FileFormat.Cheese;

/// <summary>Assembles Commodore 64 Cheese paint file bytes from a CheeseFile.</summary>
public static class CheeseWriter {

  public static byte[] ToBytes(CheeseFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CheeseFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += CheeseFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, CheeseFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += CheeseFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, CheeseFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += CheeseFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, CheeseFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += CheeseFile.ColorRamSize;

    // Border color (1 byte)
    result[offset] = file.BorderColor;
    ++offset;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Padding (14 bytes)
    file.Padding.AsSpan(0, CheeseFile.PaddingSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
