using System;

namespace FileFormat.Codecs.Cinepak;

/// <summary>
/// Builds one of a strip's codebooks: a deterministic vector quantiser over the colours a codebook
/// entry has to paint.
/// </summary>
/// <remarks>
/// The space is the twelve numbers a codebook entry paints — four colours of red, green and blue — and
/// not the four luminances and two chrominances it is written as. Clustering in the written form would
/// measure a chrominance level against a luminance level as if they were the same size, when the
/// inverse matrix doubles one and halves the other; clustering in what comes out measures the thing
/// anybody will actually see.
/// <para/>
/// <b>Deterministic, where FFmpeg's is not.</b> The reference encoder reaches for ELBG, seeded from a
/// pseudo-random generator, so the same picture need not produce the same bytes twice. This seeds from
/// the training vector nearest the mean and then repeatedly takes the vector furthest from everything
/// chosen so far, which is the ordinary farthest-point rule: no randomness, and every run of the same
/// picture is the same file.
/// <para/>
/// That seeding also settles the case the format is best at. Where a strip holds fewer distinct blocks
/// than the codebook has room for, the furthest-point rule picks each of them exactly once and then
/// finds nothing further than nought away, which both stops the search and sizes the codebook — so a
/// flat or few-colour strip is quantised without error rather than nearly.
/// </remarks>
internal sealed class CinepakVectorQuantiser {

  /// <summary>The twelve numbers of a training vector: four colours of red, green and blue.</summary>
  internal const int Dimensions = CinepakEntrySolver.TargetLength;

  /// <summary>
  /// How many rounds of Lloyd's rule follow the seeding.
  /// </summary>
  /// <remarks>
  /// Four, and it stops early when nothing moves — which for a strip whose blocks are all distinct
  /// seeds is the first round. The gain past four rounds is under a level of squared error a block on
  /// everything measured, and every round costs a full nearest-entry pass over the strip.
  /// </remarks>
  private const int _ROUNDS = 4;

  private int[] _assignment = [];
  private int[] _distance = [];
  private int[] _sums = [];

  /// <summary>The colours each entry was last solved for, so that solving it again can be skipped.</summary>
  private int[] _solved = [];

  private int[] _counts = [];
  private int[] _targets = [];

  /// <summary>
  /// Quantises a training set into at most <paramref name="size"/> entries.
  /// </summary>
  /// <param name="training">The training vectors, <see cref="Dimensions"/> bytes each.</param>
  /// <param name="count">How many of them there are.</param>
  /// <param name="size">The most entries wanted, which the distinct vectors may cut short.</param>
  /// <param name="entries">Where the written form goes, six bytes an entry.</param>
  /// <param name="painted">Where the four colours each entry paints go, twelve bytes an entry.</param>
  /// <returns>How many entries were built.</returns>
  internal int Build(byte[] training, int count, int size, byte[] entries, byte[] painted) {
    if (count <= 0 || size <= 0)
      return 0;

    this._Reserve(count, size);
    size = this._Seed(training, count, size, entries, painted);
    this._Refine(training, count, size, entries, painted);
    return size;
  }

  /// <summary>
  /// Which entry paints a training vector closest, and how far off it is.
  /// </summary>
  /// <remarks>
  /// The inner sum gives up as soon as it passes the best so far, which on a full 256-entry codebook
  /// throws out most candidates after one colour of the four. That and stopping outright on an exact
  /// match are what keep a full search affordable; nothing here approximates, so the entry found is the
  /// nearest one and ties go to the lowest number.
  /// </remarks>
  internal static int Nearest(byte[] painted, int size, ReadOnlySpan<byte> vector, out int error) {
    var best = 0;
    error = int.MaxValue;

    for (var entry = 0; entry < size; ++entry) {
      var total = 0;
      var at = entry * Dimensions;
      for (var colour = 0; colour < Dimensions; colour += 3) {
        for (var channel = colour; channel < colour + 3; ++channel) {
          var difference = painted[at + channel] - vector[channel];
          total += difference * difference;
        }

        if (total >= error)
          break;
      }

      if (total >= error)
        continue;

      error = total;
      best = entry;
      if (total == 0)
        break;
    }

    return best;
  }

  // ============================================================================================
  // Where the entries start
  // ============================================================================================

