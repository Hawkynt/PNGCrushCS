using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pwc;

/// <summary>
/// The piecewise-constant model for 4- and 8-bit palette pictures: each row is split into stripes by edge
/// decisions, and each stripe whose colour cannot be carried down from the row above has its colour found by
/// guesses and, failing those, coded outright.
/// </summary>
/// <remarks>
/// <para>
/// Every pixel owns an edge byte: bit 0 says it differs from its left neighbour, bit 1 that it differs from the
/// pixel above. The edge row and the colour row are both rewritten in place, so ahead of the cursor they still
/// describe the row above. The edge context of a pixel is the left pixel's edge byte plus the three edge bytes of
/// the row above at columns -1, 0 and +1 (two bits each, low to high).
/// </para>
/// <para>
/// A stripe's colour is known as soon as a "no vertical edge" decision ties one of its pixels to the colour above.
/// Colours proven different from the stripe — the closing colour of the previous stripe, colours above a vertical
/// edge, failed guesses — are stamped impossible and never asked about again within that stripe.
/// </para>
/// </remarks>
internal sealed class PwcRegionModel {

  private const int _ORIGIN = 4;
  private const int _VERTICAL = 0x100;
  private const int _SKIP_HIT = 0x301;
  private const int _SKIP_LENGTH = 0x311;

  private readonly PwcArithmeticCoder _coder;
  private readonly PwcSymbolCoder _symbols;
  private readonly int _width;
  private readonly bool _deep;
  private readonly PwcCodingFlags _flags;
  private readonly byte[] _colors;
  private readonly byte[] _edges;
  private readonly PwcStatistic[] _edgeStatistics = new PwcStatistic[0x331];
  private readonly PwcStatistic[] _cornerStatistics = new PwcStatistic[0x200];
  private readonly PwcStatistic[] _listStatistics = new PwcStatistic[0x100];
  private readonly int[] _impossible;
  private readonly PwcGuessPool _guesses;
  private int _stamp = 1;
  private bool _wholeRowFollows = true;
  private bool _firstRow = true;

  // Per-row cursor state. The edge cursor and the colour cursor move together except after a whole-row skip.
  private byte[] _row = [];
  private int _edge;
  private int _column;
  private int _stripeStart;
  private int _corner;
  private int _context;
  private int _skipLimit;

  public PwcRegionModel(PwcArithmeticCoder coder, PwcSymbolCoder symbols, int width, int depth, PwcCodingFlags flags) {
    this._coder = coder;
    this._symbols = symbols;
    this._width = width;
    this._deep = depth == 8;
    this._flags = flags;
    this._colors = new byte[width + 8];
    this._edges = new byte[width + 8];
    this._edges[_ORIGIN + width + 1] = 3;
    this._impossible = new int[1 << depth];
    this._guesses = this._deep ? new PwcGuessPool(0x100, 0x400) : new PwcGuessPool(0x1000, 0x100);
    Array.Fill(this._edgeStatistics, PwcStatistic.Fresh);
    Array.Fill(this._cornerStatistics, PwcStatistic.Fresh);
    Array.Fill(this._listStatistics, PwcStatistic.Fresh);
  }

  /// <summary>Codes one row of colour indices: decodes into <paramref name="row"/>, or encodes what it holds.</summary>
  public void CodeRow(byte[] row) {
    this._row = row;
    if ((this._flags & PwcCodingFlags.Gray) != 0)
      this._CodeGrayRow();
    else if (this._firstRow)
      this._CodeFirstRow();
    else
      this._CodeRow();

    this._firstRow = false;
    Array.Copy(this._colors, _ORIGIN, row, 0, this._width);
  }

  private bool Encoding => this._coder.IsEncoding;

  private int _Color(int column) => this._colors[_ORIGIN + column];

  private ref byte _EdgeAt(int column) => ref this._edges[_ORIGIN + column];

  private int _Decide(ref PwcStatistic statistic, bool bitIfEncoding) => this._coder.Adaptive(ref statistic, bitIfEncoding ? 1 : 0);

  private bool _IsPossible(int color) => this._impossible[color] != this._stamp;

  private void _Rule(int color) => this._impossible[color] = this._stamp;

