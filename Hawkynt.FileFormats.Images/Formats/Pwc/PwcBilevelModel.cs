using System;
using System.IO;

namespace FileFormat.Pwc;

/// <summary>
/// The depth-one PWC model: every pixel is a decision under a ten-pixel template, and a neighbourhood that is all
/// one colour first asks whether the whole run up to the next change in the two rows above can be skipped.
/// </summary>
/// <remarks>
/// <para>
/// The template is the two previous pixels of the row, five pixels of the row above (two left to two right) and
/// three of the row above that (one left to one right). Context bits, low to high: left, left-left, above+2,
/// above+1, above, above-1, above-2, above-above+1, above-above, above-above-1.
/// </para>
/// <para>
/// Both history rows live in buffers four pixels longer than the picture; the row above is overwritten in place by
/// the row being coded, and a one is planted two pixels past its end so that a white skip scan stops there.
/// </para>
/// </remarks>
internal sealed class PwcBilevelModel {

  private const int _SKIP_HIT = 0x400;
  private const int _SKIP_LENGTH = 0x420;

  private readonly PwcArithmeticCoder _coder;
  private readonly int _width;
  private readonly byte[] _above;
  private readonly byte[] _aboveAbove;
  private readonly PwcStatistic[] _statistics = new PwcStatistic[0x481];
  private bool _wholeRowFollows = true;

  public PwcBilevelModel(PwcArithmeticCoder coder, int width) {
    this._coder = coder;
    this._width = width;
    this._above = new byte[width + 4];
    this._aboveAbove = new byte[width + 4];
    this._above[width + 2] = 1;
    Array.Fill(this._statistics, PwcStatistic.Fresh);
  }

  /// <summary>Codes one row of 0/1 values: decodes into <paramref name="row"/>, or encodes what it holds.</summary>
  public void CodeRow(Span<byte> row) {
    var above = this._above;
    var aboveAbove = this._aboveAbove;
    var width = this._width;
    var encoding = this._coder.IsEncoding;

    var context = (aboveAbove[0] << 8) | (aboveAbove[1] << 7) | (above[0] << 4) | (above[1] << 3) | (above[2] << 2);
    var skipLimit = this._wholeRowFollows ? width : 0;
    this._wholeRowFollows = false;

    var x = 0;
    while (x < width) {
      if (context == 0 || context == 0x3FF) {
        this._Skip(row, ref x, ref context, ref skipLimit, context & 1);
        continue;
      }

      var bit = this._coder.Adaptive(ref this._statistics[context], encoding ? row[x] : 0);
      row[x] = (byte)bit;
      context = this._Shift(context, x, bit);
      ++x;
    }
  }

  /// <summary>Moves the template one pixel right after pixel <paramref name="x"/> was coded as <paramref name="bit"/>.</summary>
  private int _Shift(int context, int x, int bit) {
    var shifted = (this._aboveAbove[x + 2] << 7) | (this._above[x + 3] << 2) | ((context & 0x1BD) << 1) | bit;
    this._aboveAbove[x] = this._above[x];
    this._above[x] = (byte)bit;
    return shifted;
  }

  /// <summary>Rebuilds the template at <paramref name="x"/> after a jump through pixels equal to the old neighbourhood.</summary>
  private int _Resume(int context, int x) =>
    (this._aboveAbove[x + 1] << 7) | (this._above[x + 2] << 2) | ((context & 0x1BC) << 1) | (context & 3);

  private void _Skip(Span<byte> row, ref int x, ref int context, ref int skipLimit, int colour) {
    var above = this._above;
    var aboveAbove = this._aboveAbove;
    int length;
    if (x < skipLimit)
      length = skipLimit - x;
    else {
      var run = 0;
      while (above[x + 3 + run] == colour && aboveAbove[x + 2 + run] == colour)
        ++run;

      skipLimit = x + 1 + run;
      length = run + 1;
    }

    var bits = PwcBits.Length(length - 1);
    var encoding = this._coder.IsEncoding;
    var same = 0;
    if (encoding)
      while (same < length && row[x + same] == colour)
        ++same;

    var hit = this._coder.Adaptive(ref this._statistics[_SKIP_HIT + 16 * colour + bits], same == length ? 1 : 0);
    if (hit != 0) {
      row.Slice(x, length).Fill((byte)colour);
      x += length;
      if (length == this._width)
        this._wholeRowFollows = true;
      else
        context = this._Resume(context, x);

      return;
    }

    // The run broke early: its length follows MSB first, with a separate context set once a one was seen.
    var lengthBase = _SKIP_LENGTH + 32 * colour;
    var run2 = 0;
    var seen = 0;
    for (var bit = bits - 1; bit >= 0; --bit) {
      var step = 1 << bit;
      if (run2 + step > length)
        continue;

      if (this._coder.Adaptive(ref this._statistics[lengthBase + seen + bit], (same & step) != 0 ? 1 : 0) == 0)
        continue;

      run2 += step;
      seen = 16;
    }

    // An encoder only codes a break inside the run; a length reaching its end means the bits are not PWC's.
    if (run2 >= length)
      throw new InvalidDataException("PWC run length reaches past the run it breaks.");

    row.Slice(x, run2).Fill((byte)colour);
    x += run2;
    context = this._Resume(context, x);

    var other = colour ^ 1;
    row[x] = (byte)other;
    context = this._Shift(context, x, other);
    ++x;
  }
}
