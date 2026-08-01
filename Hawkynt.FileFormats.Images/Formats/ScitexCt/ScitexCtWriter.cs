using System;

namespace FileFormat.ScitexCt;

/// <summary>Assembles Scitex CT file bytes from pixel data.</summary>
public static class ScitexCtWriter {

  public static byte[] ToBytes(ScitexCtFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.ColorMode, file.HResolution);
  }

  internal static byte[] Assemble(
    byte[] pixelData, int width, int height, ScitexCtColorMode colorMode, int resolution) {
    var channels = colorMode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      ScitexCtColorMode.Cmyk => 4,
      _ => throw new ArgumentOutOfRangeException(nameof(colorMode), colorMode, "Unknown color mode."),
    };

    var result = new byte[ScitexCtHeader.StructSize + width * height * channels];
    ScitexCtHeader.Write(result, width, height, colorMode, resolution <= 0 ? 300 : resolution);

    pixelData.AsSpan(0, Math.Min(result.Length - ScitexCtHeader.StructSize, pixelData.Length))
      .CopyTo(result.AsSpan(ScitexCtHeader.StructSize));

    return result;
  }
}