  /// <summary>Starts a row: resets the cursors and codes the first pixel's vertical edge.</summary>
  /// <returns>The colour carried down into the first stripe, or -1 when unknown.</returns>
  private int _StartRow(bool firstRow) {
    this._edge = 0;
    this._context = (this._EdgeAt(0) << 4) | (this._EdgeAt(1) << 6);
    this._skipLimit = this._wholeRowFollows ? this._width : 0;
    this._wholeRowFollows = false;

    this._column = 0;
    this._colors[_ORIGIN - 1] = this._colors[_ORIGIN];
    this._corner = 0;
    this._stripeStart = 0;
    this._colors[_ORIGIN + this._width] = this._colors[_ORIGIN + this._width - 1];
    ++this._stamp;

    this._EdgeAt(0) = 0;
    var carried = -1;
    if (!firstRow) {
      var above = this._Color(0);
      var vertical = this._Decide(ref this._edgeStatistics[_VERTICAL + this._context], this.Encoding && above != this._row[0]);
      this._EdgeAt(0) |= (byte)(vertical << 1);
      if (vertical == 0)
        carried = above;
      else
        this._Rule(above);
    }

    this._Advance();
    return carried;
  }

  private void _Advance() {
    var edge = this._edge;
    this._context = (this._EdgeAt(edge + 2) << 6) | ((this._context >> 2) & 0x3C) | this._EdgeAt(edge);
    ++this._edge;
    ++this._column;
  }

  private bool _SameAsLeft(int column) => this._row[column] == this._row[column - 1];

  private void _CodeFirstRow() {
    this._StartRow(true);
    var width = this._width;
    var bits = width > 1 ? PwcBits.Length(width - 2) : 0;
    var uniform = true;
    if (this.Encoding)
      for (var x = 1; x < width && uniform; ++x)
        uniform = this._SameAsLeft(x);

    if (this._Decide(ref this._edgeStatistics[_SKIP_HIT + bits], uniform) != 0) {
      this._wholeRowFollows = true;
      this._column += width - 1;
      this._CloseStripe(-1);
      return;
    }

    while (this._column < width) {
      var edge = this._Decide(ref this._edgeStatistics[this._context], this.Encoding && !this._SameAsLeft(this._column));
      this._EdgeAt(this._edge) = (byte)edge;
      if (edge != 0)
        this._CloseStripe(-1);

      this._Advance();
    }

    this._CloseStripe(-1);
  }

  private void _CodeRow() {
    var carried = this._StartRow(false);
    while (this._column < this._width) {
      var context = this._context;
      if (context == 0) {
        this._Skip(ref carried);
        this._Advance();
        continue;
      }

      var column = this._column;
      var fork = context & 0x12;
      var horizontal = this._Decide(ref this._edgeStatistics[context], this.Encoding && !this._SameAsLeft(column));
      ref var edge = ref this._EdgeAt(this._edge);
      edge = (byte)horizontal;

      if (horizontal == 0) {
        // Same as the left pixel: the vertical edge is implied unless left and above both differ from the corner.
        if (fork == 0x12) {
          var above = this._Color(column);
          if (!this._IsPossible(above))
            edge |= 2;
          else if (above != carried)
            this._CodeVertical(ref carried, above);
        } else if (fork != 0)
          edge |= 2;
      } else {
        this._CloseStripe(carried);
        carried = -1;
        var above = this._Color(column);
        if (fork != 0 && this._IsPossible(above))
          this._CodeVertical(ref carried, above);
        else
          edge |= 2;
      }

      this._Advance();
    }

    this._CloseStripe(carried);
  }

  private void _CodeVertical(ref int carried, int above) {
    ref var edge = ref this._EdgeAt(this._edge);
    var vertical = this._Decide(ref this._edgeStatistics[(edge + 1) * _VERTICAL + this._context], this.Encoding && above != this._row[this._column]);
    edge |= (byte)(vertical << 1);
    if (vertical == 0)
      carried = above;
    else
      this._Rule(above);
  }

