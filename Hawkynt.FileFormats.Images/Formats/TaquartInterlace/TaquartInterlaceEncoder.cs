using System;
using FileFormat.Core;

namespace FileFormat.TaquartInterlace;

/// <summary>Settles a picture into a Taquart Interlace Picture's three differently phased fields.</summary>
/// <remarks>
/// A displayed column takes its luminance from one Graphics 9 nibble, its hue from a Graphics 11
/// nibble in step with that one, and its second luminance from a Graphics 10 nibble two pixels out
/// of step — so within any four columns the two luminance fields disagree about where their groups
/// end for half of it. The picture is therefore not a grid of independent cells but a chain: each
/// Graphics 10 nibble is shared between the luminance-and-hue pair to its left and the pair to its
/// right, and choosing one settles nothing until both are settled too.
/// <para/>
/// A chain of that shape is what dynamic programming is for. One pass along a scanline, carrying a
/// state of the hue and the Graphics 9 luminance and stepping through the Graphics 10 nibble that
/// joins one to the next, is exact — a greedy pass is right in the middle of a row and wrong at its
/// ends, because the first and last nibbles have a neighbour on one side only.
/// <para/>
/// Vertically the rows are a chain as well, but a shorter one: only the odd displayed rows carry a
/// stored luminance, and each even row is the mean of the two around it. A row is therefore fitted
/// knowing the row above, which is already settled, and the row below it is fitted knowing this one
/// — so every displayed row is paid for exactly once and none is fitted blind.
/// <para/>
/// Hue is not searched exhaustively. Sixteen hues against sixteen luminances and eight second
/// luminances is more state than the chain needs, so each nibble first names the few hues that could
/// cover the colours it has to draw at all, and the pass along the row is exact within those.
/// </remarks>
public static class TaquartInterlaceEncoder {

  /// <summary>Displayed columns one nibble of the Graphics 9 and Graphics 11 fields covers.</summary>
  private const int _NIBBLE_COLUMNS = 4;

  /// <summary>Luminances the Graphics 10 field can name: the eight registers that are not aliases.</summary>
  private const int _SECONDS = 8;

  /// <summary>Hues a nibble carries forward from the cheap search into the exact one.</summary>
  private const int _HUES = 4;

  /// <summary>Luminances a Graphics 9 nibble can name.</summary>
  private const int _LUMINANCES = 16;

  /// <summary>States the pass along a row carries: a shortlisted hue and a luminance.</summary>
  private const int _STATES = _HUES * _LUMINANCES;

  /// <summary>Turns a picture of the stored size doubled into the bytes of a Taquart file.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rgb, int storedWidth, int storedHeight) {
    var width = storedWidth * 2;
    var height = storedHeight * 2;
    var stride = storedWidth >> 2;
    var nibbles = storedWidth >> 1;
    var fieldLength = stride * storedHeight;

    var data = new byte[TaquartInterlaceFile.FieldsOffset + fieldLength * 3];
    TaquartInterlaceFile.Signature.CopyTo(data);
    data[5] = (byte)storedWidth;
    data[6] = (byte)storedHeight;
    data[7] = (byte)fieldLength;
    data[8] = (byte)(fieldLength >> 8);

    var blends = _BlendTable();
    var entries = Atari8BitGraphics.ExpandGr10Registers(TaquartInterlaceFile.Registers);
    var seconds = new int[_SECONDS];
    for (var i = 0; i < _SECONDS; ++i)
      seconds[i] = entries[i] & 15;

    // What each displayed pixel asks for, widened once so that the fitting, which asks again and
    // again, is not converting bytes every time.
    var targets = new int[width * height * 3];
    for (var at = 0; at < targets.Length; ++at)
      targets[at] = rgb[at];

    // The row above's two luminances, per displayed column: an even row is their mean with this
    // row's, and the first row has nothing above it.
    var aboveFirst = new int[width];
    var aboveSecond = new int[width];
    var rowFirst = new int[width];
    var rowSecond = new int[width];

    var hues = new int[nibbles * _HUES];
    var luminance = new int[nibbles];
    var hue = new int[nibbles];
    var second = new int[nibbles];
    var statePick = new byte[nibbles * _SECONDS];
    var secondPick = new byte[nibbles * _STATES];
    var carried = new long[_SECONDS];
    var reached = new long[_SECONDS];
    var states = new long[_STATES];

