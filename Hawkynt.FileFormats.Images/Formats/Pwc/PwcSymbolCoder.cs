using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pwc;

/// <summary>
/// The multi-symbol coder behind the "what is this colour?" decision: a prediction error folded to a
/// non-negative rank, coded as an escape sequence over groups of ranks followed by a binary index inside the group.
/// </summary>
/// <remarks>
/// <para>
/// Ranks are grouped into blocks whose size is a power of two. Every activity class owns a chain of blocks:
/// class <c>c</c> starts with blocks of <c>2^c</c> ranks and, once its own blocks are used up, continues into the
/// larger blocks of class <c>c+1</c> at the point covering the same ranks — so the classes share their tails, and
/// share the statistics in them. A busy neighbourhood selects a class with coarse blocks from the start.
/// </para>
/// <para>
/// Each block carries one escape context ("the rank lies beyond this block") and <c>size-1</c> contexts for a
/// binary tree that picks the rank inside it. Every context here uses the fixed (non-limited) adaptation.
/// </para>
/// </remarks>
internal sealed class PwcSymbolCoder {

  private readonly int _maximum;
  private readonly int _classes;
  private readonly int[] _chainHeads;
  private readonly List<int> _size = [];
  private readonly List<int> _next = [];
  private readonly List<int> _treeStart = [];
  private PwcStatistic[] _statistics = [];

  /// <param name="maximum">The largest value that can be coded (the palette size minus one).</param>
  public PwcSymbolCoder(int maximum) {
    this._maximum = maximum;
    this._classes = PwcBits.Length(maximum);
    this._chainHeads = new int[this._classes + 1];
    this._BuildChains();
    this._ResetStatistics();
  }

  /// <summary>Codes <paramref name="value"/> (ignored when decoding) around <paramref name="prediction"/> under an activity class.</summary>
  public int Code(PwcArithmeticCoder coder, int value, int prediction, int activity) {
    var block = this._chainHeads[Math.Min(activity, this._classes)];
    var rank = 0;
    if (coder.IsEncoding) {
      if (value > this._maximum)
        throw new InvalidOperationException($"Colour index {value} lies beyond the coder's range of {this._maximum}.");

      rank = this._RankOf(value, prediction);
    }

    // Escape past whole blocks until the one holding the rank.
    var passed = 0;
    while (true) {
      var size = this._size[block];
      var beyond = coder.Fixed(ref this._statistics[this._StatisticOf(block)], rank - passed >= size ? 1 : 0);
      if (beyond == 0)
        break;

      passed += size;
      block = this._next[block];
      if (block < 0)
        throw new InvalidDataException("PWC colour rank escapes past the last block.");
    }

    // Binary index inside the block: a zero steps into the left subtree, a one jumps over it.
    var offset = rank - passed;
    var context = this._treeStart[block];
    for (var half = this._size[block] >> 1; half != 0; half >>= 1) {
      if (coder.Fixed(ref this._statistics[context], (offset & half) != 0 ? 1 : 0) != 0) {
        // Block starts are multiples of the block size, so OR and addition agree; the shipped decoder ORs.
        passed |= half;
        context += half;
      } else
        ++context;
    }

    return coder.IsEncoding ? value : this._ValueOf(passed, prediction);
  }

  /// <summary>Unfolds a rank: zero is the prediction, then alternate sides, then whatever is left on the wider side.</summary>
  private int _ValueOf(int rank, int prediction) {
    var maximum = this._maximum;
    if (rank == 0 && prediction <= maximum)
      return prediction;

    var room = maximum - prediction;
    if (room <= prediction) {
      if (room * 2 < rank)
        return maximum - rank;

      return (rank & 1) != 0 ? prediction + (rank + 1) / 2 : prediction - rank / 2;
    }

    if (rank <= prediction * 2)
      return (rank & 1) != 0 ? prediction - (rank + 1) / 2 : prediction + rank / 2;

    return rank;
  }

