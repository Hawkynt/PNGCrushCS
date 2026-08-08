using System;
using FileFormat.Core;

namespace FileFormat.IcePcinPlus;

/// <summary>Settles a picture into an ICE PCIN+ screen, two character sets and thirteen colours.</summary>
/// <remarks>
/// The screen is forty cells by twenty-four, and the character codes repeat every three cell rows
/// because the high bit of a code is spent on colour rather than on naming a character. That leaves
/// eight blocks of a hundred and twenty cells against the hundred and twenty-eight characters a
/// block can name, so no two cells need share a character and the content of both character sets is
/// free. What a cell shows is therefore chosen outright rather than fitted to a set the rest of the
/// picture also uses.
/// <para/>
/// A cell is eight screen pixels. Field one draws it as four mode 12 pixels two wide, from the
/// background and three of the four playfield registers; field two draws it as two GTIA 10 pixels
/// four wide, and because its field is read as a mode 0 bitmap rather than as mode 12 the nibbles
/// reach the registers directly rather than through the table two chips disagreeing produce — so all
/// nine are available to it. The two fields read the same character code but different sets, so
/// their bits are independent; the code's high bit is the one thing they share, and it names PF2 or
/// PF3 for field one alone. Both settings are tried for every cell and the cheaper kept.
/// <para/>
/// There is no displacement between the fields to undo. GTIA 10 starts two pixels late, and the
/// reader's left skip of two is exactly that, so a field-two nibble covers the four screen pixels
/// its cell holds and nothing straddles a cell boundary.
/// <para/>
/// The thirteen colour bytes are the whole picture's, not a scanline's, and one of them does double
/// duty as field one's background and field two's first player — so they cannot be deduced from any
/// one part of the picture. They are searched instead, cheaply: a picture reduced to a colour
/// histogram costs a candidate set a pass over the buckets rather than a pass over the pixels, and
/// each register in turn is given every value the hardware holds while the other twelve stand.
/// </remarks>
public static class IcePcinPlusEncoder {

  /// <summary>Mode 12 pixels across: each is two screen pixels wide.</summary>
  private const int _FIRST_PIXELS = IcePcinPlusFile.Width / 2;

  /// <summary>Character cells across the screen.</summary>
  private const int _COLUMNS = IcePcinPlusFile.Width / 8;

  /// <summary>Character cell rows down the screen.</summary>
  private const int _CELL_ROWS = IcePcinPlusFile.Height / 8;

  /// <summary>Blocks the screen is drawn in, each with a character set pair of its own.</summary>
  private const int _BLOCKS = 8;

  /// <summary>Cell rows one block covers.</summary>
  private const int _ROWS_PER_BLOCK = _CELL_ROWS / _BLOCKS;

  /// <summary>Where the first field's character set for the first block is.</summary>
  private const int _FIRST_FONT = 14;

  /// <summary>Where the second field's is: immediately after the first field's.</summary>
  private const int _SECOND_FONT = _FIRST_FONT + 1024;

  /// <summary>How far apart one block's character sets are from the next block's.</summary>
  private const int _BLOCK_STRIDE = 2048;

  /// <summary>Colour bytes a file carries, all of them the whole picture's.</summary>
  private const int _COLORS = 13;

  /// <summary>Colours field one can draw in one cell.</summary>
  private const int _FIRST_CHOICES = 4;

  /// <summary>Colours field two can draw in one cell: every register the chip has.</summary>
  private const int _SECOND_CHOICES = 9;

  /// <summary>Blends one setting of the high bit offers.</summary>
  private const int _BLENDS = _FIRST_CHOICES * _SECOND_CHOICES;

  /// <summary>Bits a colour histogram drops from each channel.</summary>
  private const int _BUCKET_SHIFT = 4;

  /// <summary>Passes the register search makes over the thirteen bytes before giving up.</summary>
  private const int _SWEEPS = 3;

  /// <summary>Which file byte each of field one's four cell colours comes from, background first.</summary>
  /// <remarks>
  /// Mode 12 reads pattern 0 as the background and 1 and 2 as PF0 and PF1; pattern 3 is PF2 or PF3
  /// according to the character code's high bit, which is the only place the two differ.
  /// </remarks>
  private static ReadOnlySpan<byte> _FirstFieldLow => [1, 5, 7, 9];

