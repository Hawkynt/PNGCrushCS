using System;

namespace FileFormat.Blazing;

/// <summary>Assembles Blazing Paddles hires file bytes from a BlazingFile.</summary>
public static class BlazingWriter {

  public static byte[] ToBytes(BlazingFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[BlazingFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += BlazingFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, BlazingFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += BlazingFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, BlazingFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    // Remaining 7 bytes are padding (zero-initialized by new byte[])

    return result;
  }
}
