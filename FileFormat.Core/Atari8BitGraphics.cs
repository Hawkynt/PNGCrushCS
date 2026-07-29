using System;

namespace FileFormat.Core;

/// <summary>
/// Primitives shared by the Atari 8-bit picture formats: the GTIA colour palette and the ANTIC
/// mode D ("Graphics 7") bitmap layout.
/// </summary>
/// <remarks>
/// Dozens of Atari 8-bit formats are a Graphics 7 bitmap plus a handful of colour registers, so
/// the packing and the palette live here rather than being reimplemented per format.
/// </remarks>
public static class Atari8BitGraphics {

  /// <summary>Logical pixels across a Graphics 7 line. Each is displayed two screen pixels wide.</summary>
  public const int Gr7Width = 160;

  /// <summary>Bytes per Graphics 7 scanline: 160 pixels at 2 bits each.</summary>
  public const int Gr7BytesPerRow = Gr7Width / 4;

  /// <summary>Colour registers a Graphics 7 screen carries: PF0, PF1, PF2, PF3 and BAK.</summary>
  public const int ColorRegisterCount = 5;

  /// <summary>Index of the background register within a <see cref="ColorRegisterCount"/> block.</summary>
  public const int BackgroundRegisterIndex = 4;

  /// <summary>
  /// Maps a Graphics 7 pixel value to the colour register that draws it. Value 0 comes from the
  /// background register; 1, 2 and 3 come from PF0, PF1 and PF2. PF3 is unused in this mode.
  /// </summary>
  public static int RegisterForPixel(int pixel) => pixel == 0 ? BackgroundRegisterIndex : pixel - 1;

  /// <summary>Unpacks a Graphics 7 bitmap into one byte per logical pixel (values 0..3).</summary>
  /// <param name="data">Source bytes.</param>
  /// <param name="offset">Offset of the bitmap.</param>
  /// <param name="rows">Number of scanlines to unpack.</param>
  public static byte[] UnpackGr7(ReadOnlySpan<byte> data, int offset, int rows) {
    var pixels = new byte[Gr7Width * rows];
    for (var y = 0; y < rows; ++y) {
      var rowOffset = offset + y * Gr7BytesPerRow;
      for (var x = 0; x < Gr7Width; ++x) {
        var index = rowOffset + (x >> 2);
        if (index >= data.Length)
          break;

        // Four pixels per byte, most significant pair first.
        var shift = 6 - ((x & 3) << 1);
        pixels[y * Gr7Width + x] = (byte)((data[index] >> shift) & 3);
      }
    }

    return pixels;
  }