  private static ReadOnlySpan<byte> _FirstFieldHigh => [1, 5, 7, 11];

  /// <summary>
  /// Which file byte each of field two's nine cell colours comes from, indexed by the nibble that
  /// reaches it: the four players, the four playfield registers, then the background.
  /// </summary>
  /// <remarks>
  /// The remaining seven of the sixteen nibbles are aliases the chip fills — three more of the
  /// background and a second copy of each playfield register — so nothing is lost by never writing
  /// one.
  /// </remarks>
  private static ReadOnlySpan<byte> _SecondFieldBytes => [1, 2, 3, 4, 6, 8, 10, 12, 13];

  /// <summary>Which of field two's slots the initial guess fills in order of how much colour uses.</summary>
  /// <remarks>The shared byte is not among them: field one has already settled it.</remarks>
  private static ReadOnlySpan<byte> _SecondFieldOrder => [2, 3, 4, 6, 8, 10, 12, 13];

  /// <summary>Which of field one's slots the initial guess fills, likewise.</summary>
  /// <remarks>
  /// The shared byte first, because whatever goes there both fields can draw; then PF0 and PF1,
  /// which every cell reaches; then PF2 and PF3, which a cell reaches one of.
  /// </remarks>
  private static ReadOnlySpan<byte> _FirstFieldOrder => [1, 5, 7, 9, 11];

  /// <summary>Turns a 320x192 picture into the bytes of an ICE PCIN+ file.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    var palette = _ChoosePalette(rgb);

