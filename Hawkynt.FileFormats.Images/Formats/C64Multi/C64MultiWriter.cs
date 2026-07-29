using System;

namespace FileFormat.C64Multi;

/// <summary>Assembles C64 multiformat art file bytes from a C64MultiFile.</summary>
public static class C64MultiWriter {

  public static byte[] ToBytes(C64MultiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Format switch {
      C64MultiFormat.ArtStudioHires => _WriteArtStudioHires(file),
      C64MultiFormat.ArtStudioMulti => _WriteArtStudioMulti(file),
      _ => throw new NotSupportedException($"Writing format {file.Format} is not supported.")
    };
  }

  private static byte[] _WriteArtStudioHires(C64MultiFile file) {
    var result = new byte[C64MultiFile.ArtStudioHiresFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += C64MultiFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, C64MultiFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += C64MultiFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, C64MultiFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += C64MultiFile.ScreenDataSize;

    // Border color (1 byte)
    result[offset] = file.BackgroundColor;
    // Remaining 6 bytes are padding (zero-initialized by new byte[])

    return result;
  }

  private static byte[] _WriteArtStudioMulti(C64MultiFile file) {
    var result = new byte[C64MultiFile.ArtStudioMultiFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += C64MultiFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, C64MultiFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += C64MultiFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    file.ScreenData.AsSpan(0, C64MultiFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += C64MultiFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    if (file.ColorData != null)
      file.ColorData.AsSpan(0, C64MultiFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += C64MultiFile.ColorDataSize;

    // Background color (1 byte)
    result[offset] = file.BackgroundColor;
    // Remaining 15 bytes are padding (zero-initialized by new byte[])

    return result;
  }
}
