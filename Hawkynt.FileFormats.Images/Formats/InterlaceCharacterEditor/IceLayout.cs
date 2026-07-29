using System;

namespace FileFormat.InterlaceCharacterEditor;

/// <summary>
/// Where each Interlace Character Editor mode puts things, and which colour registers its two
/// frames draw from.
/// </summary>
/// <remarks>
/// All five modes share one skeleton: a short header of colour bytes, a 16384-byte font area, and
/// one or two 960-byte character maps. What differs is how long the header is, whether the frames
/// share a character map, and what the second frame does with a font byte.
/// </remarks>
public static class IceLayout {

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 192;

  /// <summary>Character cells across.</summary>
  public const int Columns = DisplayWidth / 8;

  /// <summary>Character cell rows.</summary>
  public const int Rows = DisplayHeight / 8;

  /// <summary>Bytes in a character map.</summary>
  public const int CharacterMapSize = Columns * Rows;

  /// <summary>Bytes in the font area.</summary>
  public const int FontSize = 16384;

  /// <summary>Bytes in one glyph, one per scanline of a character cell.</summary>
  public const int GlyphSize = 8;

  /// <summary>Distance between the two frames' font bases.</summary>
  public const int FontBaseStride = 1024;

  /// <summary>
  /// Character rows sharing one font bank. The high byte of a character code comes from the
  /// scanline, changing every 24 scanlines, so three character rows fall in each bank.
  /// </summary>
  public const int RowsPerBank = 3;

  /// <summary>Cells in one bank, and therefore the number of glyphs a bank can hold per frame.</summary>
  public const int CellsPerBank = RowsPerBank * Columns;

  /// <summary>Registers a mode 4 frame chooses between.</summary>
  public const int Graphics12ColorCount = 4;

  /// <summary>Length of the header preceding the font area.</summary>
  public static int HeaderSizeFor(IceMode mode) => mode is IceMode.SuperIrg2 or IceMode.Pcin ? 10 : 6;

  /// <summary>Offset of the first frame's character map.</summary>
  public static int Characters1OffsetFor(IceMode mode) => HeaderSizeFor(mode) + FontSize;

  /// <summary>Whether the two frames read the same character map.</summary>
  public static bool SharesCharacterMap(IceMode mode) => mode is IceMode.Cin or IceMode.Min or IceMode.Pcin;

  /// <summary>Offset of the second frame's character map, which may be the first one.</summary>
  public static int Characters2OffsetFor(IceMode mode)
    => Characters1OffsetFor(mode) + (SharesCharacterMap(mode) ? 0 : CharacterMapSize);

  /// <summary>Total file size.</summary>
  public static int FileSizeFor(IceMode mode)
    => Characters1OffsetFor(mode) + CharacterMapSize * (SharesCharacterMap(mode) ? 1 : 2);

  /// <summary>What the second frame does with a font byte.</summary>
  public static IceFrameKind SecondFrameKind(IceMode mode) => mode switch {
    IceMode.SuperIrg or IceMode.SuperIrg2 => IceFrameKind.Graphics12,
    IceMode.Cin => IceFrameKind.Gtia11,
    IceMode.Min => IceFrameKind.Gtia9,
    IceMode.Pcin => IceFrameKind.Gtia10,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Interlace Character Editor mode."),
  };

  /// <summary>Screen pixels one value of a frame covers.</summary>
  public static int PixelsPerValue(IceFrameKind kind) => kind == IceFrameKind.Graphics12 ? 2 : 4;

  /// <summary>Values a frame can express.</summary>
  public static int ValueCount(IceFrameKind kind) => kind switch {
    IceFrameKind.Graphics12 => Graphics12ColorCount,
    // GTIA 10 indexes the nine colour registers; the remaining nibbles are not addressable.
    IceFrameKind.Gtia10 => 9,
    _ => 16,
  };

  /// <summary>
  /// The Atari colour byte the first frame draws for each of its four pixel values, taken from
  /// the header. Value 0 always comes from the background register.
  /// </summary>
  public static byte[] FirstFrameColors(IceMode mode, ReadOnlySpan<byte> header) => mode switch {
    // Registers stored background, PF0, PF1, PF2, PF3.
    IceMode.SuperIrg or IceMode.Min => [_At(header, 1), _At(header, 2), _At(header, 3), _At(header, 4)],
    // Each playfield register is stored twice, the first frame taking the even byte of each pair.
    IceMode.SuperIrg2 => [_At(header, 1), _At(header, 2), _At(header, 4), _At(header, 6)],
    // The background is forced to zero before this frame is drawn, whatever the header holds.
    IceMode.Cin => [0, _At(header, 2), _At(header, 3), _At(header, 4)],
    // The playfield registers sit at the end of a full GTIA register block, the background before it.
    IceMode.Pcin => [_At(header, 1), _At(header, 5), _At(header, 6), _At(header, 7)],
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Interlace Character Editor mode."),
  };

  /// <summary>The Atari colour byte the second frame draws for each of its values.</summary>
  public static byte[] SecondFrameColors(IceMode mode, ReadOnlySpan<byte> header) {
    switch (mode) {
      case IceMode.SuperIrg:
        return FirstFrameColors(mode, header);
      case IceMode.SuperIrg2:
        // The odd byte of each register pair.
        return [_At(header, 1), _At(header, 3), _At(header, 5), _At(header, 7)];
      case IceMode.Min: {
        // Sixteen luminances of the background hue.
        var background = _At(header, 1);
        var colors = new byte[16];
        for (var value = 0; value < colors.Length; ++value)
          colors[value] = (byte)(background | value);

        return colors;
      }

      case IceMode.Cin: {
        // Sixteen hues at the background luminance; hue zero keeps only the hue bits.
        var background = _At(header, 1);
        var colors = new byte[16];
        for (var value = 0; value < colors.Length; ++value)
          colors[value] = (byte)(value == 0 ? background & 240 : background | (value << 4));

        return colors;
      }

      case IceMode.Pcin: {
        // The nine GTIA registers in order, indexed straight by the pixel value.
        var colors = new byte[9];
        colors[0] = _At(header, 1);
        for (var value = 1; value < colors.Length; ++value)
          colors[value] = _At(header, 1 + value);

        return colors;
      }

      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Interlace Character Editor mode.");
    }
  }

  /// <summary>The hardware ignores the low bit of a colour byte, and every reader masks it off.</summary>
  private static byte _At(ReadOnlySpan<byte> header, int index)
    => (byte)(index < header.Length ? header[index] & 254 : 0);
}
