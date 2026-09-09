using System;

namespace FileFormat.Bob;

/// <summary>Assembles Bob Raytracer image file bytes.</summary>
public static class BobWriter {

  public static byte[] ToBytes(BobFile file) {
    if (file.Width is <= 0 or > ushort.MaxValue)
      throw new ArgumentException($"Bob width must be in the range 1..{ushort.MaxValue}; got {file.Width}.", nameof(file));
    if (file.Height is <= 0 or > ushort.MaxValue)
      throw new ArgumentException($"Bob height must be in the range 1..{ushort.MaxValue}; got {file.Height}.", nameof(file));

    var pixelCount = checked(file.Width * file.Height);
    var pixels = file.PixelData ?? throw new ArgumentException("Bob pixel data is required.", nameof(file));
    if (pixels.Length != pixelCount)
      throw new ArgumentException($"Bob requires exactly {pixelCount} pixel bytes; got {pixels.Length}.", nameof(file));

    var palette = file.Palette ?? throw new ArgumentException("Bob palette data is required.", nameof(file));
    if (palette.Length != BobFile.PaletteSize)
      throw new ArgumentException($"Bob requires exactly {BobFile.PaletteSize} palette bytes; got {palette.Length}.", nameof(file));

    var result = new byte[checked(BobFile.PixelOffset + pixelCount)];

    result[0] = (byte)file.Width;
    result[1] = (byte)(file.Width >> 8);
    result[2] = (byte)file.Height;
    result[3] = (byte)(file.Height >> 8);

    palette.CopyTo(result, BobFile.HeaderSize);
    pixels.CopyTo(result, BobFile.PixelOffset);

    return result;
  }
}
