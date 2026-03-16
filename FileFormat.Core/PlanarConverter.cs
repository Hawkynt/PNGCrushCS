using System;

namespace FileFormat.Core;

/// <summary>Converts between planar and chunky pixel formats for various retro platforms.</summary>
public static class PlanarConverter {

  /// <summary>
  ///   Converts ILBM-style interleaved planar data to chunky (one byte per pixel).
  ///   Each scanline has <paramref name="numPlanes"/> bitplane rows, each word-aligned.
  /// </summary>
  public static byte[] IlbmPlanarToChunky(ReadOnlySpan<byte> planarData, int width, int height, int numPlanes) {
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerScanline = bytesPerPlaneRow * numPlanes;
    var result = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var scanlineOffset = y * bytesPerScanline;
      for (var x = 0; x < width; ++x) {
        var byteIndex = x / 8;
        var bitIndex = 7 - (x % 8);
        var pixel = 0;

        for (var plane = 0; plane < numPlanes; ++plane) {
          var planeOffset = scanlineOffset + plane * bytesPerPlaneRow + byteIndex;
          if (planeOffset < planarData.Length && (planarData[planeOffset] & (1 << bitIndex)) != 0)
            pixel |= 1 << plane;
        }

        result[y * width + x] = (byte)pixel;
      }
    }

