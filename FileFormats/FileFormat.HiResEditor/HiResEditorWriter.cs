using System;

namespace FileFormat.HiResEditor;

/// <summary>Assembles Hires Editor file bytes from a HiResEditorFile.</summary>
public static class HiResEditorWriter {

  public static byte[] ToBytes(HiResEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiResEditorFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += HiResEditorFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, HiResEditorFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += HiResEditorFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, HiResEditorFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    // Remaining 7 bytes are padding (zero-initialized by new byte[])

    return result;
  }
}