  /// <summary>Packs one byte per logical pixel (values 0..3) into the Graphics 7 bitmap layout.</summary>
  public static byte[] PackGr7(ReadOnlySpan<byte> pixels, int rows) {
    var data = new byte[Gr7BytesPerRow * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < Gr7Width; ++x) {
      var source = y * Gr7Width + x;
      if (source >= pixels.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      data[y * Gr7BytesPerRow + (x >> 2)] |= (byte)((pixels[source] & 3) << shift);
    }

    return data;
  }

  /// <summary>Logical pixels across an ANTIC mode 8 ("Graphics 3") line.</summary>
  public const int Gr3Width = 40;

  /// <summary>Logical rows in a Graphics 3 screen.</summary>
  public const int Gr3Height = 24;

  /// <summary>Bytes per Graphics 3 row: 40 pixels at 2 bits each.</summary>
  public const int Gr3BytesPerRow = Gr3Width / 4;

  /// <summary>Size of a Graphics 3 screen.</summary>
  public const int Gr3DataSize = Gr3BytesPerRow * Gr3Height;

  /// <summary>Unpacks an ANTIC mode 8 screen into one byte per logical pixel (values 0..3).</summary>
  /// <remarks>Mode 8 is the coarsest bitmap the hardware offers: 40x24 pixels, each drawn as an
  /// 8x8 block, which is why a whole screen fits in 240 bytes.</remarks>
  public static byte[] UnpackGr3(ReadOnlySpan<byte> data, int offset) {
    var pixels = new byte[Gr3Width * Gr3Height];
    for (var y = 0; y < Gr3Height; ++y)
    for (var x = 0; x < Gr3Width; ++x) {
      var index = offset + y * Gr3BytesPerRow + (x >> 2);
      if (index >= data.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      pixels[y * Gr3Width + x] = (byte)((data[index] >> shift) & 3);
    }

    return pixels;
  }

  /// <summary>Packs one byte per logical pixel (values 0..3) into the Graphics 3 layout.</summary>
  public static byte[] PackGr3(ReadOnlySpan<byte> pixels) {
    var data = new byte[Gr3DataSize];
    for (var y = 0; y < Gr3Height; ++y)
    for (var x = 0; x < Gr3Width; ++x) {
      var source = y * Gr3Width + x;
      if (source >= pixels.Length)
        break;

      var shift = 6 - ((x & 3) << 1);
      data[y * Gr3BytesPerRow + (x >> 2)] |= (byte)((pixels[source] & 3) << shift);
    }

    return data;
  }

  /// <summary>
  /// Unpacks an ANTIC mode F ("Graphics 9") row set into one luminance value (0..15) per logical
  /// pixel. Mode 9 stores two nibbles per byte, and each nibble covers four screen pixels, so a
  /// row of <paramref name="width"/> screen pixels occupies <c>width / 8</c> bytes.
  /// </summary>
  public static byte[] UnpackGr9(ReadOnlySpan<byte> data, int offset, int width, int rows) {
    var bytesPerRow = width >> 3;
    var pixels = new byte[width * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < width; ++x) {
      var index = offset + y * bytesPerRow + (x >> 3);
      if (index >= data.Length)
        break;

      // Nibbles run high first; each covers four consecutive pixels.
      var shift = (~x & 4);
      pixels[y * width + x] = (byte)((data[index] >> shift) & 15);
    }

    return pixels;
  }

  /// <summary>Packs luminance values (0..15) back into the Graphics 9 layout.</summary>
  public static byte[] PackGr9(ReadOnlySpan<byte> pixels, int width, int rows) {
    var bytesPerRow = width >> 3;
    var data = new byte[bytesPerRow * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < width; x += 4) {
      var source = y * width + x;
      if (source >= pixels.Length)
        break;

      var shift = (~x & 4);
      data[y * bytesPerRow + (x >> 3)] |= (byte)((pixels[source] & 15) << shift);
    }

    return data;
  }

  /// <summary>
  /// The 256-entry GTIA palette as RGB triplets, generated from the standard hue/luminance model
  /// rather than a captured table: the high nibble of a colour byte selects one of 15 hues on the
  /// NTSC colour burst (0 being grey), and the low nibble selects luminance.
  /// </summary>
  public static byte[] CreatePalette() {
    var palette = new byte[256 * 3];
    for (var hue = 0; hue < 16; ++hue)
    for (var luma = 0; luma < 16; ++luma) {
      var y = luma / 15.0;
      double i = 0, q = 0;
      if (hue != 0) {
        // Hues are evenly spaced around the colour wheel; the offset lines hue 1 up with the
        // orange-gold the hardware actually produces.
        var angle = (hue - 1) * (2 * Math.PI / 15) + 0.4014;
        const double saturation = 0.35;
        i = saturation * Math.Cos(angle);
        q = saturation * Math.Sin(angle);
      }

      var offset = ((hue << 4) | luma) * 3;
      palette[offset] = _Clamp(y + 0.956 * i + 0.621 * q);
      palette[offset + 1] = _Clamp(y - 0.272 * i - 0.647 * q);
      palette[offset + 2] = _Clamp(y - 1.106 * i + 1.703 * q);
    }

    return palette;
  }

  /// <summary>Finds the colour byte whose palette entry is closest to the given RGB value.</summary>
  /// <remarks>The hardware ignores the low bit of a colour byte, so only even values are considered.</remarks>
  public static byte FindNearestColorByte(ReadOnlySpan<byte> palette, byte red, byte green, byte blue) {
    var best = (byte)0;
    var bestDistance = int.MaxValue;
    for (var candidate = 0; candidate < 256; candidate += 2) {
      var offset = candidate * 3;
      int dr = palette[offset] - red, dg = palette[offset + 1] - green, db = palette[offset + 2] - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = (byte)candidate;
      if (distance == 0)
        break;
    }

    return best;
  }

  private static byte _Clamp(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
}
