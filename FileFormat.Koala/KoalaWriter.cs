using System;

namespace FileFormat.Koala;

/// <summary>Assembles Commodore 64 Koala Painter file bytes from a KoalaFile.</summary>
public static class KoalaWriter {

  public static byte[] ToBytes(KoalaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[KoalaFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += KoalaFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, KoalaFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += KoalaFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, KoalaFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += KoalaFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, KoalaFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += KoalaFile.ColorRamSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;

    return result;
  }
}
