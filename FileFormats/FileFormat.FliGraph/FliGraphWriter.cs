using System;

namespace FileFormat.FliGraph;

/// <summary>Assembles FLI Graph (FLI multicolor variant) image file bytes from a FliGraphFile.</summary>
public static class FliGraphWriter {

  public static byte[] ToBytes(FliGraphFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FliGraphFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FliGraphFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, FliGraphFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += FliGraphFile.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    file.ScreenData.AsSpan(0, FliGraphFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += FliGraphFile.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, FliGraphFile.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += FliGraphFile.ColorRamSize;

    // Padding (472 bytes)
    var paddingLength = Math.Min(file.Padding.Length, FliGraphFile.PaddingSize);
    if (paddingLength > 0)
      file.Padding.AsSpan(0, paddingLength).CopyTo(result.AsSpan(offset));

    return result;
  }
}
