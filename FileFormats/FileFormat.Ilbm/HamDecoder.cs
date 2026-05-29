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
  public static byte[] Decode(byte[] indexedData, byte[] palette, int width, int height, int numPlanes) {
    ArgumentNullException.ThrowIfNull(indexedData);
    ArgumentNullException.ThrowIfNull(palette);

    var result = new byte[width * height * 3];
    var controlBits = numPlanes - 2; // 4 for HAM6, 6 for HAM8
    var controlMask = (1 << controlBits) - 1; // 0x0F for HAM6, 0x3F for HAM8
    var shift = 8 - controlBits; // 4 for HAM6, 2 for HAM8

    for (var y = 0; y < height; ++y) {
      byte r = 0, g = 0, b = 0;
      var rowOffset = y * width;

      for (var x = 0; x < width; ++x) {
        var pixel = indexedData[rowOffset + x];
        var control = pixel >> controlBits; // top 2 bits
        var value = pixel & controlMask;     // lower bits

        switch (control) {
          case 0: // Use palette color
            var palOffset = value * 3;
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
