using System;
using System.Collections.Generic;

namespace FileFormat.WhyPic;

/// <summary>The eight interpolated probability models a WhyPic pixel is predicted from.</summary>
/// <remarks>
/// Eight neighbours are looked at — two rows up, three in the row above, four to the left:
/// <code>
///         C0
///      C1 C2 C3
/// C4 C5 C6 C7 X
/// </code>
/// Model 0 is the even split, model 1 counts colours regardless of context, and models 2 to 7 count
/// colours following ever longer contexts: C7; C2 C7; C2 C6 C7; C1 C2 C3 C6 C7; C0 C1 C2 C3 C6 C7;
/// and all eight. A model whose context has never been seen is useless, and so is every longer one,
/// so the prediction backs off to the longest context that has a count. The order reached —
/// <see cref="Order"/>, 0 to 7 — selects which of the eight sets of interpolation weights mixes them.
/// <para/>
/// The picture is a single run of dots: the rightmost one of a row borders the leftmost of the
/// next, and everything above the first row is taken to be the colour of the very first dot.
/// </remarks>
internal sealed class WhyPicContextModel {

  /// <summary>The number of context models above the plain colour count.</summary>
  public const int ContextLevels = 6;

  /// <summary>The highest back-off order, reached when every context has been seen.</summary>
  public const int MaxOrder = ContextLevels + 1;

  /// <summary>The fixed scale every cumulative probability is truncated onto: 2^16 weight steps × 256.</summary>
  public const int ProbabilityScale = 65536 * 256;

  private sealed class Context {
    public int Total;
    public int Max;
    public byte[] Symbols = new byte[2];
    public int[] Counts = new int[2];
    public int Distinct;

    public int CountOf(int symbol) {
      for (var i = 0; i < this.Distinct; ++i)
        if (this.Symbols[i] == symbol)
          return this.Counts[i];
      return 0;
    }

    public int Increment(int symbol) {
      for (var i = 0; i < this.Distinct; ++i)
        if (this.Symbols[i] == symbol)
          return ++this.Counts[i];

      if (this.Distinct == this.Symbols.Length) {
        Array.Resize(ref this.Symbols, this.Distinct * 2);
        Array.Resize(ref this.Counts, this.Distinct * 2);
      }

      this.Symbols[this.Distinct] = (byte)symbol;
      this.Counts[this.Distinct] = 1;
      ++this.Distinct;
      return 1;
    }
  }

  private readonly int _colors;
  private readonly int[] _unigram;
  private int _seen;
  private readonly Dictionary<ulong, Context>[] _levels = new Dictionary<ulong, Context>[ContextLevels];
  private readonly ulong[] _keys = new ulong[ContextLevels];

  /// <summary>Counts per order: row <c>g</c> holds model <c>g + 1</c>'s counts by colour.</summary>
  public readonly int[][] Frequencies = new int[MaxOrder][];

  /// <summary>The total behind each row of <see cref="Frequencies"/>.</summary>
  public readonly int[] Totals = new int[MaxOrder];

  /// <summary>How far the back-off reached for the context last looked up.</summary>
  public int Order { get; private set; }

  /// <summary>The colour the full eight-dot context has seen most often, kept as it is counted.</summary>
  public int TopContextLeader { get; private set; }

  public WhyPicContextModel(int colors) {
    this._colors = colors;
    this._unigram = new int[colors];
    for (var i = 0; i < ContextLevels; ++i)
      this._levels[i] = [];
    for (var g = 0; g < MaxOrder; ++g)
      this.Frequencies[g] = new int[colors];
  }

  private void _Keys(ReadOnlySpan<byte> c) {
    this._keys[0] = c[7];
    this._keys[1] = this._keys[0] | (ulong)c[2] << 8;
    this._keys[2] = this._keys[1] | (ulong)c[6] << 16;
    this._keys[3] = this._keys[2] | (ulong)c[1] << 24 | (ulong)c[3] << 32;
    this._keys[4] = this._keys[3] | (ulong)c[0] << 40;
    this._keys[5] = this._keys[4] | (ulong)c[4] << 48 | (ulong)c[5] << 56;
  }

