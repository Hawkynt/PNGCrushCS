using System;

namespace FileFormat.Centauri;

/// <summary>Assembles Commodore 64 Centauri paint file bytes from a CentauriFile.</summary>
public static class CentauriWriter {

  public static byte[] ToBytes(CentauriFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CentauriFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += CentauriFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, CentauriFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += CentauriFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, CentauriFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += CentauriFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, CentauriFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += CentauriFile.ColorRamSize;

    // Border color (1 byte)
    result[offset] = file.BorderColor;
    ++offset;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Padding (14 bytes)
    file.Padding.AsSpan(0, CentauriFile.PaddingSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