    return result;
  }

  /// <summary>
  ///   Converts chunky pixel data to ILBM-style interleaved planar format.
  ///   Each scanline produces <paramref name="numPlanes"/> bitplane rows, each word-aligned.
  /// </summary>
  public static byte[] ChunkyToIlbmPlanar(ReadOnlySpan<byte> chunkyData, int width, int height, int numPlanes) {
    var bytesPerPlaneRow = ((width + 15) / 16) * 2;
    var bytesPerScanline = bytesPerPlaneRow * numPlanes;
    var result = new byte[bytesPerScanline * height];

    for (var y = 0; y < height; ++y) {
      var scanlineOffset = y * bytesPerScanline;
      for (var x = 0; x < width; ++x) {
        var pixel = chunkyData[y * width + x];
        var byteIndex = x / 8;
        var bitIndex = 7 - (x % 8);

        for (var plane = 0; plane < numPlanes; ++plane)
          if ((pixel & (1 << plane)) != 0)
            result[scanlineOffset + plane * bytesPerPlaneRow + byteIndex] |= (byte)(1 << bitIndex);
      }
    }

    return result;
  }

  /// <summary>
  ///   Converts Atari ST word-interleaved planar data to chunky (one byte per pixel).
  ///   In Atari ST format, bitplanes are interleaved at the word (16-bit) level:
  ///   [word0-plane0][word0-plane1]...[word0-planeN][word1-plane0]...
  /// </summary>
  public static byte[] AtariStToChunky(ReadOnlySpan<byte> planarData, int width, int height, int numPlanes) {
    var wordsPerRow = (width + 15) / 16;
    var result = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var rowOffset = y * wordsPerRow * numPlanes * 2;

      for (var wordIndex = 0; wordIndex < wordsPerRow; ++wordIndex) {
        var wordGroupOffset = rowOffset + wordIndex * numPlanes * 2;

        for (var bit = 0; bit < 16; ++bit) {
          var x = wordIndex * 16 + bit;
          if (x >= width)
            break;

          var bitMask = 1 << (15 - bit);
          var pixel = 0;

          for (var plane = 0; plane < numPlanes; ++plane) {
            var planeWordOffset = wordGroupOffset + plane * 2;
            if (planeWordOffset + 1 < planarData.Length) {
              var word = (planarData[planeWordOffset] << 8) | planarData[planeWordOffset + 1];
              if ((word & bitMask) != 0)
                pixel |= 1 << plane;
            }
          }

          result[y * width + x] = (byte)pixel;
        }
      }
    }

    return result;
  }

  /// <summary>
  ///   Converts chunky pixel data to Atari ST word-interleaved planar format.
  /// </summary>
  public static byte[] ChunkyToAtariSt(ReadOnlySpan<byte> chunkyData, int width, int height, int numPlanes) {
    var wordsPerRow = (width + 15) / 16;
    var result = new byte[wordsPerRow * numPlanes * 2 * height];

    for (var y = 0; y < height; ++y) {
      var rowOffset = y * wordsPerRow * numPlanes * 2;

      for (var wordIndex = 0; wordIndex < wordsPerRow; ++wordIndex) {
        var wordGroupOffset = rowOffset + wordIndex * numPlanes * 2;
        var words = new ushort[numPlanes];

        for (var bit = 0; bit < 16; ++bit) {
          var x = wordIndex * 16 + bit;
          if (x >= width)
            break;

          var pixel = chunkyData[y * width + x];
          var bitMask = (ushort)(1 << (15 - bit));

          for (var plane = 0; plane < numPlanes; ++plane)
            if ((pixel & (1 << plane)) != 0)
              words[plane] |= bitMask;
        }

        for (var plane = 0; plane < numPlanes; ++plane) {
          var planeWordOffset = wordGroupOffset + plane * 2;
          result[planeWordOffset] = (byte)(words[plane] >> 8);
          result[planeWordOffset + 1] = (byte)(words[plane] & 0xFF);
        }
      }
    }

    return result;
  }

  /// <summary>
  ///   Converts non-interleaved planar data (all rows of plane 0, then all rows of plane 1, etc.)
  ///   to chunky (one byte per pixel). Used by GEM IMG.
  /// </summary>
  public static byte[] NonInterleavedPlanarToChunky(ReadOnlySpan<byte> planarData, int width, int height, int numPlanes) {
    var bytesPerRow = (width + 7) / 8;
    var result = new byte[width * height];

    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerRow * height;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerRow;

        for (var x = 0; x < width; ++x) {
          var byteIndex = x / 8;
          var bitIndex = 7 - (x % 8);
          var dataOffset = rowOffset + byteIndex;

          if (dataOffset < planarData.Length && (planarData[dataOffset] & (1 << bitIndex)) != 0)
            result[y * width + x] |= (byte)(1 << plane);
        }
      }
    }

    return result;
  }

  /// <summary>
  ///   Converts chunky pixel data to non-interleaved planar format.
  /// </summary>
  public static byte[] ChunkyToNonInterleavedPlanar(ReadOnlySpan<byte> chunkyData, int width, int height, int numPlanes) {
    var bytesPerRow = (width + 7) / 8;
    var result = new byte[numPlanes * bytesPerRow * height];

    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerRow * height;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerRow;

        for (var x = 0; x < width; ++x) {
          var pixel = chunkyData[y * width + x];
          if ((pixel & (1 << plane)) != 0) {
            var byteIndex = x / 8;
            var bitIndex = 7 - (x % 8);
            result[rowOffset + byteIndex] |= (byte)(1 << bitIndex);
          }
        }
      }
    }

    return result;
  }

  /// <summary>Converts an Atari ST 9-bit palette (0x0RGB, R/G/B in 0-7) to RGB triplets.</summary>
  public static byte[] StPaletteToRgb(ReadOnlySpan<short> stPalette) {
    var rgb = new byte[stPalette.Length * 3];
    for (var i = 0; i < stPalette.Length; ++i) {
      var entry = stPalette[i] & 0x0FFF;
      var r = (entry >> 8) & 0x07;
      var g = (entry >> 4) & 0x07;
      var b = entry & 0x07;
      rgb[i * 3] = (byte)(r * 255 / 7);
      rgb[i * 3 + 1] = (byte)(g * 255 / 7);
      rgb[i * 3 + 2] = (byte)(b * 255 / 7);
    }

    return rgb;
  }

  /// <summary>Converts RGB triplets back to Atari ST 9-bit palette values.</summary>
  public static short[] RgbToStPalette(ReadOnlySpan<byte> rgb, int count) {
    var palette = new short[count];
    for (var i = 0; i < count; ++i) {
      var r = rgb[i * 3] * 7 / 255;
      var g = rgb[i * 3 + 1] * 7 / 255;
      var b = rgb[i * 3 + 2] * 7 / 255;
      palette[i] = (short)((r << 8) | (g << 4) | b);
    }

    return palette;
  }
}
