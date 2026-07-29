using System;

namespace FileFormat.ZxNextImage;

/// <summary>Assembles ZX Spectrum Next (.nxi) file bytes.</summary>
public static class ZxNextImageWriter {

  public static byte[] ToBytes(ZxNextImageFile file) {
    var result = new byte[ZxNextImageFile.FileSize];

    var palette = file.PaletteData ?? [];
    palette.AsSpan(0, Math.Min(palette.Length, ZxNextImageFile.PaletteDataSize)).CopyTo(result);

    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, ZxNextImageFile.PixelDataSize))
      .CopyTo(result.AsSpan(ZxNextImageFile.PixelDataOffset));

    return result;
  }
}
