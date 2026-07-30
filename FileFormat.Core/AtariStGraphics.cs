using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Atari ST and STE picture formats.</summary>
public static class AtariStGraphics {

  /// <summary>Bytes one palette entry occupies: a big-endian word.</summary>
  public const int PaletteEntrySize = 2;

  /// <summary>
  /// Whether a stored palette is an STE one, which carries four bits per channel rather than three.
  /// </summary>
  /// <remarks>
  /// The STE gained a bit per channel and put it at the bottom of the word rather than the top, so
  /// that a picture written on an ST still reads correctly on the newer machine. The consequence is
  /// that the two palettes cannot be told apart by size or position — only by whether any entry has
  /// bits an ST would never have set. A file that happens to use only the eight ST levels reads the
  /// same either way, so guessing wrong is harmless exactly when it is undetectable.
  /// </remarks>
  public static bool IsStePalette(ReadOnlySpan<byte> data, int offset, int colors) {
    for (var i = 0; i < colors; ++i) {
      var entry = offset + i * PaletteEntrySize;
      if (entry + 1 >= data.Length)
        break;

      if ((data[entry] & 8) != 0 || (data[entry + 1] & 0x88) != 0)
        return true;
    }

    return false;
  }

  /// <summary>Reads a stored palette as RGB triplets, in whichever of the two forms it is in.</summary>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset, int colors) {
    var ste = IsStePalette(data, offset, colors);
    var rgb = new byte[colors * 3];

    for (var i = 0; i < colors; ++i) {
      var entry = offset + i * PaletteEntrySize;
      if (entry + 1 >= data.Length)
        break;

      int high = data[entry], low = data[entry + 1];
      if (ste) {
        rgb[i * 3] = ChannelScaling.Expand4(_SteChannel(high & 15));
        rgb[i * 3 + 1] = ChannelScaling.Expand4(_SteChannel((low >> 4) & 15));
        rgb[i * 3 + 2] = ChannelScaling.Expand4(_SteChannel(low & 15));
      } else {
        rgb[i * 3] = ChannelScaling.Expand3(high & 7);
        rgb[i * 3 + 1] = ChannelScaling.Expand3((low >> 4) & 7);
        rgb[i * 3 + 2] = ChannelScaling.Expand3(low & 7);
      }
    }

    return rgb;
  }

  /// <summary>Rotates an STE nibble, whose least significant bit is stored highest.</summary>
  private static int _SteChannel(int value) => ((value & 7) << 1) | ((value >> 3) & 1);

  /// <summary>Bytes one bitplane row occupies for a given width and plane count.</summary>
  public static int BytesPerRow(int width, int planes) => ((width + 15) >> 4) * planes * 2;

  /// <summary>Maps an indexed frame through a palette into RGB triplets.</summary>
  public static byte[] ToRgb(ReadOnlySpan<byte> indices, ReadOnlySpan<byte> palette, int colors) {
    var rgb = new byte[indices.Length * 3];
    for (var i = 0; i < indices.Length; ++i) {
      var entry = (indices[i] % colors) * 3;
      rgb[i * 3] = palette[entry];
      rgb[i * 3 + 1] = palette[entry + 1];
      rgb[i * 3 + 2] = palette[entry + 2];
    }

    return rgb;
  }
}