  /// <summary>Looks up the counts that apply to a dot whose eight neighbours are <paramref name="neighbours"/>.</summary>
  public void Lookup(ReadOnlySpan<byte> neighbours) {
    if (this._seen == 0) {
      this.Order = 0;
      return;
    }

    this.Totals[0] = this._seen;
    this._unigram.CopyTo(this.Frequencies[0], 0);
    this._Keys(neighbours);

    var order = 1;
    for (var level = 0; level < ContextLevels; ++level) {
      if (!this._levels[level].TryGetValue(this._keys[level], out var context) || context.Total == 0)
        break;

      var row = this.Frequencies[level + 1];
      Array.Clear(row);
      for (var i = 0; i < context.Distinct; ++i)
        row[context.Symbols[i]] = context.Counts[i];

      this.Totals[level + 1] = context.Total;
      if (level == ContextLevels - 1)
        this.TopContextLeader = context.Max;
      ++order;
    }

    this.Order = order;
  }

  /// <summary>Counts <paramref name="symbol"/> after the context <paramref name="neighbours"/>.</summary>
  public void Update(ReadOnlySpan<byte> neighbours, int symbol) {
    ++this._seen;
    ++this._unigram[symbol];
    this._Keys(neighbours);

    Context? top = null;
    var topCount = 0;
    for (var level = 0; level < ContextLevels; ++level) {
      var contexts = this._levels[level];
      if (!contexts.TryGetValue(this._keys[level], out var context)) {
        context = new();
        contexts.Add(this._keys[level], context);
      }

      ++context.Total;
      var count = context.Increment(symbol);
      if (level == ContextLevels - 1) {
        top = context;
        topCount = count;
      }
    }

    // The leader changes hands only when the newcomer has strictly overtaken it, so of two colours
    // tied for the lead the one that got there first keeps it.
    if (top!.Max != symbol && (top.Total == 1 || topCount > top.CountOf(top.Max)))
      top.Max = symbol;
  }

  /// <summary>The colour with the highest count at the order reached, the earliest on a tie.</summary>
  public int MostLikely() {
    if (this.Order == MaxOrder)
      return this.TopContextLeader;
    if (this.Order == 0)
      return 0;

    var row = this.Frequencies[this.Order - 1];
    var best = 0;
    for (var c = 1; c < this._colors; ++c)
      if (row[c] > row[best])
        best = c;
    return best;
  }

  /// <summary>Puts colour <paramref name="max"/> first in every order's counts, where version 1.1 codes it.</summary>
  public void SwapToFront(int max) {
    if (max == 0)
      return;
    for (var g = 0; g < this.Order; ++g) {
      var row = this.Frequencies[g];
      (row[0], row[max]) = (row[max], row[0]);
    }
  }

  /// <summary>Turns every order's counts into running totals, which is what intervals are cut from.</summary>
  public void Accumulate() {
    for (var g = 0; g < this.Order; ++g) {
      var row = this.Frequencies[g];
      for (var c = 1; c < this._colors; ++c)
        row[c] += row[c - 1];
    }
  }

  /// <summary>Where colour <paramref name="c"/>'s interval starts, on <see cref="ProbabilityScale"/>, once accumulated.</summary>
  /// <remarks>
  /// The even split is weighted and multiplied out first, then each model's share is added in
  /// order of increasing context, each as weight times count divided by total. Changing that order
  /// moves the sum by a unit in the last place often enough to matter.
  /// </remarks>
  public int Cumulative(double[] weights, int c, double uniform) {
    if (c == 0)
      return 0;

    var p = weights[0] * uniform * c;
    for (var g = 1; g <= this.Order; ++g)
      p += weights[g] * this.Frequencies[g - 1][c - 1] / this.Totals[g - 1];
    return (int)(p * ProbabilityScale);
  }
}
