using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.FunPainter;

/// <summary>Settles a picture into the two displaced FLI screens a Fun Painter file holds.</summary>
/// <remarks>
/// The second field sits one screen pixel to the right of the first, and a multicolour pixel is two
/// screen pixels wide, so the two fields line up on odd columns and straddle each other on even
/// ones. An odd column therefore shows one pixel of each field blended, and an even column shows a
/// pixel of the first field blended with the *previous* pixel of the second. A scanline is a chain.
/// <para/>
/// That chain is what makes an exact encoder possible at all. Averaging two of the sixteen colours
/// gives 135 distinct results from 136 unordered pairs — only <c>#6C6C6C</c> is ambiguous, being
/// both the two greys mixed and mid grey with itself — so a blended colour names the pair that made
/// it. Reading the odd columns recovers each field's pixels up to their order, the even columns
/// settle the order, and a forward and backward sweep over the row leaves only readings that work
/// end to end. Where every pixel of a picture is a blend the machine can make, the fields come back
/// exactly, which is what makes reading a file and writing it again lossless.
/// <para/>
/// What the chain cannot decide is whether a character cell can afford what it asks for. A field
/// row of four pixels may show black, the cell's colour memory, and two colours of its own; colour
/// memory is one entry for all eight rows of the cell <em>and</em> for both fields, so it is chosen
/// by trying all sixteen. A picture that fails that test is refused rather than approximated.
/// <para/>
/// A picture that is not a blend of two C64 screens in the first place — a photograph, say — cannot
/// be encoded exactly by anything, and <see cref="Encode"/> falls back to drawing the same FLI
/// screen in both fields. That reproduces the odd columns exactly and averages neighbours on the
/// even ones, which is the best a single screen can do.
/// </remarks>
public static class FunPainterEncoder {

  private const int _COLORS = Commodore64Graphics.ColorCount;
  private const int _PAIRS = _COLORS * _COLORS;

  /// <summary>Multicolour pixels across the displayed picture.</summary>
  private const int _CHAIN = FunPainterFile.Width / 2;

  /// <summary>Multicolour pixels across a stored row, which is wider than the one shown.</summary>
  private const int _STORED = FunPainterFile.StrideColumns * 4;

  private const int _CELL_ROWS = FunPainterFile.Height / Commodore64Graphics.CellHeight;
  private const int _BITMAP_SIZE = 8000;
  private const int _MATRIX_SIZE = FunPainterFile.MatrixStride * Commodore64Graphics.CellHeight;
  private const int _COLOR_RAM_SIZE = 1000;

  /// <summary>Encodes a picture, exactly where the format can hold it and approximately otherwise.</summary>
  /// <param name="rgb">A <see cref="FunPainterFile.Width"/> by <see cref="FunPainterFile.Height"/> picture, three bytes a pixel.</param>
  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    _RequireFullPicture(rgb);

    if (_TrySolveExact(rgb, out var first, out var second) && _TryFitCells(first, second, out var data))
      return data;