    for (var row = 0; row < storedHeight; ++row) {
      _ShortlistHues(targets, width, row, nibbles, hues);
      _FitRow(
        targets, blends, seconds, width, row, nibbles, hues,
        aboveFirst, aboveSecond, statePick, secondPick, carried, reached, states,
        luminance, hue, second);

      var first = TaquartInterlaceFile.FieldsOffset + row * stride;
      for (var nibble = 0; nibble < nibbles; ++nibble) {
        // All three fields pack a nibble the same way: two to a byte, the earlier one high.
        var at = nibble >> 1;
        var shift = (nibble & 1) == 0 ? 4 : 0;
        data[first + at] |= (byte)(luminance[nibble] << shift);
        data[first + fieldLength + at] |= (byte)(second[nibble] << shift);
        data[first + fieldLength * 2 + at] |= (byte)(hue[nibble] << shift);
      }

      for (var column = 0; column < width; ++column) {
        rowFirst[column] = luminance[Math.Min((column + 1) >> 2, nibbles - 1)];
        rowSecond[column] = column == 0 ? 0 : seconds[second[(column - 1) >> 2]];
      }

      (aboveFirst, rowFirst) = (rowFirst, aboveFirst);
      (aboveSecond, rowSecond) = (rowSecond, aboveSecond);
    }