    return _Assemble(rgb, palette);
  }

  /// <summary>
  /// The thirteen colour bytes, found by giving each register every value the hardware holds while
  /// the other twelve stand.
  /// </summary>
  /// <remarks>
  /// Judged on a histogram rather than on the picture: what a set of registers is worth is how close
  /// its twenty-eight blends per high bit come to the colours the picture contains, and how those
  /// colours are arranged does not change that. The arrangement matters once — when the cells are
  /// fitted — and by then the registers are settled.
  /// </remarks>
  private static byte[] _ChoosePalette(ReadOnlySpan<byte> rgb) {
    var (colors, weights) = _Buckets(rgb);
    var palette = _InitialPalette(rgb);
    var cost = _PaletteCost(palette, colors, weights);

    for (var sweep = 0; sweep < _SWEEPS; ++sweep) {
      var moved = false;

      for (var register = 1; register <= _COLORS; ++register) {
        var original = palette[register];
        var best = original;

        // The low bit of a colour register never reaches the screen, so only the even values differ.
        for (var candidate = 0; candidate < 256; candidate += 2) {
          palette[register] = (byte)candidate;
          var trial = _PaletteCost(palette, colors, weights);
          if (trial >= cost)
            continue;

          cost = trial;
          best = (byte)candidate;
        }

        palette[register] = best;
        moved |= best != original;
      }

      if (!moved)
        break;
    }

    return palette;
  }

  /// <summary>A first guess: the picture's own colours, shared out by how much of it they cover.</summary>
  /// <remarks>
  /// Field one needs five registers and field two nine, and one byte is both — so the colour the
  /// picture uses most is given to it, where both fields can draw it, and each field's remaining
  /// slots take the next most used of a reduction of its own size.
  /// </remarks>
  private static byte[] _InitialPalette(ReadOnlySpan<byte> rgb) {
    var pixels = rgb.Length / 3;
    var bgra = new byte[pixels * 4];
    for (var i = 0; i < pixels; ++i) {
      bgra[i * 4] = rgb[i * 3 + 2];
      bgra[i * 4 + 1] = rgb[i * 3 + 1];
      bgra[i * 4 + 2] = rgb[i * 3];
      bgra[i * 4 + 3] = 255;
    }

    var palette = new byte[_COLORS + 1];
    var firsts = _RankedRegisters(bgra, pixels, _FirstFieldOrder.Length);
    for (var i = 0; i < _FirstFieldOrder.Length; ++i)
      palette[_FirstFieldOrder[i]] = firsts[i];

    // The shared byte is already spoken for, so field two's own slots take everything but whichever
    // of its colours that byte already covers.
    var seconds = _RankedRegisters(bgra, pixels, _SecondFieldOrder.Length + 1);
    var skip = _NearestOf(seconds, palette[1]);
    var slot = 0;
    for (var i = 0; i < seconds.Length && slot < _SecondFieldOrder.Length; ++i) {
      if (i == skip)
        continue;

      palette[_SecondFieldOrder[slot++]] = seconds[i];
    }

    return palette;
  }

  /// <summary>The picture reduced to a given number of colour registers, most used first.</summary>
  private static byte[] _RankedRegisters(byte[] bgra, int pixels, int count) {
    var quantized = ColorQuantizer.Quantize(bgra, pixels, count);
    var entries = quantized.Count;
    var weights = new long[entries];
    foreach (var index in quantized.Indices)
      if (index >= 0 && index < entries)
        ++weights[index];

    var order = new int[entries];
    for (var i = 0; i < entries; ++i)
      order[i] = i;

    Array.Sort(order, (left, right) => weights[right] != weights[left] ? weights[right].CompareTo(weights[left]) : left.CompareTo(right));

    var registers = new byte[count];
    for (var i = 0; i < count; ++i) {
      // A picture with fewer colours than slots repeats its last one rather than falling to black.
      var entry = order[Math.Min(i, entries - 1)] * 3;
      registers[i] = Atari8BitGraphics.NearestRegister(
        quantized.Palette[entry], quantized.Palette[entry + 1], quantized.Palette[entry + 2]);
    }

    return registers;
  }

  /// <summary>Which of a set of registers is nearest a given one.</summary>
  private static int _NearestOf(ReadOnlySpan<byte> registers, byte register) {
    var gtia = Atari8BitGraphics.Palette;
    var target = (register & 254) * 3;
    var best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < registers.Length; ++i) {
      var entry = (registers[i] & 254) * 3;
      int dr = gtia[entry] - gtia[target], dg = gtia[entry + 1] - gtia[target + 1], db = gtia[entry + 2] - gtia[target + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return best;
  }

  /// <summary>The picture as a colour histogram: each occupied bucket's mean colour and its weight.</summary>
  private static (int[] Colors, long[] Weights) _Buckets(ReadOnlySpan<byte> rgb) {
    const int levels = 256 >> _BUCKET_SHIFT;
    var counts = new int[levels * levels * levels];
    var sums = new long[counts.Length * 3];
    var pixels = rgb.Length / 3;

    for (var i = 0; i < pixels; ++i) {
      var source = i * 3;
      int red = rgb[source], green = rgb[source + 1], blue = rgb[source + 2];
      var key = (((red >> _BUCKET_SHIFT) * levels) + (green >> _BUCKET_SHIFT)) * levels + (blue >> _BUCKET_SHIFT);
      ++counts[key];
      sums[key * 3] += red;
      sums[key * 3 + 1] += green;
      sums[key * 3 + 2] += blue;
    }

    var occupied = 0;
    for (var key = 0; key < counts.Length; ++key)
      if (counts[key] > 0)
        ++occupied;

    var colors = new int[occupied * 3];
    var weights = new long[occupied];
    var at = 0;
    for (var key = 0; key < counts.Length; ++key) {
      var count = counts[key];
      if (count == 0)
        continue;

      // The bucket's own mean, not its centre: the grouping is coarse but what it stands for is not.
      colors[at * 3] = (int)(sums[key * 3] / count);
      colors[at * 3 + 1] = (int)(sums[key * 3 + 1] / count);
      colors[at * 3 + 2] = (int)(sums[key * 3 + 2] / count);
      weights[at] = count;
      ++at;
    }

    return (colors, weights);
  }

  /// <summary>What a set of registers costs the picture, counted over the histogram.</summary>
  private static long _PaletteCost(ReadOnlySpan<byte> palette, int[] colors, long[] weights) {
    Span<int> blends = stackalloc int[2 * _BLENDS * 3];
    Span<int> squares = stackalloc int[2 * _BLENDS];
    _BuildBlends(palette, 0, blends[..(_BLENDS * 3)], squares[.._BLENDS]);
    _BuildBlends(palette, 1, blends[(_BLENDS * 3)..], squares[_BLENDS..]);

    long total = 0;
    for (var bucket = 0; bucket < weights.Length; ++bucket) {
      int red = colors[bucket * 3], green = colors[bucket * 3 + 1], blue = colors[bucket * 3 + 2];
      var best = int.MaxValue;

      for (var entry = 0; entry < 2 * _BLENDS; ++entry) {
        var at = entry * 3;
        int dr = red - blends[at], dg = green - blends[at + 1], db = blue - blends[at + 2];
        var cost = dr * dr + dg * dg + db * db;
        if (cost < best)
          best = cost;
      }

      total += best * weights[bucket];
    }

    return total;
  }

  /// <summary>The colours the two fields average to, for one setting of the character code's high bit.</summary>
  private static void _BuildBlends(ReadOnlySpan<byte> palette, int high, Span<int> colors, Span<int> squares) {
    var gtia = Atari8BitGraphics.Palette;
    var firsts = high == 0 ? _FirstFieldLow : _FirstFieldHigh;
    var seconds = _SecondFieldBytes;

    for (var first = 0; first < _FIRST_CHOICES; ++first)
    for (var second = 0; second < _SECOND_CHOICES; ++second) {
      var entry = first * _SECOND_CHOICES + second;
      var left = (palette[firsts[first]] & 254) * 3;
      var right = (palette[seconds[second]] & 254) * 3;
      var square = 0;

      for (var channel = 0; channel < 3; ++channel) {
        int a = gtia[left + channel], b = gtia[right + channel];
        var blended = (a & b) + (((a ^ b) >> 1) & 0x7F);
        colors[entry * 3 + channel] = blended;
        square += blended * blended;
      }

      // Held doubled because a blend covers two screen pixels of the pair it is judged against.
      squares[entry] = square * 2;
    }
  }

  /// <summary>Fits every cell and writes the file the reader will find them in.</summary>
  private static byte[] _Assemble(ReadOnlySpan<byte> rgb, byte[] palette) {
    var statistics = _PixelStatistics(rgb);
    var data = new byte[IcePcinPlusFile.FileSize];

    // The version byte the reader insists on, then the picture's thirteen colours.
    data[0] = 1;
    for (var i = 1; i <= _COLORS; ++i)
      data[i] = palette[i];

    Span<int> blends = stackalloc int[2 * _BLENDS * 3];
    Span<int> squares = stackalloc int[2 * _BLENDS];
    _BuildBlends(palette, 0, blends[..(_BLENDS * 3)], squares[.._BLENDS]);
    _BuildBlends(palette, 1, blends[(_BLENDS * 3)..], squares[_BLENDS..]);

    Span<byte> firstRows = stackalloc byte[8];
    Span<byte> secondRows = stackalloc byte[8];
    Span<byte> bestFirstRows = stackalloc byte[8];
    Span<byte> bestSecondRows = stackalloc byte[8];

    for (var cellRow = 0; cellRow < _CELL_ROWS; ++cellRow)
    for (var column = 0; column < _COLUMNS; ++column) {
      var bestCost = long.MaxValue;
      var bestHigh = 0;

      for (var high = 0; high < 2; ++high) {
        var cost = _FitCell(
          statistics, blends.Slice(high * _BLENDS * 3, _BLENDS * 3), squares.Slice(high * _BLENDS, _BLENDS),
          cellRow, column, firstRows, secondRows);

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestHigh = high;
        firstRows.CopyTo(bestFirstRows);
        secondRows.CopyTo(bestSecondRows);
      }

      // A block's hundred and twenty cells fit inside the hundred and twenty-eight characters it can
      // name, so every cell gets one to itself and neither field has to compromise over its shape.
      var block = cellRow / _ROWS_PER_BLOCK;
      var character = cellRow % _ROWS_PER_BLOCK * _COLUMNS + column;
      data[IcePcinPlusFile.ScreenOffset + cellRow * _COLUMNS + column] = (byte)((bestHigh << 7) | character);

      var first = _FIRST_FONT + block * _BLOCK_STRIDE + character * 8;
      var second = _SECOND_FONT + block * _BLOCK_STRIDE + character * 8;
      for (var row = 0; row < 8; ++row) {
        data[first + row] = bestFirstRows[row];
        data[second + row] = bestSecondRows[row];
      }
    }

    return data;
  }

  /// <summary>
  /// The cheapest the eight rows of one cell can be drawn for one setting of the high bit, and the
  /// character-set bytes that draw them.
  /// </summary>
  /// <remarks>
  /// A field-two pixel covers two field-one pixels, so the two are not independent — but the chain
  /// is only two links long and closes inside a half cell, so trying all nine of field two's colours
  /// against the best field one can answer with settles it outright.
  /// </remarks>
  private static long _FitCell(
    int[] statistics, ReadOnlySpan<int> blends, ReadOnlySpan<int> squares,
    int cellRow, int column, Span<byte> firstRows, Span<byte> secondRows) {
    long total = 0;

    for (var row = 0; row < 8; ++row) {
      var y = cellRow * 8 + row;
      var firstPattern = 0;
      var secondPattern = 0;

      for (var half = 0; half < 2; ++half) {
        var left = (y * _FIRST_PIXELS + column * 4 + half * 2) * 4;
        var right = left + 4;
        var bestCost = long.MaxValue;
        int bestSecond = 0, bestLeft = 0, bestRight = 0;

        for (var second = 0; second < _SECOND_CHOICES; ++second) {
          long leftCost = long.MaxValue, rightCost = long.MaxValue;
          int leftPick = 0, rightPick = 0;

          for (var first = 0; first < _FIRST_CHOICES; ++first) {
            var entry = first * _SECOND_CHOICES + second;
            var at = entry * 3;
            int red = blends[at], green = blends[at + 1], blue = blends[at + 2];
            long doubled = squares[entry];

            var cost = doubled
              - 2L * (red * statistics[left] + green * statistics[left + 1] + blue * statistics[left + 2])
              + statistics[left + 3];
            if (cost < leftCost) {
              leftCost = cost;
              leftPick = first;
            }

            cost = doubled
              - 2L * (red * statistics[right] + green * statistics[right + 1] + blue * statistics[right + 2])
              + statistics[right + 3];
            if (cost < rightCost) {
              rightCost = cost;
              rightPick = first;
            }
          }

          if (leftCost + rightCost >= bestCost)
            continue;

          bestCost = leftCost + rightCost;
          bestSecond = second;
          bestLeft = leftPick;
          bestRight = rightPick;
        }

        total += bestCost;

        // Mode 12 packs its four pixels most significant pair first; the mode 0 byte GTIA 10 reads
        // holds its two nibbles likewise, and a half cell is one of each pair.
        firstPattern |= (bestLeft << (6 - half * 4)) | (bestRight << (4 - half * 4));
        secondPattern |= bestSecond << (4 - half * 4);
      }

      firstRows[row] = (byte)firstPattern;
      secondRows[row] = (byte)secondPattern;
    }

    return total;
  }

  /// <summary>What each pair of screen pixels sums to, which is all the cell fitting needs of them.</summary>
  /// <remarks>
  /// A mode 12 pixel is two screen pixels wide, so no choice can tell them apart. The error of a
  /// colour against the pair expands into the colour's own square, its product with their sums, and
  /// their sum of squares, so the pixels are never touched again once these are taken.
  /// </remarks>
  private static int[] _PixelStatistics(ReadOnlySpan<byte> rgb) {
    var statistics = new int[IcePcinPlusFile.Height * _FIRST_PIXELS * 4];
    var at = 0;

    for (var y = 0; y < IcePcinPlusFile.Height; ++y)
    for (var pixel = 0; pixel < _FIRST_PIXELS; ++pixel) {
      int red = 0, green = 0, blue = 0, squares = 0;

      for (var half = 0; half < 2; ++half) {
        var source = ((y * IcePcinPlusFile.Width) + pixel * 2 + half) * 3;
        int r = rgb[source], g = rgb[source + 1], b = rgb[source + 2];
        red += r;
        green += g;
        blue += b;
        squares += r * r + g * g + b * b;
      }

      statistics[at++] = red;
      statistics[at++] = green;
      statistics[at++] = blue;
      statistics[at++] = squares;
    }

    return statistics;
  }
}
