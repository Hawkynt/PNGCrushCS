using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PowerGraphics;

/// <summary>Writes the display program that draws a given picture.</summary>
/// <remarks>
/// A PowerGraphics file is not a picture but the program that draws one, so encoding is writing a
/// program rather than packing pixels: a display list telling ANTIC what mode each scanline is and
/// where to fetch it from, and a raster program saying which chip register to write and after how
/// many processor cycles.
/// <para/>
/// Cycles are what decides where along a scanline a write lands, and they are not free. The
/// processor gets the ones ANTIC does not steal to fetch the playfield, and ANTIC steals every other
/// one from the moment the picture starts — so a write made once the line is under way costs some
/// fifty screen pixels of lead time, while the ones made before it costs nothing but the line's
/// blanked start. Four writes fit into that start, which is exactly the four registers ANTIC mode E
/// draws from: the background and PF0, PF1 and PF2.
/// <para/>
/// So each scanline gets four colours of its own out of the hundred and twenty-eight the hardware
/// holds, and a hundred and sixty pixels two screen pixels wide to spend them on. They are settled
/// one scanline at a time — the registers are rewritten every line, so no scanline constrains
/// another — by giving each register in turn every value the hardware holds while the other three
/// stand.
/// <para/>
/// The eight screen pixels at each end of a line are outside what ANTIC fetches and show the
/// background whatever the picture asks, so they are counted against the background register rather
/// than left out: it is the one register that has to answer for them.
/// </remarks>
public static class PowerGraphicsEncoder {

  /// <summary>Characters ANTIC fetches per scanline, which the DMA control byte names.</summary>
  private const int _COLUMNS = 40;

  /// <summary>The DMA control byte that asks for that width and for no sprite fetching.</summary>
  private const int _DMA_CONTROL = 50;

  /// <summary>Mode E pixels a scanline holds: four to a fetched byte, two screen pixels each.</summary>
  private const int _PIXELS = _COLUMNS * 4;

  /// <summary>Screen pixels at each end of a line that ANTIC never fetches.</summary>
  private const int _BORDER = (PowerGraphicsFile.Width - _PIXELS * 2) / 2;

  /// <summary>The display list instruction that names a mode E line and where to fetch it from.</summary>
  private const byte _LOAD_AND_MODE = 78;

  /// <summary>The one that names a mode E line continuing from the last.</summary>
  private const byte _MODE = 14;

  /// <summary>The instruction that sends ANTIC back to the start of the list and waits for the frame.</summary>
  private const byte _JUMP_AND_WAIT = 65;

  /// <summary>How far ANTIC can scan from one load before its counter wraps.</summary>
  /// <remarks>
  /// Twelve bits, so a screen larger than this cannot be one run of memory however the display list
  /// is written: the counter comes back to the start of the block rather than carrying. A screen of
  /// two hundred and forty mode E lines is more than twice that, so it is laid out a block at a time
  /// with a load instruction at each, and the sixteen bytes at the end of a block that will not hold
  /// another line are left unused rather than half a line being allowed to straddle the wrap.
  /// </remarks>
  private const int _SCAN_BLOCK = 4096;

  /// <summary>Scanlines one such block holds.</summary>
  private const int _ROWS_PER_BLOCK = _SCAN_BLOCK / _COLUMNS;

  /// <summary>Where the file says its own DMA control byte is.</summary>
  private const int _DMA_OFFSET = 774;

  /// <summary>Where the second block of register values is; its last byte is the priority.</summary>
  private const int _PRIORITY_OFFSET = 773;

  /// <summary>Where the raster program goes, which the reader insists is clear of the header.</summary>
  private const int _RASTER_OFFSET = 1536;

  /// <summary>Bytes one scanline's raster program takes: four writes of a register and a value.</summary>
  private const int _RASTER_STRIDE = 8;

  /// <summary>
  /// Where the playfield goes: past the raster program, and at a scan block boundary so that no
  /// scanline straddles one.
  /// </summary>
  private const int _SCREEN_OFFSET = 3584;

  /// <summary>Blocks the screen takes.</summary>
  private const int _BLOCKS = (PowerGraphicsFile.Height + _ROWS_PER_BLOCK - 1) / _ROWS_PER_BLOCK;

  /// <summary>How long the file is with everything in it.</summary>
  public const int FileSize = _SCREEN_OFFSET + _BLOCKS * _SCAN_BLOCK;

