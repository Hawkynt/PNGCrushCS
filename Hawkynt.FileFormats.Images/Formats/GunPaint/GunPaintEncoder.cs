using System;
using FileFormat.Core;

namespace FileFormat.GunPaint;

/// <summary>Settles a picture into the two displaced FLI screens a GunPaint file holds.</summary>
/// <remarks>
/// The two fields divide the screen into two-pixel blocks and the second field's blocks fall between
/// the first's, so every pixel but the leftmost is the average of one block from each field, and
/// every block is shared by two pixels. A scanline is therefore a chain, and one pass of dynamic
/// programming settles it exactly — the same shape as Hard Interlace, and for the same reason: a
/// pass that improves one block at a time settles on whichever reading it met first, and several
/// pairings of colours average to the same shade.
/// <para/>
/// What the chain cannot decide is which colours it may draw from. A field has four per character
/// cell per scanline: the scanline's background, the cell's colour RAM, and two of its own. The
/// background is one byte a scanline and the colour RAM one byte per eight, so both are settled
/// across everything that shares them, and only the two matrix colours are free per scanline. So the
/// chain is walked twice: once with all sixteen colours, to find out what the picture is asking for,
/// and once with the four each cell turned out to be able to offer.
/// </remarks>
public static class GunPaintEncoder {

  /// <summary>Colours the machine has.</summary>
  private const int _COLORS = Commodore64Graphics.ColorCount;

  /// <summary>Character cells across the visible picture.</summary>
  public const int CellColumns = GunPaintFile.Width / 8;

  /// <summary>Character cells down it.</summary>
  public const int CellRows = GunPaintFile.Height / 8;

  /// <summary>
  /// Blocks in one scanline's chain: a first-field block and a second-field block per two pixels.
  /// </summary>
  private const int _CHAIN = GunPaintFile.Width;

  /// <summary>
  /// What an exchange of one block for another costs against what agreeing with the other field is
  /// worth.
  /// </summary>
  /// <remarks>
  /// A pixel can usually be reached by several pairings, and which of them the chain settles on
  /// decides how many different colours a cell ends up wanting — which is the thing the second pass
  /// has to ration. Preferring the pairing where both fields show the same colour costs nothing
  /// where the pairings are equal and is never taken where they are not, the real error being scaled
  /// past it.
  /// </remarks>
  private const int _AGREEMENT = 256;

  /// <summary>The load address a GunPaint picture carries: its screens start at 0x4000.</summary>
  private static ReadOnlySpan<byte> _LoadAddress => [0x00, 0x40];

  /// <summary>Encodes a picture of exactly the size the format shows.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    var palette = Commodore64Graphics.CreatePalette();
    var blend = _BlendTable(palette);

    var chain = new byte[GunPaintFile.Height * _CHAIN];
    var allowed = new int[_CHAIN];
    Array.Fill(allowed, (1 << _COLORS) - 1);

    for (var y = 0; y < GunPaintFile.Height; ++y)
      _SolveRow(rgb, palette, blend, y, allowed, -1, chain.AsSpan(y * _CHAIN, _CHAIN));

    var backgrounds = _ChooseBackgrounds(chain);
    var colorRam = _ChooseColorRam(chain, backgrounds);

    var data = new byte[GunPaintFile.FileSize];
    _LoadAddress.CopyTo(data);

    for (var cellRow = 0; cellRow < CellRows; ++cellRow)
    for (var column = 0; column < CellColumns; ++column)
      data[GunPaintFile.ColorRamOffset + cellRow * GunPaintFile.StrideColumns + column]
        = colorRam[cellRow * CellColumns + column];

    var frees = new byte[CellColumns * 4];

    for (var y = 0; y < GunPaintFile.Height; ++y) {
      data[GunPaintFile.BackgroundOffsetFor(y)] = backgrounds[y];
      _ChooseFreeColors(chain.AsSpan(y * _CHAIN, _CHAIN), backgrounds[y], colorRam, y, frees, allowed);
      _SolveRow(rgb, palette, blend, y, allowed, backgrounds[y], chain.AsSpan(y * _CHAIN, _CHAIN));
      _Emit(data, chain.AsSpan(y * _CHAIN, _CHAIN), backgrounds[y], colorRam, frees, y);
    }

