using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Apple IIGS super hi-res formats.</summary>
public static class AppleIIGSGraphics {

  /// <summary>Colours one palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes one palette occupies.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>Reads a sixteen-colour palette, four bits a channel packed into two bytes.</summary>
  /// <param name="reversed">
  /// Whether the entries are stored last first. The hardware's registers are addressed downwards,
  /// so a program that saved them by dumping the registers gets the palette back to front, and a
  /// program that wrote a palette out deliberately does not.
  /// </param>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data, int offset, bool reversed) {
    var palette = new byte[ColorCount * 3];
    var flip = reversed ? ColorCount - 1 : 0;

    for (var i = 0; i < ColorCount; ++i) {
      var entry = offset + ((i ^ flip) << 1);
      if (entry + 1 >= data.Length)
        break;

      // Green and blue share the low byte; red is alone in the high one.
      palette[i * 3] = ChannelScaling.Expand4(data[entry + 1] & 15);
      palette[i * 3 + 1] = ChannelScaling.Expand4(data[entry] >> 4);
      palette[i * 3 + 2] = ChannelScaling.Expand4(data[entry] & 15);
    }

    return palette;
  }
}