  /// <summary>The chip register each of a mode E line's four colours is written to.</summary>
  /// <remarks>
  /// The background first because it also paints the border, then the three playfield registers in
  /// the order a two-bit pixel names them.
  /// </remarks>
  private static ReadOnlySpan<byte> _Registers => [26, 22, 23, 24];

  /// <summary>Colours a mode E pixel chooses between.</summary>
  private const int _CHOICES = 4;

  /// <summary>Passes the register search makes over a scanline's four before giving up.</summary>
  private const int _SWEEPS = 3;

  /// <summary>Turns a 336x240 picture into the bytes of a PowerGraphics file.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    var data = new byte[FileSize];

    // The executable header the machine loaded it with, and the name the format carries.
    var start = PowerGraphicsFile.LoadAddress + 6;
    var last = start + (FileSize - 6) - 1;
    data[0] = data[1] = 255;
    data[2] = (byte)start;
    data[3] = (byte)(start >> 8);
    data[4] = (byte)last;
    data[5] = (byte)(last >> 8);

    var raster = PowerGraphicsFile.LoadAddress + _RASTER_OFFSET;
    data[6] = (byte)raster;
    data[7] = (byte)(raster >> 8);
    Encoding.ASCII.GetBytes(PowerGraphicsFile.Signature).CopyTo(data, 8);

    // A line naming where each block of the playfield starts, and the rest continuing from it.
    var rows = new int[PowerGraphicsFile.Height];
    var list = PowerGraphicsFile.DisplayListOffset;
    for (var y = 0; y < PowerGraphicsFile.Height; ++y) {
      var block = y / _ROWS_PER_BLOCK;
      rows[y] = _SCREEN_OFFSET + block * _SCAN_BLOCK + y % _ROWS_PER_BLOCK * _COLUMNS;

      if (y % _ROWS_PER_BLOCK == 0) {
        var screen = PowerGraphicsFile.LoadAddress + rows[y];
        data[list++] = _LOAD_AND_MODE;
        data[list++] = (byte)screen;
        data[list++] = (byte)(screen >> 8);
        continue;
      }

      data[list++] = _MODE;
    }

    // Back to the top and wait for the frame, which is what makes the list a program and not a run
    // of instructions. Nothing reads it — the picture is two hundred and forty lines and stops — but
    // a display list without it is not one.
    var top = PowerGraphicsFile.LoadAddress + PowerGraphicsFile.DisplayListOffset;
    data[list++] = _JUMP_AND_WAIT;
    data[list++] = (byte)top;
    data[list] = (byte)(top >> 8);

    data[_PRIORITY_OFFSET] = 1;
    data[_DMA_OFFSET] = _DMA_CONTROL;

    var registers = new byte[_CHOICES];
    var statistics = new int[_PIXELS * 4];
    var border = new int[4];

    for (var y = 0; y < PowerGraphicsFile.Height; ++y) {
      _RowStatistics(rgb, y, statistics, border);
      _ChooseRegisters(statistics, border, registers);

      var program = _RASTER_OFFSET + y * _RASTER_STRIDE;
      for (var choice = 0; choice < _CHOICES; ++choice) {
        // The set bit says a value follows; the high bit on the last says the line is done.
        var operation = 32 | _Registers[choice];
        data[program + choice * 2] = (byte)(choice == _CHOICES - 1 ? operation | 128 : operation);
        data[program + choice * 2 + 1] = registers[choice];
      }

      var row = rows[y];
      for (var pixel = 0; pixel < _PIXELS; ++pixel) {
        var choice = _Nearest(statistics, pixel * 4, registers);

        // Four pixels to a byte, the leftmost in the top pair.
        data[row + (pixel >> 2)] |= (byte)(choice << ((~pixel & 3) << 1));
      }
    }

