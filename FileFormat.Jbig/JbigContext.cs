using System;

namespace FileFormat.Jbig;

/// <summary>JBIG Template 0 context model for 10-bit context extraction from surrounding pixels.</summary>
internal static class JbigContext {

  /// <summary>Number of contexts for Template 0 (2^10 = 1024).</summary>
  internal const int ContextCount = 1024;

  /// <summary>Template 0 pixel positions relative to the current pixel (dx, dy).
  /// Positions are listed from bit 9 (MSB) to bit 0 (LSB) of the context word.</summary>
  internal static readonly (int dx, int dy)[] Template0Positions = [
    ( 1, -2),  // bit 9
    ( 0, -2),  // bit 8
    (-1, -2),  // bit 7  -- this is the AT pixel (adaptive template)
    ( 2, -1),  // bit 6
    ( 1, -1),  // bit 5
    ( 0, -1),  // bit 4
    (-1, -1),  // bit 3
    (-2, -1),  // bit 2
    (-1,  0),  // bit 1
    (-2,  0),  // bit 0
  ];

  /// <summary>Extracts the 10-bit context for pixel (x, y) from the unpacked scanline buffers.</summary>
  /// <param name="cur">Current scanline (row y), unpacked to one pixel per byte (0 or 1).</param>
  /// <param name="prev1">Previous scanline (row y-1), unpacked.</param>
  /// <param name="prev2">Two scanlines above (row y-2), unpacked.</param>
  /// <param name="x">Current pixel column.</param>
  /// <param name="width">Image width.</param>
  /// <returns>A 10-bit context value.</returns>
  internal static int GetContext(byte[] cur, byte[] prev1, byte[] prev2, int x, int width) {
    var cx = 0;
    for (var i = 0; i < Template0Positions.Length; ++i) {
      var (dx, dy) = Template0Positions[i];
      var px = x + dx;
      int bit;
      if (px < 0 || px >= width)
        bit = 0;
      else if (dy == 0)
        bit = cur[px];
      else if (dy == -1)
        bit = prev1[px];
      else
        bit = prev2[px];

      cx = (cx << 1) | (bit & 1);
    }

    return cx;
  }
}
