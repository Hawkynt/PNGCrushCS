using System;
using FileFormat.Core;

namespace FileFormat.AtariIce;

/// <summary>One field of an Interlace Character Editor picture, and how to read it.</summary>
public readonly record struct IceField {

  /// <summary>
  /// Where the screen's character codes are, or <see cref="FirstFontSheet"/> and
  /// <see cref="SecondFontSheet"/> for a file that is a character set rather than a picture.
  /// </summary>
  public int CharactersOffset { get; init; }

  /// <summary>Where the character set is.</summary>
  public int FontOffset { get; init; }

  /// <summary>How the field is to be read.</summary>
  public IceFrameMode Mode { get; init; }

  /// <summary>The nine GTIA colour registers as this field sees them.</summary>
  public byte[] Registers { get; init; }

  /// <summary>How far right the field's own timing pushes it.</summary>
  public int LeftSkip { get; init; }

  /// <summary>
  /// Which GTIA mode a version 2.0 field is read in — 9, 10 or 11 — or zero for an ordinary field.
  /// </summary>
  public int Ice20Mode { get; init; }

  /// <summary>Whether a version 2.0 field is the second of the pair, which orders its rows differently.</summary>
  public bool Ice20Second { get; init; }
}

/// <summary>Draws one field of an Interlace Character Editor picture.</summary>
public static class IceRenderer {

  /// <summary>Asks for the first of the two sheets a character set is shown as.</summary>
  public const int FirstFontSheet = -1;

  /// <summary>Asks for the second sheet, which differs in the order it takes the characters.</summary>
  public const int SecondFontSheet = -2;

  /// <summary>
  /// Which character each row of the first sheet starts at, when a file is a character set and has
  /// no screen of its own to say.
  /// </summary>
  /// <remarks>
  /// Not in order: the editor showed the set in the arrangement its own screen used, which puts the
  /// upper-case letters on the second row and the control characters on the first. The two sheets
  /// differ only in the second half, so that between them every character appears in both fields.
  /// </remarks>
  private static ReadOnlySpan<byte> _FirstSheetRows => [64, 0, 32, 96, 192, 128, 160, 224, 64, 0, 32, 96, 192, 128, 160, 224];

  private static ReadOnlySpan<byte> _SecondSheetRows => [64, 0, 32, 96, 192, 128, 160, 224, 192, 128, 160, 224, 64, 0, 32, 96];

  /// <summary>Character cells across a screen the editor stores.</summary>
  private const int _SCREEN_COLUMNS = 40;

  /// <summary>Rows in one of the three blocks a screen's character codes are split into.</summary>
  private const int _BLOCK_ROWS = 24;

  /// <summary>Draws one field into GTIA colour bytes.</summary>
  public static byte[] Render(ReadOnlySpan<byte> data, IceField field, int width, int height) {
    if (field.Ice20Mode != 0)
      return _RenderIce20(data, field, width, height);

    var frame = new byte[width * height];
    var registers = field.Registers;
    var entries = Atari8BitGraphics.ExpandGr10Registers(registers);
    var doubleLine = field.Mode is IceFrameMode.Gr13Gtia9 or IceFrameMode.Gr13Gtia10 or IceFrameMode.Gr13Gtia11 ? 1 : 0;
    var columns = width >> 3;
    var bitmap = new byte[columns];
    var frameOffset = 0;

    for (var y = 0; y < height; ++y) {
      for (var col = 0; col < columns; ++col) {
        var character = field.CharactersOffset switch {
          FirstFontSheet => _FirstSheetRows[y >> (3 + doubleLine)] + col,
          SecondFontSheet => _SecondSheetRows[y >> (3 + doubleLine)] + col,

          // A screen taller than one block repeats its character codes, the high bit of the code
          // being spent on colour; the block number supplies the bit the code cannot.
          _ => ((y / _BLOCK_ROWS) << 8)
               + _At(data, field.CharactersOffset + (y >> 3) * _SCREEN_COLUMNS + col),
        };

        var pattern = _At(data, field.FontOffset + ((character & ~128) << 3) + ((y >> doubleLine) & 7));

        switch (field.Mode) {
          case IceFrameMode.Gr0:
          case IceFrameMode.Gr0Gtia9:
          case IceFrameMode.Gr0Gtia10:
          case IceFrameMode.Gr0Gtia11:
            // On a sheet the high bit is not a colour but the editor's inverse-video flag.
            if (field.CharactersOffset < 0 && (character & 128) != 0)
              pattern ^= 255;

            bitmap[col] = (byte)pattern;
            break;

          case IceFrameMode.Gr12:
            _DrawGr12(frame, frameOffset, registers, col, pattern, character, width, field.LeftSkip);
            break;

          case IceFrameMode.Gr12Gtia10:
          case IceFrameMode.Gr13Gtia10:
            bitmap[col] = _GtiaByte(pattern, character, true);
            break;

          default:
            bitmap[col] = _GtiaByte(pattern, character, false);
            break;
        }
      }

      switch (field.Mode) {
        case IceFrameMode.Gr0:
          _DrawGr8(frame, frameOffset, bitmap, registers, width, field.LeftSkip);
          break;

        case IceFrameMode.Gr12:
          // The pixels the displacement pushed off the right have nothing behind them but border.
          for (var x = width; x < width + field.LeftSkip; ++x)
            _Plot(frame, frameOffset + x, registers[8]);

          break;

        case IceFrameMode.Gr0Gtia9:
        case IceFrameMode.Gr12Gtia9:
        case IceFrameMode.Gr13Gtia9:
          _DrawGtia9(frame, frameOffset, bitmap, registers, width, field.LeftSkip);
          break;

        case IceFrameMode.Gr0Gtia10:
        case IceFrameMode.Gr12Gtia10:
        case IceFrameMode.Gr13Gtia10:
          _DrawGtia10(frame, frameOffset, bitmap, entries, width, field.LeftSkip);
          break;

        default:
          _DrawGtia11(frame, frameOffset, bitmap, registers, width, field.LeftSkip);
          break;
      }

      frameOffset += width;
    }

    return frame;
  }

