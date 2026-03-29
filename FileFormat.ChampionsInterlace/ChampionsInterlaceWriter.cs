using System;

namespace FileFormat.ChampionsInterlace;

/// <summary>Assembles Champions Interlace (.cin) file bytes from a ChampionsInterlaceFile.</summary>
public static class ChampionsInterlaceWriter {

  public static byte[] ToBytes(ChampionsInterlaceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ChampionsInterlaceFile.FileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += ChampionsInterlaceFile.LoadAddressSize;

    // Bitmap1 (8000 bytes)
    file.Bitmap1.AsSpan(0, ChampionsInterlaceFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen1 (1000 bytes)
    file.Screen1.AsSpan(0, ChampionsInterlaceFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // ColorData (1000 bytes)
    file.ColorData.AsSpan(0, ChampionsInterlaceFile.ColorDataSize).CopyTo(result.AsSpan(offset));
    offset += ChampionsInterlaceFile.ColorDataSize;

    // Bitmap2 (8000 bytes)
    file.Bitmap2.AsSpan(0, ChampionsInterlaceFile.BitmapDataSize).CopyTo(result.AsSpan(offset));
    offset += ChampionsInterlaceFile.BitmapDataSize;

    // Screen2 (1000 bytes)
    file.Screen2.AsSpan(0, ChampionsInterlaceFile.ScreenDataSize).CopyTo(result.AsSpan(offset));
    offset += ChampionsInterlaceFile.ScreenDataSize;

    // BackgroundColor (1 byte)
    result[offset] = file.BackgroundColor;

    return result;
  }
}