    return data;
  }

  /// <summary>What each pairing of colours looks like once the display has averaged the fields.</summary>
  private static byte[] _BlendTable(ReadOnlySpan<byte> palette) {
    var blend = new byte[_COLORS * _COLORS * 3];
    for (var first = 0; first < _COLORS; ++first)
    for (var second = 0; second < _COLORS; ++second)
    for (var channel = 0; channel < 3; ++channel) {
      int a = palette[first * 3 + channel], b = palette[second * 3 + channel];
      blend[(first * _COLORS + second) * 3 + channel] = (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    }

    return blend;
  }

  /// <summary>
  /// Settles one scanline's blocks, taking each field's from the colours it is allowed.
  /// </summary>
  /// <param name="background">
  /// The scanline's background, which the second field falls back to at the leftmost pixel because
  /// its displacement leaves nothing behind it there, or -1 while that has still to be chosen.
  /// </param>
  private static void _SolveRow(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> blend,
    int y, ReadOnlySpan<int> allowed, int background, Span<byte> chain) {
    var totals = new long[_CHAIN * _COLORS];
    var previous = new byte[_CHAIN * _COLORS];
    var row = y * GunPaintFile.Width * 3;

    for (var color = 0; color < _COLORS; ++color)
      totals[color] = (allowed[0] >> color & 1) == 0
        ? long.MaxValue / 4
        : _AGREEMENT * (long)(background < 0
          ? _Cost(palette, color * 3, rgb, row)
          : _Cost(blend, (color * _COLORS + background) * 3, rgb, row));

    for (var step = 0; step + 1 < _CHAIN; ++step) {
      var pixel = row + (step + 1) * 3;

      for (var next = 0; next < _COLORS; ++next) {
        if ((allowed[step + 1] >> next & 1) == 0) {
          totals[(step + 1) * _COLORS + next] = long.MaxValue / 4;
          continue;
        }

        var best = 0;
        var bestTotal = long.MaxValue;

        for (var current = 0; current < _COLORS; ++current) {
          var total = totals[step * _COLORS + current]
                      + _AGREEMENT * (long)_Cost(blend, (current * _COLORS + next) * 3, rgb, pixel)
                      + (current == next ? 0 : 1);

          if (total >= bestTotal)
            continue;

          bestTotal = total;
          best = current;
        }

        totals[(step + 1) * _COLORS + next] = bestTotal;
        previous[(step + 1) * _COLORS + next] = (byte)best;
      }
    }

    var end = 0;
    for (var color = 1; color < _COLORS; ++color)
      if (totals[(_CHAIN - 1) * _COLORS + color] < totals[(_CHAIN - 1) * _COLORS + end])
        end = color;

    for (var step = _CHAIN - 1; step >= 0; --step) {
      chain[step] = (byte)end;
      end = previous[step * _COLORS + end];
    }
  }

  private static int _Cost(ReadOnlySpan<byte> color, int at, ReadOnlySpan<byte> target, int pixel) {
    int dr = color[at] - target[pixel], dg = color[at + 1] - target[pixel + 1], db = color[at + 2] - target[pixel + 2];

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>
  /// The background a scanline takes: whichever colour it asked for most, since that is the one an
  /// entry spent on it saves the most matrix colours.
  /// </summary>
  /// <remarks>
  /// The last four scanlines share one byte, which is not a rounding of the table but its shape —
  /// most of the screen, then twenty lines from a second place, then a single byte for whatever is
  /// left. They are counted together so that none of them is settled against a background it will
  /// not get.
  /// </remarks>
  private static byte[] _ChooseBackgrounds(ReadOnlySpan<byte> chain) {
    var backgrounds = new byte[GunPaintFile.Height];
    var counts = new int[_COLORS];

    for (var y = 0; y < GunPaintFile.Height; ++y) {
      Array.Clear(counts);
      for (var step = 0; step < _CHAIN; ++step)
        ++counts[chain[y * _CHAIN + step]];

      backgrounds[y] = _Commonest(counts);
    }

    var shared = GunPaintFile.Height - 1;
    while (shared > 0 && GunPaintFile.BackgroundOffsetFor(shared - 1) == GunPaintFile.BackgroundOffsetFor(shared))
      --shared;

    Array.Clear(counts);
    for (var y = shared; y < GunPaintFile.Height; ++y)
    for (var step = 0; step < _CHAIN; ++step)
      ++counts[chain[y * _CHAIN + step]];

    var together = _Commonest(counts);
    for (var y = shared; y < GunPaintFile.Height; ++y)
      backgrounds[y] = together;

    return backgrounds;
  }

  /// <summary>
  /// The colour RAM a character cell takes: the colour its eight scanlines asked for most that the
  /// background is not already giving them.
  /// </summary>
  private static byte[] _ChooseColorRam(ReadOnlySpan<byte> chain, ReadOnlySpan<byte> backgrounds) {
    var colorRam = new byte[CellRows * CellColumns];
    var counts = new int[_COLORS];

    for (var cellRow = 0; cellRow < CellRows; ++cellRow)
    for (var column = 0; column < CellColumns; ++column) {
      Array.Clear(counts);

      for (var line = 0; line < 8; ++line) {
        var y = cellRow * 8 + line;
        for (var step = column * 8; step < column * 8 + 8; ++step) {
          var color = chain[y * _CHAIN + step];
          if (color != backgrounds[y])
            ++counts[color];
        }
      }

      colorRam[cellRow * CellColumns + column] = _Commonest(counts);
    }

    return colorRam;
  }

  /// <summary>
  /// The two colours each field gets to itself in each cell of one scanline, and the mask of what
  /// that leaves the chain to draw from.
  /// </summary>
  /// <remarks>
  /// Taken from what the unconstrained pass asked for, counting only what the background and the
  /// colour RAM are not already supplying — those two reach every cell for nothing, and spending a
  /// matrix colour on either of them would leave a cell with three colours where it could have had
  /// four.
  /// </remarks>
  private static void _ChooseFreeColors(
    ReadOnlySpan<byte> chain, byte background, ReadOnlySpan<byte> colorRam, int y,
    Span<byte> frees, Span<int> allowed) {
    Span<int> counts = stackalloc int[_COLORS];

    for (var column = 0; column < CellColumns; ++column) {
      var shared = colorRam[y / 8 * CellColumns + column];
      var mask = (1 << background) | (1 << shared);

      for (var field = 0; field < 2; ++field) {
        counts.Clear();
        for (var block = 0; block < 4; ++block) {
          var color = chain[column * 8 + block * 2 + field];
          if (color != background && color != shared)
            ++counts[color];
        }

        var first = _Commonest(counts);
        counts[first] = 0;
        var second = _Commonest(counts);

        frees[column * 4 + field * 2] = first;
        frees[column * 4 + field * 2 + 1] = second;

        // A block's place in the chain says which field it belongs to, and the two fields do not
        // share their matrix colours — so what one may draw is not what the other may.
        for (var block = 0; block < 4; ++block)
          allowed[column * 8 + block * 2 + field] = mask | (1 << first) | (1 << second);
      }
    }
  }

  private static byte _Commonest(ReadOnlySpan<int> counts) {
    var best = 0;
    for (var color = 1; color < _COLORS; ++color)
      if (counts[color] > counts[best])
        best = color;

    return (byte)best;
  }

  /// <summary>Writes one scanline's blocks out as bitmap patterns and matrix colours.</summary>
  private static void _Emit(
    Span<byte> data, ReadOnlySpan<byte> chain, byte background, ReadOnlySpan<byte> colorRam,
    ReadOnlySpan<byte> frees, int y) {
    for (var column = 0; column < CellColumns; ++column) {
      var cell = y / 8 * GunPaintFile.StrideColumns + column;
      var shared = colorRam[y / 8 * CellColumns + column];

      for (var field = 0; field < 2; ++field) {
        var first = frees[column * 4 + field * 2];
        var second = frees[column * 4 + field * 2 + 1];
        var bitmap = field == 0 ? GunPaintFile.FirstBitmapOffset : GunPaintFile.SecondBitmapOffset;
        var matrix = field == 0 ? GunPaintFile.FirstMatrixOffset : GunPaintFile.SecondMatrixOffset;

        data[matrix + (y & 7) * GunPaintFile.MatrixStride + cell] = (byte)((first << 4) | second);

        var pattern = 0;
        for (var block = 0; block < 4; ++block) {
          var color = chain[column * 8 + block * 2 + field];
          var bits = color == background ? 0 : color == shared ? 3 : color == first ? 1 : 2;
          pattern |= bits << (6 - block * 2);
        }

        data[bitmap + (cell << 3) + (y & 7)] = (byte)pattern;
      }
    }
  }
}