  /// <summary>
  /// Draws a version 2.0 field, which abandons the character screen for a fixed arrangement.
  /// </summary>
  /// <remarks>
  /// The later editor stopped storing a screen at all: the picture is the character set laid out in
  /// a fixed order, thirty-two half-characters across and 288 lines down, and the colour comes from
  /// multiplying the pattern by a number that changes every thirty-two rows. The two fields take
  /// that multiplier in different orders — one cycling within a block of three, the other stepping
  /// once per block — so a character shows three colours in one field and three in the other, and
  /// nine between them.
  /// </remarks>
  private static byte[] _RenderIce20(ReadOnlySpan<byte> data, IceField field, int width, int height) {
    var frame = new byte[width * height];
    var registers = field.Registers;
    var entries = Atari8BitGraphics.ExpandGr10Registers(registers);
    var columns = width >> 3;
    var bitmap = new byte[columns];

    for (var y = 0; y < height; ++y) {
      var block = y >> 5;
      var multiplier = (field.Ice20Second ? block / 3 : block % 3) + 1;

      for (var col = 0; col < columns; ++col) {
        var character = ((y & 24) << 1) + (col >> 1);
        var value = _At(data, field.FontOffset + (character << 3) + (y & 7));
        value = (col & 1) == 0 ? value >> 4 : value & 15;

        // Each bit is spread out to every fourth position and then scaled, so a pattern of four
        // bits becomes a nibble pair whose value carries both the shape and the colour.
        value = (((value & 8) << 3) | ((value & 4) << 2) | ((value & 2) << 1) | (value & 1)) * multiplier;

        if (field.Ice20Mode == 10) {
          // Two products land on register numbers the mode cannot show, and are nudged to ones it
          // can rather than left to draw the wrong colour.
          if ((value & 112) == 64)
            value = 128 + (value & 15);

          if ((value & 7) == 4)
            value = (value & 240) + 8;
        }

        bitmap[col] = (byte)value;
      }

      var frameOffset = y * width;
      switch (field.Ice20Mode) {
        case 9: _DrawGtia9(frame, frameOffset, bitmap, registers, width, field.LeftSkip); break;
        case 10: _DrawGtia10(frame, frameOffset, bitmap, entries, width, field.LeftSkip); break;
        default: _DrawGtia11(frame, frameOffset, bitmap, registers, width, field.LeftSkip); break;
      }
    }

    return frame;
  }

  /// <summary>
  /// Draws the eight pixels of a mode 12 character cell, four of them two bits wide.
  /// </summary>
  /// <remarks>
  /// Pattern 0 takes the background and 1 and 2 take PF0 and PF1; pattern 3 takes PF2 or PF3
  /// according to the character code's high bit, which is how the mode shows five colours from a
  /// two-bit pixel at the cost of half the character set.
  /// </remarks>
  private static void _DrawGr12(
    Span<byte> frame, int frameOffset, ReadOnlySpan<byte> registers,
    int col, int pattern, int character, int width, int leftSkip) {
    for (var x = col == 0 ? leftSkip : 0; x < 8; ++x) {
      var value = (pattern >> (~x & 6)) & 3;
      var register = value switch { 0 => 8, 1 => 4, 2 => 5, _ => (character & 128) == 0 ? 6 : 7 };
      _Plot(frame, frameOffset + (col << 3) + x - leftSkip, registers[register]);
    }
  }

