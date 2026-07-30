using System;

namespace FileFormat.ColorStar;

/// <summary>Assembles ColorSTar picture bytes.</summary>
public static class ColorStarWriter {

  public static byte[] ToBytes(ColorStarFile file) {
    var result = new byte[ColorStarFile.PlainFileSize];
    _Copy(file.Palette, result, 0, ColorStarFile.PaletteSize);
    _Copy(file.BitmapData, result, ColorStarFile.PaletteSize, ColorStarFile.BitmapSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
