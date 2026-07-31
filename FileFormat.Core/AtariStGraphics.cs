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
  public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset, int colors)
    => ReadPalette(data, offset, colors, IsStePalette(data, offset, colors));

  /// <summary>Reads a stored palette as RGB triplets in the form the caller has already settled.</summary>
  /// <remarks>
  /// A picture that changes its palette every scanline holds one machine's worth of colours across
  /// all of them, so which form they are in has to be decided from the whole set at once. Deciding
  /// it per line would read a line whose colours happen to fit in three bits as an ST palette in the
  /// middle of an STE picture, and shift every channel.
  /// </remarks>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset, int colors, bool ste) {
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

  /// <summary>Reads one stored colour as 0xRRGGBB, in whichever of the two forms the caller names.</summary>
  public static int ColorAt(ReadOnlySpan<byte> data, int offset, bool ste) {
    if (offset + 1 >= data.Length)
      return 0;

    int high = data[offset], low = data[offset + 1];
    if (!ste)
      return (ChannelScaling.Expand3(high & 7) << 16)
             | (ChannelScaling.Expand3((low >> 4) & 7) << 8)
             | ChannelScaling.Expand3(low & 7);

    return (ChannelScaling.Expand4(_SteChannel(high & 15)) << 16)
           | (ChannelScaling.Expand4(_SteChannel((low >> 4) & 15)) << 8)
           | ChannelScaling.Expand4(_SteChannel(low & 15));
  }

  /// <summary>Rotates an STE nibble, whose least significant bit is stored highest.</summary>
  private static int _SteChannel(int value) => ((value & 7) << 1) | ((value >> 3) & 1);

  /// <summary>
  /// Expands an STE interlaced colour word — the form Spectrum 512's extended files store — into
  /// packed 0xRRGGBB.
  /// </summary>
  /// <remarks>
  /// The extended format shows two frames and averages them, which buys a bit of precision per
  /// channel over the STE's four. The extra bit is not appended: the word's bits are scattered so
  /// that the same sixteen bits still read as an ordinary STE colour on hardware that knows nothing
  /// about the trick. Unpacking is therefore a shuffle rather than a shift, and reading it as a
  /// plain four-bit-per-channel value halves every channel.
  /// </remarks>
  public static int InterlacedColorToRgb(int word) {
    var rgb = ((word & 0x0700) << 10) | ((word & 0x0870) << 6) | ((word & 0x4087) << 2)
      | ((word & 0x2000) >> 5) | ((word & 0x0008) >> 2) | ((word & 0x1000) >> 12);

    return (rgb << 3) | ((rgb >> 2) & 0x070707);
  }

  /// <summary>Packs five-bit channels back into an STE interlaced colour word.</summary>
  /// <remarks>
  /// The exact inverse of <see cref="InterlacedColorToRgb"/>. Each channel's top three bits sit
  /// where an ordinary STE colour keeps its whole channel, and the two extra bits are tucked into
  /// the word's spare positions — which is what lets the same sixteen bits mean one thing to a
  /// machine that knows the trick and something close to it on one that does not.
  /// </remarks>
  public static int RgbToInterlacedColor(int red, int green, int blue) {
    var word = 0;

    // Red: bits 4..2 to 10..8, bit 1 to 11, bit 0 to 14.
    word |= ((red >> 2) & 7) << 8;
    word |= ((red >> 1) & 1) << 11;
    word |= (red & 1) << 14;

    // Green: bits 4..2 to 6..4, bit 1 to 7, bit 0 to 13.
    word |= ((green >> 2) & 7) << 4;
    word |= ((green >> 1) & 1) << 7;
    word |= (green & 1) << 13;

    // Blue: bits 4..2 to 2..0, bit 1 to 3, bit 0 to 12.
    word |= (blue >> 2) & 7;
    word |= ((blue >> 1) & 1) << 3;
    word |= (blue & 1) << 12;

    return word;
  }

  /// <summary>Bytes one bitplane row occupies for a given width and plane count.</summary>
  public static int BytesPerRow(int width, int planes) => ((width + 15) >> 4) * planes * 2;

  /// <summary>Unpacks word-interleaved bitplanes into one palette index per pixel.</summary>
  /// <param name="stride">Bytes one row of the picture occupies across all its planes.</param>
  /// <remarks>
  /// The planes interleave every sixteen pixels rather than every scanline or not at all: a row is
  /// a run of words, the first word of each group belonging to plane 0, the second to plane 1 and so
  /// on. That is what the hardware's shift registers wanted, and it is why a bitplane picture cannot
  /// be read as either a chunky one or a set of separate planes.
  /// </remarks>
  public static byte[] UnpackBitplanes(
    ReadOnlySpan<byte> data, int offset, int stride, int planes, int width, int height) {
    var indices = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      var rowOffset = offset + y * stride;
      for (var x = 0; x < width; ++x) {
        // The word this pixel is in, then the byte within it.
        var at = rowOffset + ((x >> 3) & ~1) * planes + ((x >> 3) & 1);
        var index = 0;
        for (var plane = 0; plane < planes; ++plane) {
          var source = at + plane * 2;
          if (source < data.Length && ((data[source] >> (~x & 7)) & 1) != 0)
            index |= 1 << plane;
        }

        indices[y * width + x] = (byte)index;
      }
    }

    return indices;
  }

  /// <summary>
  /// Reads a GEM VDI palette: six bytes a colour, three big-endian words of intensity per thousand.
  /// </summary>
  /// <remarks>
  /// VDI numbers its colours by what they are for rather than by where they sit in hardware — white
  /// is colour 1 because it is the usual background, and black is the highest index. So the entries
  /// have to be permuted back into hardware order before a bitplane index can find them, and the
  /// permutation is not a rotation or a reversal but a table.
  /// </remarks>
  public static byte[] ReadVdiPalette(ReadOnlySpan<byte> data, int offset, int colors, int planes) {
    var palette = new byte[colors * 3];

    for (var i = 0; i < colors; ++i) {
      var entry = offset + i * 6;
      if (entry + 5 >= data.Length)
        break;

      var target = VdiToHardwareIndex(i, planes) * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var thousandths = (data[entry + channel * 2] << 8) | data[entry + channel * 2 + 1];
        palette[target + channel] = (byte)(thousandths < 1000 ? thousandths * 255 / 1000 : 255);
      }
    }

    return palette;
  }

  /// <summary>Where a VDI colour number sits in hardware order.</summary>
  public static int VdiToHardwareIndex(int index, int planes) => index switch {
    1 => (1 << planes) - 1,
    2 => 1,
    3 => 2,
    5 => 6,
    6 => 3,
    7 => 5,
    8 => 7,
    9 => 8,
    10 => 9,
    11 => 10,
    13 => 14,
    14 => 11,
    15 => 13,
    255 => 15,
    _ => index,
  };

  /// <summary>Reads a Falcon palette, which stores four bytes a colour with the third unused.</summary>
  public static byte[] ReadFalconPalette(ReadOnlySpan<byte> data, int offset, int colors) {
    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      var entry = offset + (i << 2);
      if (entry + 3 >= data.Length)
        break;

      palette[i * 3] = data[entry];
      palette[i * 3 + 1] = data[entry + 1];
      palette[i * 3 + 2] = data[entry + 3];
    }

    return palette;
  }

  /// <summary>Converts a Falcon true-colour pixel, which packs 5-6-5 bits into a big-endian word.</summary>
  public static void FalconTrueColorToRgb(ReadOnlySpan<byte> data, int offset, Span<byte> rgb) {
    var word = offset + 1 < data.Length ? (data[offset] << 8) | data[offset + 1] : 0;
    rgb[0] = ChannelScaling.Expand5(word >> 11);
    rgb[1] = ChannelScaling.Expand6((word >> 5) & 63);
    rgb[2] = ChannelScaling.Expand5(word & 31);
  }

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
