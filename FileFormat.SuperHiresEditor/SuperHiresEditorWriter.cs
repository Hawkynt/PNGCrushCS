using System;

namespace FileFormat.SuperHiresEditor;

/// <summary>Assembles Super Hires Editor (.she) file bytes from a SuperHiresEditorFile.</summary>
public static class SuperHiresEditorWriter {

  public static byte[] ToBytes(SuperHiresEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var totalSize = SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize + file.TrailingData.Length;
    var result = new byte[totalSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += SuperHiresEditorFile.LoadAddressSize;

    // Bitmap 1 (8000 bytes)
    file.Bitmap1.AsSpan(0, SuperHiresEditorFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresEditorFile.BitmapDataSize;

    // Screen 1 (1000 bytes)
    file.Screen1.AsSpan(0, SuperHiresEditorFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresEditorFile.ScreenDataSize;

    // Bitmap 2 (8000 bytes)
    file.Bitmap2.AsSpan(0, SuperHiresEditorFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresEditorFile.BitmapDataSize;

    // Screen 2 (1000 bytes)
    file.Screen2.AsSpan(0, SuperHiresEditorFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += SuperHiresEditorFile.ScreenDataSize;

    // Trailing data
    if (file.TrailingData.Length > 0)
      file.TrailingData.AsSpan().CopyTo(result.AsSpan(offset));

    return result;
  }
}
