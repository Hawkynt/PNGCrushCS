using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.GedPicture;

/// <summary>Settles a picture into a GED file's colour tables and playfield.</summary>
/// <remarks>
/// A scanline is a four-colour playfield whose three colours are rewritten six times as it is drawn,
/// which gives it six segments of four colours rather than four colours outright. The rewrites are
/// not independent: each register keeps its new value until it is written again, so the first
/// segment's three colours also serve part of the second and third, and the eight tables the file
/// stores overlap the six segments in a fixed pattern.
/// <para/>
/// The fourth colour is the background, which is one register for the whole picture — except that a
/// file may poke one register of its choosing per scanline, and the background is a register. So the
/// poke is spent on it, which turns a picture-wide colour into a per-scanline one and gives every
/// segment a fourth colour that costs nothing.
/// <para/>
/// Where the six segments fall is not a choice either. Rewriting a register costs cycles the
/// processor has to find between ANTIC's fetches, so the positions follow from the timing, and the
/// file names which of eight timings it was drawn against rather than the positions. All eight are
/// tried and the one the picture fits best is kept.
/// </remarks>
public static class GedPictureEncoder {

  /// <summary>Two-bit pixels one scanline holds; each covers two of the screen's.</summary>
  public const int PlayfieldPixels = GedPictureFile.Width / 2;

  /// <summary>Colour tables a file carries, each with one entry per scanline.</summary>
  public const int TableCount = 8;

  /// <summary>Colours a scanline settles: the background and the eight tables.</summary>
  private const int _SLOTS = TableCount + 1;

  /// <summary>Segments a scanline is drawn in, one per register rewrite and one before them.</summary>
  private const int _SEGMENTS = 6;

  /// <summary>Timings the register writes may have been made against.</summary>
  private const int _CYCLES = 8;

  /// <summary>Distinct colours of a scanline that are worth trying in its registers.</summary>
  /// <remarks>
  /// Only colours the scanline actually contains: a register holding anything else is a register
  /// holding nothing the picture asked for. Nine is all a scanline can show, so a scanline the
  /// format could have drawn brings every colour it needs and the search can reach it exactly.
  /// </remarks>
  private const int _CANDIDATES = 20;

  /// <summary>Passes of settling one colour at a time against the other eight.</summary>
  private const int _ROUNDS = 5;

  /// <summary>Every how many scanlines the timings are compared against each other.</summary>
  private const int _CYCLE_SAMPLE = 4;

  /// <summary>
  /// Which of the nine colours each segment's four registers hold: the background, then the
  /// playfield ones as the rewrites have left them.
  /// </summary>
  private static ReadOnlySpan<byte> _SegmentSlots => [
    0, 1, 2, 3,
    0, 4, 2, 3,
    0, 4, 5, 3,
    0, 4, 5, 6,
    0, 7, 5, 6,
    0, 7, 8, 6,
  ];

  /// <summary>The chip register the per-scanline poke is spent on: the background colour.</summary>
  private const byte _BACKGROUND_REGISTER = 26;

  /// <summary>A priority arrangement that ranks nothing, there being no sprites to rank.</summary>
  private const byte _PRIORITY = 1;

  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    var gtia = Atari8BitGraphics.Palette;

    // Where each segment ends, per timing, in two-pixel columns from the left of the picture.
    var bounds = new int[_CYCLES * _SEGMENTS];
    for (var cycle = 0; cycle < _CYCLES; ++cycle)
      _Bounds(cycle, bounds.AsSpan(cycle * _SEGMENTS, _SEGMENTS));

    var segments = new byte[_CYCLES * PlayfieldPixels];
    for (var cycle = 0; cycle < _CYCLES; ++cycle)
    for (int pixel = 0, segment = 0; pixel < PlayfieldPixels; ++pixel) {
      while (pixel >= bounds[cycle * _SEGMENTS + segment])
        ++segment;

      segments[cycle * PlayfieldPixels + pixel] = (byte)segment;
    }

    var chosen = new byte[GedPictureFile.Height * _SLOTS];
    var costs = new long[_CYCLES];
    var distances = new long[PlayfieldPixels * _CANDIDATES];
    var candidates = new byte[_CANDIDATES];
    var scratch = new byte[_SLOTS];

    // Which timing to use is one byte for the whole picture, so it is judged on a sample of the
    // scanlines rather than on all of them: settling every scanline against all eight timings and
    // then throwing seven eighths of the work away costs more than the choice is worth.
    for (var y = 0; y < GedPictureFile.Height; y += _CYCLE_SAMPLE) {
      var sampled = _Candidates(rgb, gtia, y, candidates, distances);

      for (var cycle = 0; cycle < _CYCLES; ++cycle)
        costs[cycle] += _SolveRow(
          distances, candidates, sampled, segments.AsSpan(cycle * PlayfieldPixels, PlayfieldPixels), scratch);
    }

