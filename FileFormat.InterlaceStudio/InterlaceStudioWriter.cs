using System;

namespace FileFormat.InterlaceStudio;

/// <summary>Assembles Interlace Studio (.ist) file bytes from an InterlaceStudioFile.</summary>
public static class InterlaceStudioWriter {

  public static byte[] ToBytes(InterlaceStudioFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[InterlaceStudioFile.FileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += InterlaceStudioFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    file.Bitmap1.AsSpan(0, InterlaceStudioFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    file.Screen1.AsSpan(0, InterlaceStudioFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += InterlaceStudioFile.ScreenDataSize;

    // ColorData (1000 bytes)
    file.ColorData.AsSpan(0, InterlaceStudioFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += InterlaceStudioFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    file.Bitmap2.AsSpan(0, InterlaceStudioFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += InterlaceStudioFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    file.Screen2.AsSpan(0, InterlaceStudioFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += InterlaceStudioFile.ScreenDataSize;

    // BackgroundColor (1 byte)
    result[offset] = file.BackgroundColor;

    return result;
  }
}