    return data;
  }

  /// <summary>The colours a hue and a pair of luminances average to.</summary>
  /// <remarks>
  /// Both fields draw the same hue at a given column — one hue field serves them both, which is what
  /// keeps the file to three equal parts — so a displayed colour is the mean of two entries of one
  /// row of the palette, and there are only sixteen by sixteen by sixteen of those.
  /// </remarks>
  private static int[] _BlendTable() {
    var gtia = Atari8BitGraphics.Palette;
    var colors = new int[16 * _LUMINANCES * _LUMINANCES * 3];

    for (var tint = 0; tint < 16; ++tint)
    for (var first = 0; first < _LUMINANCES; ++first)
    for (var other = 0; other < _LUMINANCES; ++other) {
      var entry = (tint * _LUMINANCES + first) * _LUMINANCES + other;
      var left = ((tint << 4) | first) * 3;
      var right = ((tint << 4) | other) * 3;

      for (var channel = 0; channel < 3; ++channel) {
        int a = gtia[left + channel], b = gtia[right + channel];
        colors[entry * 3 + channel] = (a & b) + (((a ^ b) >> 1) & 0x7F);
      }
    }

    return colors;
  }

  /// <summary>The few hues each nibble could be drawn in, judged before the luminances are settled.</summary>
  /// <remarks>
  /// A hue covers four displayed columns on two rows, and whether it can cover them at all does not
  /// depend much on which luminances end up beside it — so the shortlist is taken against the single
  /// nearest luminance, which is cheap, and the pass along the row then weighs what is left exactly.
  /// </remarks>
  private static void _ShortlistHues(int[] targets, int width, int row, int nibbles, int[] hues) {
    var gtia = Atari8BitGraphics.Palette;
    Span<long> scores = stackalloc long[16];

    for (var nibble = 0; nibble < nibbles; ++nibble) {
      scores.Clear();

      for (var offset = 0; offset < _NIBBLE_COLUMNS; ++offset) {
        // A nibble covers the columns one to the left of its own four, the field being displaced.
        var column = nibble * _NIBBLE_COLUMNS + offset - 1;
        if (column < 0 || column >= width - 1)
          continue;

        for (var half = 0; half < 2; ++half) {
          var at = ((row * 2 + half) * width + column) * 3;
          int red = targets[at], green = targets[at + 1], blue = targets[at + 2];

          for (var tint = 0; tint < 16; ++tint) {
            var best = long.MaxValue;
            for (var level = 0; level < _LUMINANCES; ++level) {
              var entry = ((tint << 4) | level) * 3;
              long dr = red - gtia[entry], dg = green - gtia[entry + 1], db = blue - gtia[entry + 2];
              var cost = dr * dr + dg * dg + db * db;
              if (cost < best)
                best = cost;
            }

            scores[tint] += best;
          }
        }
      }

      for (var slot = 0; slot < _HUES; ++slot) {
        var best = 0;
        for (var tint = 1; tint < 16; ++tint)
          if (scores[tint] < scores[best])
            best = tint;

        hues[nibble * _HUES + slot] = best;
        scores[best] = long.MaxValue;
      }
    }
  }

  /// <summary>
  /// Settles one stored row: one pass along the chain of nibbles, exact within the shortlisted hues.
  /// </summary>
  /// <remarks>
  /// The chain's links are the Graphics 10 nibbles. Nibble n of that field covers the two columns to
  /// the right of the luminance-and-hue nibble n and the two columns to the left of nibble n + 1, so
  /// the pass alternates between what a Graphics 10 nibble can reach given the pair before it and
  /// what the pair after it can reach given that Graphics 10 nibble. Nothing else in a row is
  /// coupled, so one forward pass and one walk back name the whole of it.
  /// </remarks>
  private static void _FitRow(
    int[] targets, int[] blends, int[] seconds, int width, int row, int nibbles, int[] hues,
    int[] aboveFirst, int[] aboveSecond, byte[] statePick, byte[] secondPick,
    long[] carried, long[] reached, long[] states,
    int[] luminance, int[] hue, int[] second) {
    var odd = (row * 2 + 1) * width;
    var even = row * 2 * width;

    // The first displayed column is the one the Graphics 10 field never reaches: its own timing puts
    // it a pixel to the right, and what falls off the left takes the first register.
    for (var state = 0; state < _STATES; ++state)
      states[state] = _SegmentCost(
        targets, blends, width, odd, even, 0, 1,
        hues[state / _LUMINANCES], state % _LUMINANCES, 0, aboveFirst[0], aboveSecond[0]);

    for (var nibble = 0; nibble < nibbles; ++nibble) {
      if (nibble > 0) {
        // The two columns between this nibble's pair and the last one, which the previous Graphics
        // 10 nibble covers together with this pair.
        var start = nibble * _NIBBLE_COLUMNS - 1;
        for (var state = 0; state < _STATES; ++state) {
          var best = long.MaxValue;
          var pick = 0;

          for (var link = 0; link < _SECONDS; ++link) {
            var cost = carried[link] + _SegmentCost(
              targets, blends, width, odd, even, start, 2,
              hues[nibble * _HUES + state / _LUMINANCES], state % _LUMINANCES, seconds[link],
              aboveFirst[start], aboveSecond[start]);

            if (cost >= best)
              continue;

            best = cost;
            pick = link;
          }

          states[state] = best;
          secondPick[nibble * _STATES + state] = (byte)pick;
        }
      }

      // The two columns this nibble's pair shares with the Graphics 10 nibble of the same number.
      var own = nibble * _NIBBLE_COLUMNS + 1;
      for (var link = 0; link < _SECONDS; ++link) {
        var best = long.MaxValue;
        var pick = 0;

        for (var state = 0; state < _STATES; ++state) {
          var cost = states[state] + _SegmentCost(
            targets, blends, width, odd, even, own, 2,
            hues[nibble * _HUES + state / _LUMINANCES], state % _LUMINANCES, seconds[link],
            aboveFirst[own], aboveSecond[own]);

          if (cost >= best)
            continue;

          best = cost;
          pick = state;
        }

        reached[link] = best;
        statePick[nibble * _SECONDS + link] = (byte)pick;
      }

      Array.Copy(reached, carried, _SECONDS);
    }

    var lastLink = 0;
    for (var link = 1; link < _SECONDS; ++link)
      if (carried[link] < carried[lastLink])
        lastLink = link;

    for (var nibble = nibbles - 1; nibble >= 0; --nibble) {
      var state = statePick[nibble * _SECONDS + lastLink];
      luminance[nibble] = state % _LUMINANCES;
      hue[nibble] = hues[nibble * _HUES + state / _LUMINANCES];
      second[nibble] = lastLink;
      lastLink = secondPick[nibble * _STATES + state];
    }
  }

  /// <summary>
  /// What one run of columns costs on both of the displayed rows a stored row is drawn on.
  /// </summary>
  /// <remarks>
  /// The odd row is the one that carries the luminances, so it shows the two fields as they stand.
  /// The even row above it has none of its own and takes the mean of this row's and the row above's,
  /// which is why the row above has to be settled before this one can be.
  /// </remarks>
  private static long _SegmentCost(
    int[] targets, int[] blends, int width, int odd, int even, int start, int count,
    int tint, int first, int other, int aboveFirst, int aboveSecond) {
    var stored = (tint * _LUMINANCES + first) * _LUMINANCES + other;
    var meaned = (tint * _LUMINANCES + ((aboveFirst + first) >> 1)) * _LUMINANCES + ((aboveSecond + other) >> 1);
    long total = 0;

    for (var offset = 0; offset < count; ++offset) {
      var column = start + offset;

      // The last displayed column takes its luminance from the next row's first nibble, which this
      // row cannot say anything about, so it is left out of what this row is judged on.
      if (column < 0 || column >= width - 1)
        continue;

      total += _PixelCost(targets, blends, (odd + column) * 3, stored);
      total += _PixelCost(targets, blends, (even + column) * 3, meaned);
    }

    return total;
  }

  private static long _PixelCost(int[] targets, int[] blends, int at, int entry) {
    var color = entry * 3;
    long dr = targets[at] - blends[color], dg = targets[at + 1] - blends[color + 1], db = targets[at + 2] - blends[color + 2];

    return dr * dr + dg * dg + db * db;
  }
}