    return _Approximate(rgb);
  }

  /// <summary>Encodes a picture the format can hold exactly, and refuses one it cannot.</summary>
  /// <exception cref="ArgumentException">The picture names something the two fields cannot show.</exception>
  public static byte[] EncodeExact(ReadOnlySpan<byte> rgb) {
    _RequireFullPicture(rgb);
    _SolveExact(rgb, out var first, out var second);

    return _FitCells(first, second);
  }

  private static void _RequireFullPicture(ReadOnlySpan<byte> rgb) {
    var needed = FunPainterFile.Width * FunPainterFile.Height * 3;
    if (rgb.Length < needed)
      throw new ArgumentException(
        $"A Fun Painter picture is {FunPainterFile.Width}x{FunPainterFile.Height} in Rgb24, "
        + $"so {needed} bytes; got {rgb.Length}.", nameof(rgb));
  }

  #region reading the blend back apart

  /// <summary>What every ordered pair of field colours averages to, and which pairs make a colour.</summary>
  /// <remarks>
  /// The reverse direction is what the solver leans on. Two hundred and fifty-six ordered pairs give
  /// 135 distinct colours, so a pair list is almost always one entry and never more than four —
  /// which is why reading a picture back apart is a search over a handful of readings rather than
  /// over the sixteen colours squared.
  /// </remarks>
  private sealed class _Blend {

    /// <summary>The colour an ordered pair averages to, as 0xRRGGBB.</summary>
    public readonly int[] Table = new int[_PAIRS];

    private readonly Dictionary<int, int[]> _pairsByColor;

    public _Blend() {
      for (var a = 0; a < _COLORS; ++a)
      for (var b = 0; b < _COLORS; ++b) {
        int first = Commodore64Graphics.HexColors[a], second = Commodore64Graphics.HexColors[b];
        this.Table[a * _COLORS + b] = (first & second) + (((first ^ second) >> 1) & 0x7F7F7F);
      }

      var grouped = new Dictionary<int, List<int>>();
      for (var pair = 0; pair < _PAIRS; ++pair) {
        if (!grouped.TryGetValue(this.Table[pair], out var pairs))
          grouped[this.Table[pair]] = pairs = [];

        pairs.Add(pair);
      }

      this._pairsByColor = new(grouped.Count);
      foreach (var (color, pairs) in grouped)
        this._pairsByColor[color] = [.. pairs];
    }

    /// <summary>Every ordered pair of field colours that averages to this one.</summary>
    public int[] PairsFor(int color) => this._pairsByColor.TryGetValue(color, out var pairs) ? pairs : [];
  }

  private static bool _TrySolveExact(ReadOnlySpan<byte> rgb, out byte[] first, out byte[] second) {
    try {
      _SolveExact(rgb, out first, out second);
      return true;
    } catch (ArgumentException) {
      first = second = [];
      return false;
    }
  }

  /// <summary>Recovers both fields' pixels from the blended picture, row by row.</summary>
  private static void _SolveExact(ReadOnlySpan<byte> rgb, out byte[] first, out byte[] second) {
    if (rgb.Length < FunPainterFile.Width * FunPainterFile.Height * 3)
      throw new ArgumentException($"A Fun Painter picture is {FunPainterFile.Width}x{FunPainterFile.Height}.", nameof(rgb));

    var blend = new _Blend();
    first = new byte[FunPainterFile.Height * _CHAIN];
    second = new byte[FunPainterFile.Height * _CHAIN];

    var row = new int[FunPainterFile.Width];
    var reach = new bool[_CHAIN * _PAIRS];

    for (var y = 0; y < FunPainterFile.Height; ++y) {
      for (var x = 0; x < FunPainterFile.Width; ++x) {
        var at = (y * FunPainterFile.Width + x) * 3;
        row[x] = (rgb[at] << 16) | (rgb[at + 1] << 8) | rgb[at + 2];
      }

      _SolveRow(row, blend, reach, y,
        first.AsSpan(y * _CHAIN, _CHAIN), second.AsSpan(y * _CHAIN, _CHAIN));
    }
  }

  /// <summary>
  /// Settles one scanline: which pixel of each field stands behind every column of the picture.
  /// </summary>
  private static void _SolveRow(
    ReadOnlySpan<int> row, _Blend blend, bool[] reach, int y, Span<byte> first, Span<byte> second) {
    Array.Clear(reach);
    var table = blend.Table;

    // The odd column shows one pixel of each field, so it names the pair outright. The leftmost
    // column is the exception: the displaced field has nothing there, so it blends against black.
    var survivors = 0;
    foreach (var pair in blend.PairsFor(row[1]))
      if (table[pair / _COLORS * _COLORS] == row[0]) {
        reach[pair] = true;
        ++survivors;
      }

    _Require(survivors, row[1], 1, y);

    Span<bool> reachable = stackalloc bool[_COLORS];
    for (var k = 1; k < _CHAIN; ++k) {
      // Which colours the previous column leaves available to the displaced field.
      reachable.Clear();
      foreach (var pair in blend.PairsFor(row[2 * k - 1]))
        if (reach[(k - 1) * _PAIRS + pair])
          reachable[pair % _COLORS] = true;

      var want = row[2 * k];
      survivors = 0;
      foreach (var pair in blend.PairsFor(row[2 * k + 1])) {
        // An even column blends this field's pixel with the previous one of the displaced field.
        var leading = pair / _COLORS * _COLORS;
        for (var trailing = 0; trailing < _COLORS; ++trailing)
          if (reachable[trailing] && table[leading + trailing] == want) {
            reach[k * _PAIRS + pair] = true;
            ++survivors;
            break;
          }
      }

      _Require(survivors, want, 2 * k, y);
    }

    // Backwards, dropping readings that cannot be carried on to the end of the row.
    for (var k = _CHAIN - 2; k >= 0; --k) {
      reachable.Clear();
      foreach (var pair in blend.PairsFor(row[2 * k + 3]))
        if (reach[(k + 1) * _PAIRS + pair])
          reachable[pair / _COLORS] = true;

      var want = row[2 * (k + 1)];
      survivors = 0;
      foreach (var pair in blend.PairsFor(row[2 * k + 1])) {
        if (!reach[k * _PAIRS + pair])
          continue;

        var trailing = pair % _COLORS;
        var keep = false;
        for (var leading = 0; leading < _COLORS && !keep; ++leading)
          keep = reachable[leading] && table[leading * _COLORS + trailing] == want;

        reach[k * _PAIRS + pair] = keep;
        if (keep)
          ++survivors;
      }

      _Require(survivors, want, 2 * (k + 1), y);
    }

    // Every surviving reading now carries on to the end, so the first one found does.
    var previous = -1;
    for (var k = 0; k < _CHAIN; ++k) {
      var chosen = -1;
      foreach (var pair in blend.PairsFor(row[2 * k + 1])) {
        if (!reach[k * _PAIRS + pair])
          continue;
        if (k > 0 && table[pair / _COLORS * _COLORS + previous] != row[2 * k])
          continue;

        chosen = pair;
        break;
      }

      _Require(chosen < 0 ? 0 : 1, row[2 * k + 1], 2 * k + 1, y);
      first[k] = (byte)(chosen / _COLORS);
      second[k] = (byte)(chosen % _COLORS);
      previous = second[k];
    }
  }

  private static void _Require(int survivors, int color, int x, int y) {
    if (survivors > 0)
      return;

    throw new ArgumentException(
      $"Fun Painter cannot show #{color:X6} at {x},{y}: no pair of the sixteen Commodore 64 colours "
      + "averages to it beside what its neighbours need.", "rgb");
  }

  #endregion

  #region fitting the character cells

  private static bool _TryFitCells(byte[] first, byte[] second, out byte[] data) {
    try {
      data = _FitCells(first, second);
      return true;
    } catch (ArgumentException) {
      data = [];
      return false;
    }
  }

  /// <summary>Lays both fields out as bitmaps, video matrices and the colour memory they share.</summary>
  private static byte[] _FitCells(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var bitmaps = new byte[2][] { new byte[_BITMAP_SIZE], new byte[_BITMAP_SIZE] };
    var matrices = new byte[2][] { new byte[_MATRIX_SIZE], new byte[_MATRIX_SIZE] };
    var colorRam = new byte[_COLOR_RAM_SIZE];

    Span<byte> pixels = stackalloc byte[4];
    for (var cellRow = 0; cellRow < _CELL_ROWS; ++cellRow)
    for (var column = 0; column < FunPainterFile.VisibleColumns; ++column) {
      var entry = _ChooseColorRam(first, second, cellRow, column);
      if (entry < 0)
        throw new ArgumentException(
          $"Fun Painter cannot hold the character cell at column {column}, row {cellRow}: its "
          + "scanlines need more colours than one colour memory entry and two a line can name.", "rgb");

      var cell = cellRow * FunPainterFile.StrideColumns + column;
      colorRam[cell] = (byte)entry;

      for (var field = 0; field < 2; ++field) {
        var source = field == 0 ? first : second;
        for (var line = 0; line < Commodore64Graphics.CellHeight; ++line) {
          _PixelsOf(source, cellRow, column, line, pixels);
          var (_, leading, trailing) = _SpareColors(pixels, entry);

          var bits = 0;
          for (var i = 0; i < 4; ++i) {
            int pixel = pixels[i];
            var pattern = pixel == 0 ? 0
              : pixel == leading ? 1
              : pixel == trailing ? 2
              : 3;
            bits |= pattern << ((3 - i) * 2);
          }

          bitmaps[field][cell * Commodore64Graphics.CellHeight + line] = (byte)bits;
          matrices[field][line * FunPainterFile.MatrixStride + cell] = (byte)((leading << 4) | trailing);
        }
      }
    }

    return _Assemble(bitmaps[0], matrices[0], colorRam, bitmaps[1], matrices[1]);
  }

  private static void _PixelsOf(
    ReadOnlySpan<byte> field, int cellRow, int column, int line, Span<byte> pixels) {
    var y = cellRow * Commodore64Graphics.CellHeight + line;
    for (var i = 0; i < 4; ++i)
      pixels[i] = field[y * _CHAIN + column * 4 + i];
  }

  /// <summary>
  /// The colour memory entry that lets every line of the cell, in both fields, name what it shows;
  /// -1 where no entry does.
  /// </summary>
  private static int _ChooseColorRam(
    ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, int cellRow, int column) {
    Span<byte> pixels = stackalloc byte[4];

    for (var entry = 0; entry < _COLORS; ++entry) {
      var fits = true;
      for (var field = 0; field < 2 && fits; ++field) {
        var source = field == 0 ? first : second;
        for (var line = 0; line < Commodore64Graphics.CellHeight && fits; ++line) {
          _PixelsOf(source, cellRow, column, line, pixels);
          // Black and the colour memory entry are free; a line may name two colours besides.
          fits = _SpareColors(pixels, entry).Count <= 2;
        }
      }

      if (fits)
        return entry;
    }

    return -1;
  }

  /// <summary>The colours a line needs its own video matrix entry for, black-padded.</summary>
  /// <remarks>
  /// Black and the colour memory entry cost a line nothing, so only what is left has to be named,
  /// and the video matrix has room for two. <c>Count</c> is how many the line actually asks for; a
  /// line asking for more than two cannot be drawn, and the caller decides what to do about it.
  /// </remarks>
  private static (int Count, int Leading, int Trailing) _SpareColors(ReadOnlySpan<byte> pixels, int entry) {
    int leading = 0, trailing = 0, count = 0;
    foreach (var pixel in pixels) {
      if (pixel == 0 || pixel == entry)
        continue;
      if (count > 0 && pixel == leading)
        continue;
      if (count > 1 && pixel == trailing)
        continue;

      if (count == 0)
        leading = pixel;
      else if (count == 1)
        trailing = pixel;

      ++count;
    }

    return (count, leading, trailing);
  }

  #endregion

  #region the approximation

  /// <summary>
  /// Draws one multicolour FLI screen in both fields, which is what a picture the machine cannot
  /// hold gets instead of a refusal.
  /// </summary>
  /// <remarks>
  /// The odd columns of the picture show a field pixel by itself, so that is where the target is
  /// sampled; the even columns then average neighbouring pixels, which reads as a slight horizontal
  /// softening rather than as an error. The stored row is wider than the shown one, so the sample is
  /// padded with black out to the full forty character cells before the shared encoder sees it.
  /// </remarks>
  private static byte[] _Approximate(ReadOnlySpan<byte> rgb) {
    var target = new byte[_STORED * FunPainterFile.Height * 3];
    for (var y = 0; y < FunPainterFile.Height; ++y)
    for (var k = 0; k < _CHAIN; ++k) {
      var from = (y * FunPainterFile.Width + Math.Min(2 * k + 1, FunPainterFile.Width - 1)) * 3;
      var to = (y * _STORED + k) * 3;
      target[to] = rgb[from];
      target[to + 1] = rgb[from + 1];
      target[to + 2] = rgb[from + 2];
    }

    var bitmap = new byte[_BITMAP_SIZE];
    var matrix = new byte[_MATRIX_SIZE];
    var colorRam = new byte[_COLOR_RAM_SIZE];
    Commodore64Graphics.EncodeMulticolorFli(
      target, _STORED, FunPainterFile.Height, 0,
      bitmap, matrix, FunPainterFile.MatrixStride, colorRam);

    return _Assemble(bitmap, matrix, colorRam, bitmap, matrix);
  }

  #endregion

  /// <summary>The load address a Fun Painter picture carries: its screens start at 0x4000.</summary>
  private static ReadOnlySpan<byte> _LoadAddress => [0x00, 0x40];

  private static byte[] _Assemble(
    ReadOnlySpan<byte> firstBitmap, ReadOnlySpan<byte> firstMatrix, ReadOnlySpan<byte> colorRam,
    ReadOnlySpan<byte> secondBitmap, ReadOnlySpan<byte> secondMatrix) {
    var data = new byte[FunPainterFile.FileSize];
    _LoadAddress.CopyTo(data);
    for (var i = 0; i < FunPainterFile.Signature.Length; ++i)
      data[FunPainterFile.SignatureOffset + i] = (byte)FunPainterFile.Signature[i];

    firstMatrix.CopyTo(data.AsSpan(FunPainterFile.FirstMatrixOffset));
    firstBitmap.CopyTo(data.AsSpan(FunPainterFile.FirstBitmapOffset));
    colorRam.CopyTo(data.AsSpan(FunPainterFile.ColorRamOffset));
    secondMatrix.CopyTo(data.AsSpan(FunPainterFile.SecondMatrixOffset));
    secondBitmap.CopyTo(data.AsSpan(FunPainterFile.SecondBitmapOffset));

    return data;
  }
}
