using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing;

/// <summary>
/// Point sampling done on the indices themselves, so a palette picture comes out a palette picture.
/// </summary>
/// <remarks>
/// The alternative — decode to colour, resample, re-quantise — would be a round trip through a
/// palette chooser for an operation that invents no colours at all. Nearest neighbour picks whole
/// pixels, and a whole pixel of an indexed picture is an index; copying it keeps the palette, the
/// transparency table and the index width exactly as they were.
/// <para/>
/// Indices are packed most significant first within a byte and run continuously across rows, which
/// is the layout <see cref="RawImage"/> states for the sub-byte widths.
/// </remarks>
internal static class IndexedSampler {

  /// <summary>Point-samples an indexed picture to a new size, keeping it indexed.</summary>
  public static RawImage Nearest(RawImage image, int width, int height) {
    var bits = RawPixelFormats.Get(image.Format).IndexBitDepth;
    if (bits is not (1 or 4 or 8 or 16))
      throw new ArgumentException($"An index width of {bits} bit(s) is not one this samples.", nameof(image));

    var source = image.PixelData;
    var target = new byte[checked(((long)width * height * bits + 7) / 8)];
    var factorX = (double)width / image.Width;
    var factorY = (double)height / image.Height;

    for (var y = 0; y < height; ++y) {
      var sourceY = _Pick((y + 0.5) / factorY, image.Height);

      for (var x = 0; x < width; ++x) {
        var sourceX = _Pick((x + 0.5) / factorX, image.Width);
        _Put(target, (long)y * width + x, bits, _Get(source, (long)sourceY * image.Width + sourceX, bits));
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = image.Format,
      PixelData = target,
      Palette = image.Palette,
      PaletteCount = image.PaletteCount,
      AlphaTable = image.AlphaTable,
      Metadata = image.Metadata,
      ColorInfo = image.ColorInfo,
    };
  }

  private static int _Pick(double position, int length) {
    var at = (int)position;
    return at < 0 ? 0 : at >= length ? length - 1 : at;
  }

  private static int _Get(byte[] data, long pixel, int bits) {
    switch (bits) {
      case 8: {
        var at = pixel;
        return at < data.Length ? data[at] : 0;
      }
      case 16: {
        var at = pixel * 2;
        // Both bytes are copied as a pair, so which of them is the high half never comes up.
        return at + 1 < data.Length ? (data[at] << 8) | data[at + 1] : 0;
      }
      default: {
        var perByte = 8 / bits;
        var at = pixel / perByte;
        if (at >= data.Length)
          return 0;

        var shift = 8 - bits * (int)(pixel % perByte + 1);
        return (data[at] >> shift) & ((1 << bits) - 1);
      }
    }
  }

  private static void _Put(byte[] data, long pixel, int bits, int index) {
    switch (bits) {
      case 8:
        data[pixel] = (byte)index;
        break;
      case 16:
        data[pixel * 2] = (byte)(index >> 8);
        data[pixel * 2 + 1] = (byte)index;
        break;
      default: {
        var perByte = 8 / bits;
        var at = pixel / perByte;
        var shift = 8 - bits * (int)(pixel % perByte + 1);
        var mask = ((1 << bits) - 1) << shift;
        data[at] = (byte)((data[at] & ~mask) | ((index << shift) & mask));
        break;
      }
    }
  }
}