  /// <summary>The inverse of <see cref="_ValueOf"/>.</summary>
  private int _RankOf(int value, int prediction) {
    var room = this._maximum - prediction;
    int doubled;
    if (prediction < room) {
      var above = value - prediction;
      if (prediction < above)
        return value;

      doubled = above * 2;
    } else {
      var below = prediction - value;
      if (room < below)
        return below + room;

      doubled = below * 2;
    }

    return doubled < 0 ? ~doubled : doubled;
  }

  private int _StatisticOf(int block) => this._treeStart[block] - 1;

  private int _AddBlock(int size, int next) {
    var index = this._size.Count;
    this._size.Add(size);
    this._next.Add(next);
    this._treeStart.Add(-1);
    return index;
  }

  private void _BuildChains() {
    // Block counts per class: at least eight (rounded to even), the last class (or one that would reach past the
    // maximum) just enough to cover every rank. Each class doubles the block size and halves the carried count.
    var count = 0u;
    var size = 1;
    for (var activity = 0; activity <= this._classes; ++activity) {
      var target = Math.Max(count + 1, 8u);
      count = (target + 1) & ~1u;
      var cover = (uint)this._maximum / (uint)size;
      if (cover < count || activity == this._classes)
        count = cover + 1;

      var head = -1;
      for (var i = 0u; i < count; ++i)
        head = this._AddBlock(size, head);

      this._chainHeads[activity] = head;
      count >>= 1;
      size <<= 1;
    }

    // Splice each class onto the next: two blocks of one class cover one block of the next, so after an even
    // number of own blocks the chain continues halfway that far into the next class. An odd count ends the chain.
    for (var activity = 1; activity <= this._classes; ++activity) {
      var lower = this._chainHeads[activity - 1];
      var upper = this._chainHeads[activity];
      int last;
      var linked = true;
      do {
        if (upper < 0)
          throw new InvalidOperationException("PWC symbol chains cannot be spliced.");

        last = lower;
        if (this._size[lower] != this._size[upper]) {
          last = this._next[lower];
          if (last < 0) {
            linked = false;
            break;
          }
        }

        lower = this._next[last];
        upper = this._next[upper];
      } while (lower >= 0);

      if (linked)
        this._next[last] = upper;
    }

    // Lay out statistics: the escape context, then the tree contexts, per block.
    var statistics = 0;
    for (var block = 0; block < this._size.Count; ++block) {
      this._treeStart[block] = statistics + 1;
      statistics += this._size[block];
    }

    this._statistics = new PwcStatistic[statistics];
  }

  private void _ResetStatistics() {
    var statistics = this._statistics;
    for (var activity = 0; activity <= this._classes; ++activity) {
      var block = this._chainHeads[activity];
      var remaining = this._maximum + 1;
      while (true) {
        statistics[this._StatisticOf(block)] = PwcStatistic.Fresh;
        var tree = this._treeStart[block];
        for (var i = 0; i < this._size[block] - 1; ++i)
          statistics[tree + i] = new() { Zeros = 4, Total = 8 };

        if (remaining <= this._size[block])
          break;

        remaining -= this._size[block];
        block = this._next[block];
      }

      // The block holding the last rank never escapes, and the branches past that rank are never taken.
      statistics[this._StatisticOf(block)] = PwcStatistic.Certain;
      var context = this._treeStart[block];
      for (var half = this._size[block] >> 1; half != 0; half >>= 1) {
        if (half < remaining) {
          remaining -= half;
          context += half;
        } else
          statistics[context++] = PwcStatistic.Certain;
      }
    }
  }
}

/// <summary>Bit-length helper shared by the PWC models.</summary>
internal static class PwcBits {

  /// <summary>The number of significant bits in a non-negative value; zero has none.</summary>
  public static int Length(int value) {
    var bits = 0;
    for (; value > 0; value >>= 1)
      ++bits;

    return bits;
  }
}