    return data;
  }

  /// <summary>
  /// What each mode E pixel of a scanline sums to, and what the border it cannot reach sums to.
  /// </summary>
  /// <remarks>
  /// A mode E pixel is two screen pixels wide, so no choice can tell them apart. The error of a
  /// colour against a run of pixels expands into the colour's own square, its product with their
  /// sums, and their sum of squares, so the pixels are never touched again once these are taken.
  /// </remarks>
  private static void _RowStatistics(ReadOnlySpan<byte> rgb, int y, int[] statistics, int[] border) {
    var row = y * PowerGraphicsFile.Width * 3;
    Array.Clear(border);

    for (var pixel = 0; pixel < _PIXELS; ++pixel) {
      int red = 0, green = 0, blue = 0, squares = 0;

      for (var half = 0; half < 2; ++half) {
        var at = row + (_BORDER + pixel * 2 + half) * 3;
        int r = rgb[at], g = rgb[at + 1], b = rgb[at + 2];
        red += r;
        green += g;
        blue += b;
        squares += r * r + g * g + b * b;
      }

      statistics[pixel * 4] = red;
      statistics[pixel * 4 + 1] = green;
      statistics[pixel * 4 + 2] = blue;
      statistics[pixel * 4 + 3] = squares;
    }

    for (var side = 0; side < 2; ++side)
    for (var offset = 0; offset < _BORDER; ++offset) {
      var at = row + (side == 0 ? offset : PowerGraphicsFile.Width - _BORDER + offset) * 3;
      int r = rgb[at], g = rgb[at + 1], b = rgb[at + 2];
      border[0] += r;
      border[1] += g;
      border[2] += b;
      border[3] += r * r + g * g + b * b;
    }
  }

  /// <summary>The four colours a scanline is drawn from, each in turn given every value there is.</summary>
  /// <remarks>
  /// A scanline's four registers constrain no other scanline, the raster program rewriting all four
  /// every line — so this is a search over four bytes and not over the picture, and it can afford to
  /// be exhaustive one byte at a time.
  /// </remarks>
  private static void _ChooseRegisters(int[] statistics, int[] border, byte[] registers) {
    var median = ColorQuantizer.Quantize(_AsBgra(statistics), _PIXELS, _CHOICES);
    for (var choice = 0; choice < _CHOICES; ++choice) {
      var entry = Math.Min(choice, median.Count - 1) * 3;
      registers[choice] = Atari8BitGraphics.NearestRegister(
        median.Palette[entry], median.Palette[entry + 1], median.Palette[entry + 2]);
    }

    var cost = _RowCost(statistics, border, registers);
    for (var sweep = 0; sweep < _SWEEPS; ++sweep) {
      var moved = false;

      for (var choice = 0; choice < _CHOICES; ++choice) {
        var original = registers[choice];
        var best = original;

        // The low bit of a colour register does not reach the screen, so only the even values differ.
        for (var candidate = 0; candidate < 256; candidate += 2) {
          registers[choice] = (byte)candidate;
          var trial = _RowCost(statistics, border, registers);
          if (trial >= cost)
            continue;

          cost = trial;
          best = (byte)candidate;
        }

        registers[choice] = best;
        moved |= best != original;
      }

      if (!moved)
        break;
    }
  }

  /// <summary>The scanline's pixels as a buffer the quantizer reads, for a first guess.</summary>
  private static byte[] _AsBgra(int[] statistics) {
    var bgra = new byte[_PIXELS * 4];
    for (var pixel = 0; pixel < _PIXELS; ++pixel) {
      bgra[pixel * 4] = (byte)(statistics[pixel * 4 + 2] >> 1);
      bgra[pixel * 4 + 1] = (byte)(statistics[pixel * 4 + 1] >> 1);
      bgra[pixel * 4 + 2] = (byte)(statistics[pixel * 4] >> 1);
      bgra[pixel * 4 + 3] = 255;
    }

    return bgra;
  }

  /// <summary>What a set of four registers costs a scanline, the border falling to the background.</summary>
  private static long _RowCost(int[] statistics, int[] border, byte[] registers) {
    var total = _Cost(border, 0, registers[0], _BORDER * 2);

    for (var pixel = 0; pixel < _PIXELS; ++pixel) {
      var best = long.MaxValue;
      for (var choice = 0; choice < _CHOICES; ++choice) {
        var cost = _Cost(statistics, pixel * 4, registers[choice], 2);
        if (cost < best)
          best = cost;
      }

      total += best;
    }

    return total;
  }

  /// <summary>Which of the four registers a pixel is drawn with.</summary>
  private static int _Nearest(int[] statistics, int at, byte[] registers) {
    var best = long.MaxValue;
    var pick = 0;

    for (var choice = 0; choice < _CHOICES; ++choice) {
      var cost = _Cost(statistics, at, registers[choice], 2);
      if (cost >= best)
        continue;

      best = cost;
      pick = choice;
    }

    return pick;
  }

  private static long _Cost(int[] statistics, int at, byte register, int count) {
    var gtia = Atari8BitGraphics.Palette;
    var entry = (register & 254) * 3;
    int red = gtia[entry], green = gtia[entry + 1], blue = gtia[entry + 2];

    return (long)count * (red * red + green * green + blue * blue)
      - 2L * (red * statistics[at] + green * statistics[at + 1] + blue * statistics[at + 2])
      + statistics[at + 3];
  }
}
