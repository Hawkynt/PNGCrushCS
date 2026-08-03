using System;

namespace FileFormat.ScitexCt;

/// <summary>Assembles Scitex CT file bytes from pixel data.</summary>
public static class ScitexCtWriter {

  /// <summary>Turns pixels back into separations, a whole row of each per row of picture.</summary>
  private static byte[] _ChunkyToSeparations(byte[] chunky, int width, int height, ScitexCtColorMode mode) {
    var channels = mode switch {
      ScitexCtColorMode.Grayscale => 1,
      ScitexCtColorMode.Rgb => 3,
      _ => 4,
    };

    if (channels == 1 || chunky == null)
      return chunky ?? [];

    var separations = new byte[chunky.Length];

    for (var row = 0; row < height; ++row) {
      var rowStart = row * width * channels;
      if (rowStart + width * channels > chunky.Length)
        break;

      for (var channel = 0; channel < channels; ++channel)
        for (var column = 0; column < width; ++column)
          separations[rowStart + channel * width + column] = chunky[rowStart + column * channels + channel];
    }

    return separations;
  }

  public static byte[] ToBytes(ScitexCtFile file) {
    ArgumentNullException.ThrowIfNull(file);
    // Written back one separation per row, which is how a real file holds it.
    return Assemble(_ChunkyToSeparations(file.PixelData, file.Width, file.Height, file.ColorMode), file.Width, file.Height, file.ColorMode, file.HResolution);
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
