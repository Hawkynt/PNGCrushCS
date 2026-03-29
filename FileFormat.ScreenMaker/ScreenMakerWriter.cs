using System;

namespace FileFormat.ScreenMaker;

/// <summary>Assembles Screen Maker file bytes from a ScreenMakerFile.</summary>
public static class ScreenMakerWriter {

  public static byte[] ToBytes(ScreenMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var fileSize = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + pixelCount;
    var result = new byte[fileSize];
    var offset = 0;

    // Width (2 bytes, little-endian)
    result[offset] = (byte)(file.Width & 0xFF);
    result[offset + 1] = (byte)(file.Width >> 8);
    offset += 2;

    // Height (2 bytes, little-endian)
    result[offset] = (byte)(file.Height & 0xFF);
    result[offset + 1] = (byte)(file.Height >> 8);
    offset += 2;

    // Palette (768 bytes)
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, ScreenMakerFile.PaletteDataSize)).CopyTo(result.AsSpan(offset));
    offset += ScreenMakerFile.PaletteDataSize;

    // Pixel data (width x height bytes)
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelCount)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
