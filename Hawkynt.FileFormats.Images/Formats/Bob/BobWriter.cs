using System;

namespace FileFormat.Bob;

/// <summary>Assembles Bob Raytracer image file bytes.</summary>
public static class BobWriter {

  public static byte[] ToBytes(BobFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[BobFile.PixelOffset + file.Width * file.Height];

    result[0] = (byte)file.Width;
    result[1] = (byte)(file.Width >> 8);
    result[2] = (byte)file.Height;
    result[3] = (byte)(file.Height >> 8);

    var palette = file.Palette ?? [];
    palette.AsSpan(0, Math.Min(palette.Length, BobFile.PaletteSize)).CopyTo(result.AsSpan(BobFile.HeaderSize));
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length - BobFile.PixelOffset)).CopyTo(result.AsSpan(BobFile.PixelOffset));

    return result;
  }
}