    var best = 0;
    for (var cycle = 1; cycle < _CYCLES; ++cycle)
      if (costs[cycle] < costs[best])
        best = cycle;

    var chosenSegments = segments.AsSpan(best * PlayfieldPixels, PlayfieldPixels);
    for (var y = 0; y < GedPictureFile.Height; ++y) {
      var count = _Candidates(rgb, gtia, y, candidates, distances);
      _SolveRow(distances, candidates, count, chosenSegments, chosen.AsSpan(y * _SLOTS, _SLOTS));
    }

    return _Assemble(rgb, gtia, chosen, chosenSegments, best);
  }

  /// <summary>Where each of a timing's six segments ends, counted in two-pixel columns.</summary>
  /// <remarks>
  /// The picture begins at position 48 and ends at 208, and the six ends follow from the timing
  /// exactly as the display produces them — one of them differently for the four slower timings,
  /// where the second rewrite has stopped fitting where the faster ones put it.
  /// </remarks>
  private static void _Bounds(int cycle, Span<int> bounds) {
    var first = 63 + (cycle << 3);
    var second = cycle < 4 ? first + 32 : 107 + (cycle << 2);
    var third = 123 + (cycle << 2);

    bounds[0] = first - 48;
    bounds[1] = second - 48;
    bounds[2] = third - 48;
    bounds[3] = third + 24 - 48;
    bounds[4] = third + 48 - 48;
    bounds[5] = PlayfieldPixels;
  }

  /// <summary>
  /// The colours worth trying in one scanline's registers, and what each of its pixels costs against
  /// every one of them.
  /// </summary>
  private static int _Candidates(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, int y, Span<byte> candidates, Span<long> distances) {
    var row = y * GedPictureFile.Width * 3;
    var counts = new Dictionary<byte, int>();

    for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
      var (red, green, blue) = _Target(rgb, row, pixel);
      var color = Atari8BitGraphics.FindNearestColorByte(gtia, red, green, blue);
      counts[color] = counts.GetValueOrDefault(color) + 1;
    }

    var ordered = new List<KeyValuePair<byte, int>>(counts);
    ordered.Sort((left, right) => right.Value != left.Value ? right.Value - left.Value : left.Key - right.Key);

    var count = Math.Min(ordered.Count, _CANDIDATES);
    for (var entry = 0; entry < count; ++entry)
      candidates[entry] = ordered[entry].Key;

    for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
      var (red, green, blue) = _Target(rgb, row, pixel);
      for (var entry = 0; entry < count; ++entry) {
        var at = candidates[entry] * 3;
        long dr = gtia[at] - red, dg = gtia[at + 1] - green, db = gtia[at + 2] - blue;
        distances[pixel * _CANDIDATES + entry] = dr * dr + dg * dg + db * db;
      }
    }

    return count;
  }

  /// <summary>What one two-pixel column of the picture asks for.</summary>
  private static (byte Red, byte Green, byte Blue) _Target(ReadOnlySpan<byte> rgb, int row, int pixel) {
    var left = row + pixel * 6;

    return (
      (byte)((rgb[left] + rgb[left + 3]) >> 1),
      (byte)((rgb[left + 1] + rgb[left + 4]) >> 1),
      (byte)((rgb[left + 2] + rgb[left + 5]) >> 1));
  }

  /// <summary>
  /// Settles one scanline's nine colours against one timing, and says what the scanline costs.
  /// </summary>
  /// <remarks>
  /// One colour at a time against the other eight, repeatedly. The nine are not separable — the
  /// segments overlap, so moving one changes what the two either side of it are worth — but each is
  /// cheap to settle once the others are fixed, and the picture's own colours are the only ones
  /// worth trying, so a handful of passes reaches a scanline the format could have drawn.
  /// </remarks>
  private static long _SolveRow(
    ReadOnlySpan<long> distances, ReadOnlySpan<byte> candidates, int count,
    ReadOnlySpan<byte> segments, Span<byte> chosen) {
    var slots = _SegmentSlots;
    Span<int> values = stackalloc int[_SLOTS];
    Span<long> others = stackalloc long[PlayfieldPixels];

    _Guess(distances, count, segments, values);

    Span<short> affected = stackalloc short[PlayfieldPixels];
    var total = 0L;

    for (var round = 0; round < _ROUNDS; ++round) {
      for (var slot = 0; slot < _SLOTS; ++slot) {
        var fixedCost = 0L;
        var reached = 0;

        for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
          var segment = segments[pixel] * 4;
          var best = long.MaxValue;
          var serves = false;

          for (var register = 0; register < 4; ++register) {
            if (slots[segment + register] == slot) {
              serves = true;
              continue;
            }

            best = Math.Min(best, distances[pixel * _CANDIDATES + values[slots[segment + register]]]);
          }

          if (serves)
            affected[reached++] = (short)pixel;
          else
            fixedCost += best;

          others[pixel] = best;
        }

        var bestEntry = values[slot];
        var bestTotal = long.MaxValue;

        for (var entry = 0; entry < count; ++entry) {
          var candidate = fixedCost;
          for (var at = 0; at < reached; ++at) {
            var pixel = affected[at];
            candidate += Math.Min(others[pixel], distances[pixel * _CANDIDATES + entry]);
          }

          if (candidate >= bestTotal)
            continue;

          bestTotal = candidate;
          bestEntry = entry;
        }

        values[slot] = bestEntry;
        total = bestTotal;
      }

      // Two colours may be in each other's registers, which no single move can undo: a segment that
      // holds both is as happy either way, and the segment that holds only one pays for both. The
      // exchange has to be tried as one move.
      for (var first = 0; first < _SLOTS; ++first)
      for (var second = first + 1; second < _SLOTS; ++second) {
        (values[first], values[second]) = (values[second], values[first]);
        var exchanged = _Cost(distances, slots, segments, values);
        if (exchanged < total)
          total = exchanged;
        else
          (values[first], values[second]) = (values[second], values[first]);
      }
    }

    for (var slot = 0; slot < _SLOTS; ++slot)
      chosen[slot] = candidates[values[slot]];

    return total;
  }

  /// <summary>What a scanline costs with its nine colours as they stand.</summary>
  private static long _Cost(
    ReadOnlySpan<long> distances, ReadOnlySpan<byte> slots, ReadOnlySpan<byte> segments, ReadOnlySpan<int> values) {
    var total = 0L;

    for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
      var segment = segments[pixel] * 4;
      var best = long.MaxValue;
      for (var register = 0; register < 4; ++register)
        best = Math.Min(best, distances[pixel * _CANDIDATES + values[slots[segment + register]]]);

      total += best;
    }

    return total;
  }

  /// <summary>
  /// A first reading of a scanline's nine colours, deduced from which segments each colour appears
  /// in rather than searched for.
  /// </summary>
  /// <remarks>
  /// The overlaps that make the nine hard to settle are also what identifies them. Every segment
  /// shows four colours, and the four sets say between them which is which: one colour appears in
  /// all six and is the background; of the three others in the first segment, the one the second
  /// segment does not show is the first register's, and of the remaining two the one the third
  /// segment shows is the third register's. Each later segment then brings exactly one colour none
  /// before it had.
  /// <para/>
  /// Deduced rather than searched because a search cannot get out of an exchange: two colours in
  /// each other's registers cost nothing in the segments holding both and everything in the one
  /// holding one, and three of them in a ring cannot be undone by exchanging any two.
  /// </remarks>
  private static void _Guess(
    ReadOnlySpan<long> distances, int count, ReadOnlySpan<byte> segments, Span<int> values) {
    Span<int> counts = stackalloc int[_SEGMENTS * _CANDIDATES];
    Span<int> totals = stackalloc int[_CANDIDATES];
    counts.Clear();
    totals.Clear();

    for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
      var nearest = 0;
      for (var entry = 1; entry < count; ++entry)
        if (distances[pixel * _CANDIDATES + entry] < distances[pixel * _CANDIDATES + nearest])
          nearest = entry;

      ++counts[segments[pixel] * _CANDIDATES + nearest];
      ++totals[nearest];
    }

    // The four commonest colours of each segment, which is all a segment can show.
    Span<int> sets = stackalloc int[_SEGMENTS * 4];
    Span<int> sizes = stackalloc int[_SEGMENTS];
    for (var segment = 0; segment < _SEGMENTS; ++segment) {
      var size = 0;
      for (var slot = 0; slot < 4; ++slot) {
        var best = -1;
        for (var entry = 0; entry < count; ++entry) {
          if (counts[segment * _CANDIDATES + entry] == 0 || _Has(sets, segment * 4, size, entry))
            continue;

          if (best < 0 || counts[segment * _CANDIDATES + entry] > counts[segment * _CANDIDATES + best])
            best = entry;
        }

        if (best < 0)
          break;

        sets[segment * 4 + size++] = best;
      }

      sizes[segment] = size;
    }

    var background = 0;
    var reach = -1;
    for (var entry = 0; entry < count; ++entry) {
      var seen = 0;
      for (var segment = 0; segment < _SEGMENTS; ++segment)
        if (_Has(sets, segment * 4, sizes[segment], entry))
          ++seen;

      if (seen > reach || (seen == reach && totals[entry] > totals[background])) {
        reach = seen;
        background = entry;
      }
    }

    values[0] = background;

    // The first segment shows the background and the first three registers, and the segments after
    // it say which of the three is which.
    Span<int> rest = stackalloc int[4];
    var remaining = 0;
    for (var slot = 0; slot < sizes[0]; ++slot)
      if (sets[slot] != background)
        rest[remaining++] = sets[slot];

    values[1] = _TakeUnseen(rest, ref remaining, sets, 4, sizes[1], false, background);
    values[3] = _TakeUnseen(rest, ref remaining, sets, 8, sizes[2], true, background);
    values[2] = remaining > 0 ? rest[0] : background;

    // Every segment after the first brings one colour none before it showed.
    values[4] = _Fresh(sets, 4, sizes[1], values[0], values[2], values[3]);
    values[5] = _Fresh(sets, 8, sizes[2], values[0], values[4], values[3]);
    values[6] = _Fresh(sets, 12, sizes[3], values[0], values[4], values[5]);
    values[7] = _Fresh(sets, 16, sizes[4], values[0], values[5], values[6]);
    values[8] = _Fresh(sets, 20, sizes[5], values[0], values[7], values[6]);
  }

  private static bool _Has(ReadOnlySpan<int> sets, int offset, int size, int entry) {
    for (var slot = 0; slot < size; ++slot)
      if (sets[offset + slot] == entry)
        return true;

    return false;
  }

  /// <summary>
  /// Takes from the first segment's colours the one the next segment does or does not also show.
  /// </summary>
  private static int _TakeUnseen(
    Span<int> rest, ref int remaining, ReadOnlySpan<int> sets, int offset, int size, bool wanted, int fallback) {
    var taken = -1;
    for (var slot = 0; slot < remaining && taken < 0; ++slot)
      if (_Has(sets, offset, size, rest[slot]) == wanted)
        taken = slot;

    if (taken < 0)
      return remaining > 0 ? rest[--remaining] : fallback;

    var value = rest[taken];
    rest[taken] = rest[--remaining];

    return value;
  }

  /// <summary>The colour a segment shows that the three registers carried into it do not.</summary>
  private static int _Fresh(ReadOnlySpan<int> sets, int offset, int size, int first, int second, int third) {
    for (var slot = 0; slot < size; ++slot) {
      var entry = sets[offset + slot];
      if (entry != first && entry != second && entry != third)
        return entry;
    }

    return size > 0 ? sets[offset] : first;
  }

  /// <summary>Writes the tables, the pokes and the playfield out.</summary>
  private static byte[] _Assemble(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, ReadOnlySpan<byte> chosen,
    ReadOnlySpan<byte> segments, int cycle) {
    var data = new byte[GedPictureFile.FileSize];
    GedPictureFile.Signature.CopyTo(data);

    data[3290] = 0;
    data[3291] = 0;
    data[3292] = _PRIORITY;
    data[3300] = (byte)cycle;

    var slots = _SegmentSlots;

    for (var y = 0; y < GedPictureFile.Height; ++y) {
      var line = chosen.Slice(y * _SLOTS, _SLOTS);

      data[GedPictureFile.PokeAddressOffset + y] = _BACKGROUND_REGISTER;
      data[GedPictureFile.PokeValueOffset + y] = line[0];
      for (var table = 0; table < TableCount; ++table)
        data[GedPictureFile.ColorTablesOffset + table * GedPictureFile.Height + y] = line[table + 1];

      var row = y * GedPictureFile.Width * 3;
      for (var pixel = 0; pixel < PlayfieldPixels; ++pixel) {
        var segment = segments[pixel] * 4;
        var (red, green, blue) = _Target(rgb, row, pixel);

        var best = 0;
        var bestCost = long.MaxValue;
        for (var register = 0; register < 4; ++register) {
          var at = line[slots[segment + register]] * 3;
          long dr = gtia[at] - red, dg = gtia[at + 1] - green, db = gtia[at + 2] - blue;
          var cost = dr * dr + dg * dg + db * db;
          if (cost >= bestCost)
            continue;

          bestCost = cost;
          best = register;
        }

        data[GedPictureFile.PlayfieldOffset + y * GedPictureFile.Columns + (pixel >> 2)]
          |= (byte)(best << (6 - (pixel & 3) * 2));
      }
    }

    return data;
  }
}
