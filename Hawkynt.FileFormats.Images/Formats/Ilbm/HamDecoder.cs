using System;

namespace FileFormat.Ilbm;

/// <summary>Decodes HAM6 and HAM8 Hold-And-Modify pixel data to RGB.</summary>
internal static class HamDecoder {

  /// <summary>Decodes HAM-encoded indexed pixel data to RGB byte array.</summary>
  /// <param name="indexedData">Indexed pixel data (one byte per pixel, values 0..2^numPlanes-1).</param>
  /// <param name="palette">RGB palette (3 bytes per entry).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="numPlanes">Number of bitplanes (6 for HAM6, 8 for HAM8).</param>
  /// <returns>RGB pixel data (3 bytes per pixel).</returns>
  public static byte[] Decode(byte[] indexedData, byte[] palette, int width, int height, int numPlanes)
    => Decode(indexedData, palette, width, height, numPlanes, 0);

  /// <summary>Decodes HAM pixel data, optionally against a palette that changes down the screen.</summary>
  /// <param name="perScanline">
  /// Colours each scanline's palette holds, or zero when one palette serves the whole picture. A
  /// sliced picture states fewer palettes than it has lines when the display is interlaced, in which
  /// case each serves two.
  /// </param>
  public static byte[] Decode(byte[] indexedData, byte[] palette, int width, int height, int numPlanes, int perScanline) {
    ArgumentNullException.ThrowIfNull(indexedData);
    ArgumentNullException.ThrowIfNull(palette);

    var result = new byte[width * height * 3];
    var controlBits = numPlanes - 2; // 4 for HAM6, 6 for HAM8
    var controlMask = (1 << controlBits) - 1; // 0x0F for HAM6, 0x3F for HAM8
    var shift = 8 - controlBits; // 4 for HAM6, 2 for HAM8

    // A scanline starts from the background colour, which is the palette's first entry, and not from
    // black. The two are usually close enough to pass unnoticed, and differ in exactly the first one
    // or two pixels of every row — the holding carries the border colour in until something modifies
    // each channel in turn.
    var slices = perScanline > 0 ? palette.Length / (perScanline * 3) : 0;

    for (var y = 0; y < height; ++y) {
      // A sliced picture states one palette a line, or one per two when the display is interlaced.
      var at = slices == 0 ? 0
        : (slices >= height ? y : y * slices / height) * perScanline * 3;

      byte r = 0, g = 0, b = 0;
      if (at + 2 < palette.Length) {
        r = palette[at];
        g = palette[at + 1];
        b = palette[at + 2];
      }

      var rowOffset = y * width;

      for (var x = 0; x < width; ++x) {
        var pixel = indexedData[rowOffset + x];
        var control = pixel >> controlBits; // top 2 bits
        var value = pixel & controlMask;     // lower bits

        switch (control) {
          case 0: // Use palette color
            var palOffset = at + value * 3;
            if (palOffset + 2 < palette.Length) {
              r = palette[palOffset];
              g = palette[palOffset + 1];
              b = palette[palOffset + 2];
            }
            break;
          case 1: // Modify blue
            b = (byte)(value << shift | value >> (controlBits - shift));
            break;
          case 2: // Modify red
            r = (byte)(value << shift | value >> (controlBits - shift));
            break;
          case 3: // Modify green
            g = (byte)(value << shift | value >> (controlBits - shift));
            break;
        }

        var outOffset = (rowOffset + x) * 3;
        result[outOffset] = r;
        result[outOffset + 1] = g;
        result[outOffset + 2] = b;
      }
    }

    return result;
  }
}