  /// <summary>
  /// At a pixel with no edges around it, asks whether the row runs on unchanged up to the next edge of the row
  /// above; if not, codes how far it does run and starts a new stripe where it breaks.
  /// </summary>
  private void _Skip(ref int carried) {
    var edge = this._edge;
    int length;
    if (edge < this._skipLimit)
      length = this._skipLimit - edge;
    else {
      var next = edge + 2;
      while (this._EdgeAt(next) == 0)
        ++next;

      length = next - 1 - edge;
    }

    var bits = PwcBits.Length(length - 1);
    var column = this._column;
    var same = 0;
    if (this.Encoding)
      while (same < length && this._SameAsLeft(column + same))
        ++same;

    if (this._Decide(ref this._edgeStatistics[_SKIP_HIT + bits], same == length) != 0) {
      if (length - 1 == this._width - 2)
        this._wholeRowFollows = true;
      else
        this._edge += length - 1;

      this._FillStripe(carried);
      this._corner = this._Color(column);
      this._column += length - 1;
      this._stripeStart = this._column;
      return;
    }

    var run = 0;
    var seen = 0;
    for (var bit = bits - 1; bit >= 0; --bit) {
      var step = 1 << bit;
      if (run + step > length)
        continue;

      if (this._Decide(ref this._edgeStatistics[_SKIP_LENGTH + seen + bit], (same & step) != 0) == 0)
        continue;

      run += step;
      seen = 16;
    }

    if (run >= length)
      throw new InvalidDataException("PWC run length reaches past the run it breaks.");

    this._edge += run;
    this._EdgeAt(this._edge) = 3;
    var closed = carried;
    this._FillStripe(closed);
    this._corner = this._Color(column);
    carried = -1;
    this._column += run;
    this._stripeStart = this._column;
    ++this._stamp;
    this._Rule(closed);
  }

  private void _FillStripe(int color) {
    for (var x = this._stripeStart; x < this._column; ++x)
      this._colors[_ORIGIN + x] = (byte)color;
  }

  /// <summary>Ends the stripe before the cursor, finding its colour first when it is still unknown.</summary>
  private void _CloseStripe(int color) {
    if (color < 0)
      color = this._FindColor(this._column - this._stripeStart);

    this._corner = this._Color(this._column - 1);
    this._FillStripe(color);
    this._stripeStart = this._column;
    ++this._stamp;
    this._Rule(color);
  }

  private int _FindColor(int length) {
    if (!this._deep)
      return this._FindShallowColor();

    if ((this._flags & PwcCodingFlags.NoCornerGuesses) == 0) {
      var corner = this._CornerColor(length);
      if (corner >= 0)
        return corner;
    }

    var start = this._stripeStart;
    var target = this.Encoding ? this._row[start] : -1;
    var left = this._Color(start - 1);
    var bucket = this._guesses.Bucket(left);
    if (bucket.Count > 0) {
      var listed = (this._flags & PwcCodingFlags.NoListDecision) == 0;
      var inList = true;
      if (listed)
        inList = this._Decide(ref this._listStatistics[left], this.Encoding && this._guesses.Contains(left, target)) != 0;

      if (inList) {
        var found = -1;
        var last = listed ? bucket.Count - 1 : bucket.Count;
        for (var i = 0; i < last && found < 0; ++i) {
          var entry = bucket[i];
          var color = this._guesses.ColorOf(entry);
          if (this._IsPossible(color) && this._Decide(ref this._guesses.StatisticOf(entry), color == target) != 0)
            found = entry;
        }

        if (found < 0 && listed)
          found = bucket[^1];

        if (found >= 0) {
          this._guesses.Promote(found);
          return this._guesses.ColorOf(found);
        }
      }
    }

    var coded = this._PredictColor(start);
    this._guesses.Allocate(left, coded);
    return coded;
  }

  private int _FindShallowColor() {
    var start = this._stripeStart;
    var target = this.Encoding ? this._row[start] : -1;
    var key = (this._Color(start) << 8) | (this._corner << 4) | this._Color(start - 1);
    foreach (var entry in this._guesses.Bucket(key)) {
      var color = this._guesses.ColorOf(entry);
      if (this._IsPossible(color) && this._Decide(ref this._guesses.StatisticOf(entry), color == target) != 0) {
        this._guesses.Promote(entry);
        return color;
      }
    }

    var coded = this._symbols.Code(this._coder, target, 0, int.MaxValue);
    if (coded >= this._impossible.Length)
      throw new InvalidDataException($"PWC 4-bit picture decodes colour {coded}, which no 4-bit pixel can hold.");

    this._guesses.Allocate(key, coded);
    return coded;
  }

  /// <summary>Asks about the colour above-left of the stripe, then the one above-right of its end.</summary>
  private int _CornerColor(int length) {
    var target = this.Encoding ? this._row[this._stripeStart] : -1;
    foreach (var candidate in (ReadOnlySpan<int>)[this._corner, this._Color(this._stripeStart + length)]) {
      if (!this._IsPossible(candidate))
        continue;

      if (this._Decide(ref this._cornerStatistics[candidate], candidate == target) != 0)
        return candidate;

      this._Rule(candidate);
    }

    return -1;
  }

