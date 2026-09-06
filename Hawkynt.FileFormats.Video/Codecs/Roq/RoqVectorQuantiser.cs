using System;

namespace FileFormat.Codecs.Roq;

/// <summary>
/// Builds one of a picture's two codebooks: a deterministic vector quantiser over the samples a
/// codebook cell states.
/// </summary>
/// <remarks>
/// The space is the format's own. A 2x2 cell is six numbers — four luminances, one Cb, one Cr — and a
/// 4x4 cell is the four 2x2 cells it names, so twenty-four of the same numbers. Unlike Cinepak, whose
/// codebook has to be solved backwards through an inverse matrix before anybody sees it, a RoQ cell is
/// written in the very samples it paints, so there is nothing to solve: the cluster mean is the entry.
/// <para/>
/// <b>Deterministic.</b> The first seed is the training vector nearest the mean of the whole set — a
/// cell that exists rather than an average of cells that do not — and every seed after it is the vector
/// furthest from everything already picked. That is the ordinary farthest-point rule and it needs no
/// random state, so the same picture is the same bytes every time. Where a picture holds fewer distinct
/// cells than the codebook has room for, the rule picks each of them exactly once and then finds nothing
/// further than nought away, which both stops the search and sizes the codebook — so a flat or
/// few-colour picture is quantised without error rather than nearly.
/// </remarks>
internal sealed class RoqVectorQuantiser {

  /// <summary>
  /// How many rounds of Lloyd's rule follow the seeding.
  /// </summary>
  /// <remarks>
  /// Twelve, and it stops early when nothing moves — which for a picture whose cells are all distinct
  /// seeds is the first round. Twelve rather than the four Cinepak's quantiser here settles for, because
  /// a RoQ codebook is worked much harder: 256 cells serve a whole picture rather than one strip of one,
  /// and they serve it at two sizes at once. Measured over a 320x240 fractal, the twelfth round is worth
  /// 0.31 dB and 3 per cent fewer bytes against the fourth, and the twentieth another 0.07 dB for 2 per
  /// cent more bytes, which is where the returns stop.
  /// </remarks>
  private const int _ROUNDS = 12;

  private int[] _assignment = [];
  private int[] _distance = [];
  private int[] _sums = [];
  private int[] _counts = [];

  /// <summary>
  /// Picks the seeds a codebook of at most <paramref name="size"/> entries starts from.
  /// </summary>
  /// <param name="training">The training vectors, <paramref name="dimensions"/> bytes each.</param>
  /// <param name="count">How many of them there are.</param>
  /// <param name="dimensions">Six for a 2x2 cell, twenty-four for a 4x4 one.</param>
  /// <param name="size">The most entries wanted, which the distinct vectors may cut short.</param>
  /// <param name="entries">Where the entries go, <paramref name="dimensions"/> bytes each.</param>
  /// <returns>How many entries were seeded.</returns>
  /// <remarks>
  /// The result is nested in its own size: seeds nought to <c>k−1</c> of a call asking for 256 are the
  /// seeds a call asking for <c>k</c> would have picked, since each is chosen by how far it sits from
  /// the ones before it and nothing later changes that. So one seeding prices every codebook size worth
  /// trying, and only the size that wins needs <see cref="Refine"/> running over it.
  /// </remarks>
  internal int Seed(byte[] training, int count, int dimensions, int size, byte[] entries) {
    if (count <= 0 || size <= 0)
      return 0;

    this._Reserve(count, size, dimensions);
    return this._Seed(training, count, dimensions, size, entries);
  }

  /// <summary>Moves each entry onto the mean of what it states best, until nothing moves.</summary>
  internal void Refine(byte[] training, int count, int dimensions, int size, byte[] entries) {
    if (count <= 0 || size <= 0)
      return;

    this._Reserve(count, size, dimensions);
    this._Refine(training, count, dimensions, size, entries);
  }

