using System;

namespace FileFormat.Pixel64;

/// <summary>Assembles Commodore 64 Pixel Perfect paint file bytes from a Pixel64File.</summary>
public static class Pixel64Writer {

  public static byte[] ToBytes(Pixel64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Pixel64File.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += Pixel64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, Pixel64File.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += Pixel64File.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, Pixel64File.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += Pixel64File.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, Pixel64File.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += Pixel64File.ColorRamSize;

    // Border color (1 byte)
    result[offset] = file.BorderColor;
    ++offset;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Padding (14 bytes)
    file.Padding.AsSpan(0, Pixel64File.PaddingSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