  /// <summary>Codes a colour against the median edge detector of left, above and corner, its activity the spread of those and above-right.</summary>
  private int _PredictColor(int column) {
    int left = this._Color(column - 1), above = this._Color(column), corner = this._corner, aboveRight = this._Color(column + 1);
    var low = Math.Min(left, above);
    var high = Math.Max(left, above);
    int prediction;
    if (corner <= low) {
      prediction = high;
      low = corner;
    } else if (corner >= high) {
      prediction = low;
      high = corner;
    } else
      prediction = (byte)(left + above - corner);

    low = Math.Min(low, aboveRight);
    high = Math.Max(high, aboveRight);
    var target = this.Encoding ? this._row[column] : 0;
    return this._symbols.Code(this._coder, target, prediction, PwcBits.Length(high - low));
  }

  private void _CodeGrayRow() {
    this._colors[_ORIGIN - 1] = this._colors[_ORIGIN];
    this._colors[_ORIGIN + this._width] = this._colors[_ORIGIN + this._width - 1];
    this._corner = 0;
    for (var x = 0; x < this._width; ++x) {
      // The 4-bit coder has no grey predictor: its grey mode codes nothing and yields index zero throughout.
      var color = this._deep ? this._PredictColor(x) : 0;
      this._corner = this._Color(x);
      this._colors[_ORIGIN + x] = (byte)color;
    }
  }
}

/// <summary>
/// The guess cache: a fixed pool of (key, colour) entries, each with its own decision statistic, filed into
/// per-key lists ordered most recently confirmed first and recycled least recently used.
/// </summary>
internal sealed class PwcGuessPool {

  private readonly List<int>[] _buckets;
  private readonly int[] _key;
  private readonly int[] _color;
  private readonly PwcStatistic[] _statistic;
  private readonly int[] _newer;
  private readonly int[] _older;
  private int _newest;

  public PwcGuessPool(int keys, int entries) {
    this._buckets = new List<int>[keys];
    for (var i = 0; i < keys; ++i)
      this._buckets[i] = [];

    this._key = new int[entries];
    this._color = new int[entries];
    this._statistic = new PwcStatistic[entries];
    this._newer = new int[entries];
    this._older = new int[entries];
    Array.Fill(this._key, -1);
    Array.Fill(this._statistic, PwcStatistic.Fresh);

    // Recency ring: entry 0 is the least recent, so it is recycled first, then 1, and so on.
    for (var i = 0; i < entries; ++i) {
      this._older[i] = (i + entries - 1) % entries;
      this._newer[i] = (i + 1) % entries;
    }

    this._newest = entries - 1;
  }

  public List<int> Bucket(int key) => this._buckets[key];

  public int ColorOf(int entry) => this._color[entry];

  public ref PwcStatistic StatisticOf(int entry) => ref this._statistic[entry];

  public bool Contains(int key, int color) {
    foreach (var entry in this._buckets[key])
      if (this._color[entry] == color)
        return true;

    return false;
  }

  /// <summary>A confirmed guess moves to the front of its list and becomes the most recent entry.</summary>
  public void Promote(int entry) {
    var bucket = this._buckets[this._key[entry]];
    bucket.Remove(entry);
    bucket.Insert(0, entry);
    this._Touch(entry);
  }

  /// <summary>Recycles the least recent entry for a new colour, appended to the end of its key's list; its statistic carries over.</summary>
  public void Allocate(int key, int color) {
    var entry = this._older[this._newest];
    this._Touch(entry);
    if (this._key[entry] >= 0)
      this._buckets[this._key[entry]].Remove(entry);

    this._key[entry] = key;
    this._color[entry] = color;
    this._buckets[key].Add(entry);
  }

  private void _Touch(int entry) {
    if (entry == this._newest)
      return;

    // Unlink, then re-insert between the oldest and the newest of the ring, and make it the newest.
    var older = this._older[entry];
    var newer = this._newer[entry];
    this._newer[older] = newer;
    this._older[newer] = older;

    var oldest = this._older[this._newest];
    this._newer[oldest] = entry;
    this._older[entry] = oldest;
    this._newer[entry] = this._newest;
    this._older[this._newest] = entry;
    this._newest = entry;
  }
}