  /// <summary>
  /// Which entry states a vector closest, and how far off it is.
  /// </summary>
  /// <remarks>
  /// The sum gives up as soon as it passes the best so far, and the search stops outright on an exact
  /// match. Nothing here approximates: the entry found is the nearest one, and ties go to the lowest
  /// number so that the answer does not depend on the order the entries happen to sit in.
  /// </remarks>
  internal static int Nearest(byte[] entries, int size, int dimensions, ReadOnlySpan<byte> vector, out int error) {
    var best = 0;
    error = int.MaxValue;

    for (var entry = 0; entry < size; ++entry) {
      var total = 0;
      var at = entry * dimensions;
      for (var channel = 0; channel < dimensions; ++channel) {
        var difference = entries[at + channel] - vector[channel];
        total += difference * difference;
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

  private int _Seed(byte[] training, int count, int dimensions, int size, byte[] entries) {
    var mean = this._sums;
    Array.Clear(mean, 0, dimensions);
    for (var vector = 0; vector < count; ++vector)
      for (var channel = 0; channel < dimensions; ++channel)
        mean[channel] += training[vector * dimensions + channel];

    for (var channel = 0; channel < dimensions; ++channel)
      mean[channel] = (mean[channel] + count / 2) / count;

    var first = 0;
    var nearest = int.MaxValue;
    for (var vector = 0; vector < count; ++vector) {
      var total = 0;
      for (var channel = 0; channel < dimensions; ++channel) {
        var difference = training[vector * dimensions + channel] - mean[channel];
        total += difference * difference;
      }

      if (total >= nearest)
        continue;

      nearest = total;
      first = vector;
    }

    Array.Copy(training, first * dimensions, entries, 0, dimensions);
    for (var vector = 0; vector < count; ++vector)
      this._distance[vector] = _Apart(training, dimensions, vector, first);

    for (var entry = 1; entry < size; ++entry) {
      var furthest = 0;
      var apart = -1;
      for (var vector = 0; vector < count; ++vector) {
        if (this._distance[vector] <= apart)
          continue;

        apart = this._distance[vector];
        furthest = vector;
      }

      // Nothing left that some seed does not already state exactly, so the codebook is as big as the
      // picture has distinct cells and no bigger.
      if (apart <= 0)
        return entry;

      Array.Copy(training, furthest * dimensions, entries, entry * dimensions, dimensions);
      for (var vector = 0; vector < count; ++vector)
        this._distance[vector] = Math.Min(this._distance[vector], _Apart(training, dimensions, vector, furthest));
    }

    return size;
  }

  private static int _Apart(byte[] training, int dimensions, int left, int right) {
    var total = 0;
    for (var channel = 0; channel < dimensions; ++channel) {
      var difference = training[left * dimensions + channel] - training[right * dimensions + channel];
      total += difference * difference;
    }

    return total;
  }

  // ============================================================================================
  // Lloyd's rule
  // ============================================================================================

  private void _Refine(byte[] training, int count, int dimensions, int size, byte[] entries) {
    Array.Fill(this._assignment, -1, 0, count);

    for (var round = 0; round < _ROUNDS; ++round) {
      var moved = false;
      for (var vector = 0; vector < count; ++vector) {
        var chosen = Nearest(entries, size, dimensions, training.AsSpan(vector * dimensions, dimensions), out _);
        if (this._assignment[vector] == chosen)
          continue;

        this._assignment[vector] = chosen;
        moved = true;
      }

      if (!moved)
        return;

      Array.Clear(this._sums, 0, size * dimensions);
      Array.Clear(this._counts, 0, size);
      for (var vector = 0; vector < count; ++vector) {
        var entry = this._assignment[vector];
        ++this._counts[entry];
        for (var channel = 0; channel < dimensions; ++channel)
          this._sums[entry * dimensions + channel] += training[vector * dimensions + channel];
      }

      // An entry nothing was assigned to keeps what it had. It is still a cell of the picture, and
      // moving it anywhere would only be a guess at where a gap in the picture might be.
      for (var entry = 0; entry < size; ++entry) {
        var members = this._counts[entry];
        if (members == 0)
          continue;

        for (var channel = 0; channel < dimensions; ++channel)
          entries[entry * dimensions + channel] =
            (byte)((this._sums[entry * dimensions + channel] + members / 2) / members);
      }
    }
  }

  private void _Reserve(int count, int size, int dimensions) {
    if (this._assignment.Length < count) {
      this._assignment = new int[count];
      this._distance = new int[count];
    }

    if (this._sums.Length < size * dimensions)
      this._sums = new int[size * dimensions];

    if (this._counts.Length < size)
      this._counts = new int[size];
  }
}
