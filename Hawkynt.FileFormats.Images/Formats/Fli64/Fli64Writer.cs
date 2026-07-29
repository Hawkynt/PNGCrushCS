using System;

namespace FileFormat.Fli64;

/// <summary>Assembles FLI Designer (FLI multicolor) image file bytes from a Fli64File.</summary>
public static class Fli64Writer {

  public static byte[] ToBytes(Fli64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Fli64File.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += Fli64File.LoadAddressSize;

    // Bitmap data (8000 bytes)
    file.BitmapData.AsSpan(0, Fli64File.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += Fli64File.BitmapDataSize;

    // Per-scanline screen RAM (8000 bytes)
    file.ScreenData.AsSpan(0, Fli64File.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += Fli64File.ScreenDataSize;

    // Color RAM (1000 bytes)
    file.ColorRam.AsSpan(0, Fli64File.ColorRamSize).CopyTo(result.AsSpan(offset));
    offset += Fli64File.ColorRamSize;

    // Padding (472 bytes)
    var paddingLength = Math.Min(file.Padding.Length, Fli64File.PaddingSize);
    if (paddingLength > 0)
      file.Padding.AsSpan(0, paddingLength).CopyTo(result.AsSpan(offset));

    return result;
  }
}
