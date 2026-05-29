using System;

namespace FileFormat.MultiPainter;

/// <summary>Assembles Commodore 64 Multi Painter file bytes from a MultiPainterFile.</summary>
public static class MultiPainterWriter {

  public static byte[] ToBytes(MultiPainterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MultiPainterFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += MultiPainterFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, MultiPainterFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += MultiPainterFile.BitmapDataSize;

    // Video matrix (1000 bytes)
    file.VideoMatrix.AsSpan(0, MultiPainterFile.VideoMatrixSize).CopyTo(result.AsSpan(offset));
    offset += MultiPainterFile.VideoMatrixSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, MultiPainterFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += MultiPainterFile.ColorRamSize;

    // Border color (1 byte)
    result[offset] = file.BorderColor;
    ++offset;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    ++offset;

    // Padding (14 bytes)
    file.Padding.AsSpan(0, MultiPainterFile.PaddingSize).CopyTo(result.AsSpan(offset));

    return result;
  }
}