  /// <summary>Draws a mode 0 line: one bit a pixel, the lit one taking PF1's luminance on PF2's hue.</summary>
  private static void _DrawGr8(
    Span<byte> frame, int frameOffset, ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> registers,
    int width, int leftSkip) {
    var background = registers[6];
    var foreground = (byte)((registers[6] & 240) | (registers[5] & 14));
    frameOffset -= leftSkip;

    var x = leftSkip;
    for (; x < width; ++x)
      _Plot(frame, frameOffset + x, ((bitmap[x >> 3] >> (~x & 7)) & 1) != 0 ? foreground : background);

    for (; x < width + leftSkip; ++x)
      _Plot(frame, frameOffset + x, registers[8]);
  }

  /// <summary>Draws a GTIA 9 line: a nibble is a luminance on the background's hue.</summary>
  private static void _DrawGtia9(
    Span<byte> frame, int frameOffset, ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> registers,
    int width, int leftSkip) {
    for (var x = 0; x < width; ++x) {
      var source = x + leftSkip;
      var luminance = source < 0 || source >= width ? 0 : (bitmap[source >> 3] >> (~source & 4)) & 15;
      _Plot(frame, frameOffset + x, (byte)(registers[8] | luminance));
    }
  }

  /// <summary>Draws a GTIA 10 line: a nibble indexes the sixteen entries the nine registers fill.</summary>
  private static void _DrawGtia10(
    Span<byte> frame, int frameOffset, ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> entries,
    int width, int leftSkip) {
    frameOffset += 2 - leftSkip;

    var x = leftSkip - 2;
    for (; x < 0; ++x)
      _Plot(frame, frameOffset + x, entries[0]);

    for (; x < width + leftSkip - 2; ++x)
      _Plot(frame, frameOffset + x, entries[(bitmap[x >> 3] >> (~x & 4)) & 15]);
  }

  /// <summary>Draws a GTIA 11 line: a nibble is a hue at the background's luminance.</summary>
  private static void _DrawGtia11(
    Span<byte> frame, int frameOffset, ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> registers,
    int width, int leftSkip) {
    frameOffset -= leftSkip;

    var x = leftSkip;
    for (; x < width; ++x) {
      var hue = (bitmap[x >> 3] << (x & 4)) & 240;

      // Hue zero is not a colour but the absence of one, so it shows black rather than the
      // background's luminance — the one place where the two GTIA colour modes are not symmetric.
      _Plot(frame, frameOffset + x, (byte)(hue == 0 ? registers[8] & 240 : registers[8] | hue));
    }

    for (; x < width + leftSkip; ++x)
      _Plot(frame, frameOffset + x, (byte)(registers[8] & 240));
  }

  /// <summary>Reinterprets a mode 12 byte as the two nibbles a GTIA mode reads from it.</summary>
  private static byte _GtiaByte(int value, int character, bool gtia10)
    => (byte)((_GtiaNibble(value >> 4, character, gtia10) << 4) | _GtiaNibble(value & 15, character, gtia10));

  /// <summary>
  /// What one nibble of a mode 12 byte means once GTIA is reading four bits where ANTIC wrote two.
  /// </summary>
  /// <remarks>
  /// The two chips disagree about the byte: ANTIC has already turned the character's bits into
  /// register numbers by the time GTIA sees them, so the nibble GTIA reads is a pair of register
  /// numbers rather than a colour index, and the mapping back is a table with no pattern to it.
  /// Several nibbles collapse onto the same value because two register numbers can produce one.
  /// </remarks>
  private static int _GtiaNibble(int nibble, int character, bool gtia10) {
    var high = (character & 128) != 0;

    return nibble switch {
      0 or 1 or 4 or 5 => 0,
      2 or 6 => 1,
      3 or 7 => high ? 3 : 2,
      8 => gtia10 ? 8 : 4,
      9 => 4,
      10 => 5,
      11 => high ? 7 : 6,
      12 => gtia10 || !high ? 8 : 12,
      13 => high ? 12 : 8,
      14 => high ? 13 : 9,
      _ => high ? 15 : 10,
    };
  }

  private static void _Plot(Span<byte> frame, int offset, byte value) {
    if (offset >= 0 && offset < frame.Length)
      frame[offset] = value;
  }

  private static int _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : 0;
}
