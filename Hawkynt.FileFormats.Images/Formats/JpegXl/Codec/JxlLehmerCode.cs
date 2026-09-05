using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Turns a Lehmer code back into the permutation it stands for (libjxl
/// <c>DecodeLehmerCode</c> in <c>lib/jxl/lehmer_code.h</c>).
/// </summary>
/// <remarks>
/// The code states, for each position in turn, how many of the values still
/// unused are smaller than the one that goes there. Reading it back is a run of
/// "find the n-th value still available, then take it out", which is done here
/// over a Fenwick tree of counts so both halves are logarithmic rather than
/// linear — the same structure libjxl uses, and the reason the tree is walked
/// from its top bit downwards instead of scanned.
/// </remarks>
internal static class JxlLehmerCode {

  /// <summary>The permutation a code of <paramref name="count"/> entries states.</summary>
  /// <param name="code">One entry per position; entry <c>i</c> must be less
  /// than <c>count - i</c>, which is how many values are still unused there.</param>
  /// <param name="count">Length of the permutation. Must be positive.</param>
  public static int[] Decode(int[] code, int count) {
    ArgumentNullException.ThrowIfNull(code);
    if (count <= 0)
      throw new ArgumentOutOfRangeException(nameof(count), "A permutation has at least one entry.");
    if (code.Length < count)
      throw new ArgumentException($"A permutation of {count} needs {count} code entries, not {code.Length}.", nameof(code));

    var log2N = _CeilLog2(count);
    var paddedCount = 1 << log2N;

    // Each slot holds how many values are still available in the range the
    // Fenwick tree gives it, which starts as the size of that range.
    var counts = new uint[paddedCount];
    for (var i = 0; i < paddedCount; ++i)
      counts[i] = (uint)_LowestSetBit(i + 1);

    var permutation = new int[count];
    for (var i = 0; i < count; ++i) {
      if (code[i] < 0 || code[i] + i >= count)
        throw new ArgumentException($"Code entry {i} is {code[i]}, which names no value still unused.", nameof(code));

      // Walk down the tree to the value with this many smaller ones left.
      var rank = (uint)code[i] + 1;
      var bit = paddedCount;
      var next = 0;
      for (var b = 0; b <= log2N; ++b) {
        var candidate = next + bit;
        bit >>= 1;
        if (counts[candidate - 1] >= rank)
          continue;

        next = candidate;
        rank -= counts[candidate - 1];
      }

      permutation[i] = next;

      // Take it out, so the next position does not see it.
      for (var at = next + 1; at <= paddedCount; at += _LowestSetBit(at))
        --counts[at - 1];
    }

    return permutation;
  }

  private static int _LowestSetBit(int value) => value & -value;

  private static int _CeilLog2(int value) {
    var bits = 0;
    while ((1 << bits) < value)
      ++bits;
    return bits;
  }
}