  /// <summary>
  /// Picks the seeds and solves an entry for each, returning how many there turned out to be.
  /// </summary>
  /// <remarks>
  /// The first seed is the vector nearest the mean of the whole training set rather than the mean
  /// itself, so that a codebook of one entry is a block that exists rather than an average of blocks
  /// that do not. Every seed after it is the vector furthest from everything picked, which is the
  /// ordinary farthest-point rule and needs no random state to be reproducible.
  /// </remarks>
  private int _Seed(byte[] training, int count, int size, byte[] entries, byte[] painted) {
    var mean = this._sums;
    Array.Clear(mean, 0, Dimensions);
    for (var vector = 0; vector < count; ++vector)
      for (var channel = 0; channel < Dimensions; ++channel)
        mean[channel] += training[vector * Dimensions + channel];

    for (var channel = 0; channel < Dimensions; ++channel)
      mean[channel] = (mean[channel] + count / 2) / count;

    var first = 0;
    var nearest = int.MaxValue;
    for (var vector = 0; vector < count; ++vector) {
      var total = 0;
      for (var channel = 0; channel < Dimensions; ++channel) {
        var difference = training[vector * Dimensions + channel] - mean[channel];
        total += difference * difference;
      }

      if (total >= nearest)
        continue;

      nearest = total;
      first = vector;
    }

    this._Adopt(training, first, 0, entries, painted);
    for (var vector = 0; vector < count; ++vector)
      this._distance[vector] = _Apart(training, vector, first);

    for (var entry = 1; entry < size; ++entry) {
      var furthest = 0;
      var apart = -1;
      for (var vector = 0; vector < count; ++vector) {
        if (this._distance[vector] <= apart)
          continue;

        apart = this._distance[vector];
        furthest = vector;
      }

      // Nothing left that any seed does not already state exactly, so the codebook is as big as the
      // strip has distinct blocks and no bigger.
      if (apart <= 0)
        return entry;

      this._Adopt(training, furthest, entry, entries, painted);
      for (var vector = 0; vector < count; ++vector)
        this._distance[vector] = Math.Min(this._distance[vector], _Apart(training, vector, furthest));
    }

    return size;
  }

  private static int _Apart(byte[] training, int left, int right) {
    var total = 0;
    for (var channel = 0; channel < Dimensions; ++channel) {
      var difference = training[left * Dimensions + channel] - training[right * Dimensions + channel];
      total += difference * difference;
    }

    return total;
  }

  /// <summary>Solves the entry that comes closest to painting one training vector.</summary>
  private void _Adopt(byte[] training, int vector, int entry, byte[] entries, byte[] painted) {
    for (var channel = 0; channel < Dimensions; ++channel)
      this._targets[channel] = training[vector * Dimensions + channel];

    Array.Copy(this._targets, 0, this._solved, entry * Dimensions, Dimensions);
    _Solve(this._targets, entry, entries, painted);
  }

  // ============================================================================================
  // Lloyd's rule
  // ============================================================================================

  private void _Refine(byte[] training, int count, int size, byte[] entries, byte[] painted) {
    Array.Fill(this._assignment, -1, 0, count);

    for (var round = 0; round < _ROUNDS; ++round) {
      var moved = false;
      for (var vector = 0; vector < count; ++vector) {
        var chosen = Nearest(painted, size, training.AsSpan(vector * Dimensions, Dimensions), out _);
        if (this._assignment[vector] == chosen)
          continue;

        this._assignment[vector] = chosen;
        moved = true;
      }

      if (!moved)
        return;

      Array.Clear(this._sums, 0, size * Dimensions);
      Array.Clear(this._counts, 0, size);
      for (var vector = 0; vector < count; ++vector) {
        var entry = this._assignment[vector];
        ++this._counts[entry];
        for (var channel = 0; channel < Dimensions; ++channel)
          this._sums[entry * Dimensions + channel] += training[vector * Dimensions + channel];
      }

      // An entry nothing was assigned to keeps what it had. It is still a block of the strip, and
      // moving it anywhere would only be a guess at where a gap in the picture might be.
      for (var entry = 0; entry < size; ++entry) {
        var members = this._counts[entry];
        if (members == 0)
          continue;

        var settled = true;
        for (var channel = 0; channel < Dimensions; ++channel) {
          this._targets[channel] = (this._sums[entry * Dimensions + channel] + members / 2) / members;
          settled &= this._targets[channel] == this._solved[entry * Dimensions + channel];
        }

        // Most rounds move a handful of vectors and leave every other cluster's mean where it was, and
        // solving an entry is the dearest thing in here. So one that is asked for the same colours it
        // was solved for last time keeps the answer.
        if (settled)
          continue;

        Array.Copy(this._targets, 0, this._solved, entry * Dimensions, Dimensions);
        _Solve(this._targets, entry, entries, painted);
      }
    }
  }

  private static void _Solve(int[] targets, int entry, byte[] entries, byte[] painted) {
    var written = entries.AsSpan(entry * CinepakEntrySolver.EntryLength, CinepakEntrySolver.EntryLength);
    CinepakEntrySolver.Solve(targets, written);
    CinepakColorConversion.ToRgb(written[..4], written[4], written[5], painted.AsSpan(entry * Dimensions, Dimensions));
  }

  private void _Reserve(int count, int size) {
    if (this._assignment.Length < count) {
      this._assignment = new int[count];
      this._distance = new int[count];
    }

    if (this._sums.Length < size * Dimensions) {
      this._sums = new int[size * Dimensions];
      this._solved = new int[size * Dimensions];
      this._counts = new int[size];
    }

    if (this._targets.Length == 0)
      this._targets = new int[Dimensions];
  }
}
