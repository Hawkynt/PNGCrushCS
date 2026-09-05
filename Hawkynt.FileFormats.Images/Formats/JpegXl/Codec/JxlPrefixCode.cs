using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The writing half of the prefix codes the format uses in place of its
/// arithmetic coder (ISO/IEC 18181-1 §C.5; libjxl <c>lib/jxl/enc_huffman.cc</c>
/// and <c>lib/jxl/dec_huffman.cc</c>).
/// </summary>
/// <remarks>
/// A prefix code is written one of two ways, and which one is not a choice: an
/// alphabet with at most four symbols in use has to go in the short form, and
/// anything larger in the long one, because the reader decides which it is from
/// the same two bits either way.
///
/// <para>The long form states the lengths of its own code lengths first, and the
/// reader stops taking those the moment their weights add up to a complete code
/// rather than at a stated count. That is why the loop below breaks on the
/// running total: writing one entry more than the reader takes puts every bit
/// after it in the wrong place.</para>
/// </remarks>
internal sealed class JxlPrefixCode {

  /// <summary>Bit length per symbol; zero for a symbol the code never emits.</summary>
  private int[] _lengths = [];

  /// <summary>Canonical code per symbol, most significant bit first.</summary>
  private int[] _codes = [];

  /// <summary>True when the code carries a single symbol and so states nothing per token.</summary>
  private bool _silent;

  /// <summary>libjxl <c>kCodeLengthCodeOrder</c>: the order the code-length code
  /// lengths are stated in, which puts the lengths most codes use first.</summary>
  private static readonly byte[] _CodeLengthCodeOrder =
    [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];

  /// <summary>The longest a code length may be (libjxl <c>PREFIX_MAX_BITS</c>).</summary>
  private const int _MaxCodeLength = 15;

  /// <summary>
  /// The longest a code-length code length may be. The static code the reader
  /// uses for those states only the values zero to five, so a code-length code
  /// deeper than five cannot be written at all.
  /// </summary>
  private const int _MaxCodeLengthCodeLength = 5;

  /// <summary>Write a token with this code. A single-symbol code writes nothing.</summary>
  public void Write(JxlBitWriter writer, int symbol) {
    if (_silent)
      return;

    var length = _lengths[symbol];
    var code = _codes[symbol];
    // Prefix codes are the one field the format states most significant bit
    // first; everything else in the codestream goes the other way round.
    for (var i = length - 1; i >= 0; --i)
      writer.WriteBits((uint)((code >> i) & 1), 1);
  }

  /// <summary>
  /// Build the cheapest code for the given symbol counts and write its header.
  /// </summary>
  /// <param name="writer">Where the header goes.</param>
  /// <param name="counts">How often each symbol of the alphabet occurs.</param>
  public static JxlPrefixCode Build(JxlBitWriter writer, int[] counts) {
    ArgumentNullException.ThrowIfNull(writer);
    ArgumentNullException.ThrowIfNull(counts);
    if (counts.Length == 0)
      throw new ArgumentOutOfRangeException(nameof(counts), "A prefix code needs at least one symbol.");

    // An alphabet of one is stated by its size alone; the reader takes no bits
    // for it and neither does the writer.
    if (counts.Length == 1)
      return new JxlPrefixCode { _silent = true };

    var used = new List<int>();
    for (var symbol = 0; symbol < counts.Length; ++symbol)
      if (counts[symbol] > 0)
        used.Add(symbol);
    if (used.Count == 0)
      used.Add(0);

    // Most frequent first, so the short form hands the shortest code to the
    // symbol that pays for it most often.
    used.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));

    return used.Count <= 4
      ? _WriteSimple(writer, counts, used)
      : _WriteComplex(writer, counts);
  }

  /// <summary>
  /// The short form: up to four symbols stated outright, their lengths implied
  /// by how many there are.
  /// </summary>
  private static JxlPrefixCode _WriteSimple(JxlBitWriter writer, int[] counts, List<int> used) {
    var alphabetSize = counts.Length;
    var indexBits = _FloorLog2((uint)(alphabetSize - 1)) + 1;

    writer.WriteBits(1, 2); // simple_code_or_skip = 1
    writer.WriteBits((uint)(used.Count - 1), 2);
    foreach (var symbol in used)
      writer.WriteBits((uint)symbol, indexBits);

    var lengths = new int[alphabetSize];
    switch (used.Count) {
      case 1:
        // Every length stays zero: the reader answers with this symbol and
        // takes no bits for it.
        return new JxlPrefixCode { _silent = true };
      case 2:
        lengths[used[0]] = 1;
        lengths[used[1]] = 1;
        break;
      case 3:
        lengths[used[0]] = 1;
        lengths[used[1]] = 2;
        lengths[used[2]] = 2;
        break;
      default: {
        // Four symbols come as either a flat pair of pairs or a skewed tree;
        // whichever costs fewer bits over the actual counts wins.
        var flat = 2L * (counts[used[0]] + counts[used[1]] + counts[used[2]] + counts[used[3]]);
        var skewed = counts[used[0]] + 2L * counts[used[1]] + 3L * (counts[used[2]] + counts[used[3]]);
        var skew = skewed < flat;
        writer.WriteBool(skew);
        lengths[used[0]] = skew ? 1 : 2;
        lengths[used[1]] = 2;
        lengths[used[2]] = skew ? 3 : 2;
        lengths[used[3]] = skew ? 3 : 2;
        break;
      }
    }

    return new JxlPrefixCode { _lengths = lengths, _codes = _CanonicalCodes(lengths) };
  }

  /// <summary>
  /// The long form: a code over the code lengths themselves, then one length
  /// per symbol up to the last one the code actually uses.
  /// </summary>
  private static JxlPrefixCode _WriteComplex(JxlBitWriter writer, int[] counts) {
    var lengths = _CodeLengths(counts, _MaxCodeLength);

    // Lengths past the last non-zero one are never stated: the reader stops as
    // soon as the weights complete the code, and a trailing zero is not weight.
    var last = lengths.Length - 1;
    while (last > 0 && lengths[last] == 0)
      --last;

    var lengthCounts = new int[18];
    for (var symbol = 0; symbol <= last; ++symbol)
      ++lengthCounts[lengths[symbol]];

    var lengthCodeLengths = _CodeLengths(lengthCounts, _MaxCodeLengthCodeLength);
    _EnsureTwoCodes(lengthCodeLengths);
    var lengthCodes = _CanonicalCodes(lengthCodeLengths);

    writer.WriteBits(0, 2); // simple_code_or_skip = 0, i.e. skip nothing

    var space = 32;
    foreach (var symbol in _CodeLengthCodeOrder) {
      var length = lengthCodeLengths[symbol];
      _WriteCodeLengthCodeLength(writer, length);
      if (length == 0)
        continue;
      space -= 32 >> length;
      if (space <= 0)
        break;
    }

    for (var symbol = 0; symbol <= last; ++symbol) {
      var value = lengths[symbol];
      for (var i = lengthCodeLengths[value] - 1; i >= 0; --i)
        writer.WriteBits((uint)((lengthCodes[value] >> i) & 1), 1);
    }

    return new JxlPrefixCode { _lengths = lengths, _codes = _CanonicalCodes(lengths) };
  }

  /// <summary>
  /// Make sure the code over the code lengths has two codes in it, which is what
  /// makes its weights add up to a whole and lets the reader stop where the
  /// writer did.
  /// </summary>
  /// <remarks>
  /// A picture whose every symbol happens to want the same code length would
  /// otherwise state a single one-length code, whose weight is half a code and
  /// never completes; the reader would then keep taking entries the writer never
  /// wrote. A second code nothing ever emits costs one bit and settles it.
  /// </remarks>
  private static void _EnsureTwoCodes(int[] lengthCodeLengths) {
    var nonZero = 0;
    foreach (var length in lengthCodeLengths)
      if (length != 0)
        ++nonZero;
    if (nonZero >= 2)
      return;

    // The one code that exists becomes a single bit, and a symbol nothing ever
    // emits takes the other bit.
    for (var symbol = 0; symbol < lengthCodeLengths.Length; ++symbol)
      if (lengthCodeLengths[symbol] != 0)
        lengthCodeLengths[symbol] = 1;

    var needed = 2 - nonZero;
    for (var symbol = 0; symbol < lengthCodeLengths.Length && needed > 0; ++symbol)
      if (lengthCodeLengths[symbol] == 0) {
        lengthCodeLengths[symbol] = 1;
        --needed;
      }
  }

  /// <summary>
  /// The static code the reader uses for a code-length code length (libjxl
  /// <c>kHuffmanBitLengthPrefixCode</c>). It states only zero through five.
  /// </summary>
  private static void _WriteCodeLengthCodeLength(JxlBitWriter writer, int value) {
    switch (value) {
      case 0: writer.WriteBits(0, 1); writer.WriteBits(0, 1); break;
      case 1: writer.WriteBits(1, 1); writer.WriteBits(1, 1); writer.WriteBits(1, 1); writer.WriteBits(0, 1); break;
      case 2: writer.WriteBits(1, 1); writer.WriteBits(1, 1); writer.WriteBits(0, 1); break;
      case 3: writer.WriteBits(0, 1); writer.WriteBits(1, 1); break;
      case 4: writer.WriteBits(1, 1); writer.WriteBits(0, 1); break;
      case 5: writer.WriteBits(1, 1); writer.WriteBits(1, 1); writer.WriteBits(1, 1); writer.WriteBits(1, 1); break;
      default:
        throw new ArgumentOutOfRangeException(nameof(value), $"A code-length code length of {value} cannot be stated.");
    }
  }

  /// <summary>
  /// Canonical codes from bit lengths, assigned the way the reader rebuilds
  /// them: shortest length first and, within a length, in symbol order.
  /// </summary>
  private static int[] _CanonicalCodes(int[] lengths) {
    var count = new int[_MaxCodeLength + 1];
    foreach (var length in lengths)
      if (length > 0)
        ++count[length];

    var next = new int[_MaxCodeLength + 2];
    var code = 0;
    for (var length = 1; length <= _MaxCodeLength; ++length) {
      next[length] = code;
      code = (code + count[length]) << 1;
    }

    var codes = new int[lengths.Length];
    for (var symbol = 0; symbol < lengths.Length; ++symbol)
      if (lengths[symbol] > 0)
        codes[symbol] = next[lengths[symbol]]++;
    return codes;
  }

  /// <summary>
  /// Bit lengths for the given counts, none longer than <paramref name="maxLength"/>.
  /// </summary>
  /// <remarks>
  /// A plain Huffman code can run deeper than the format allows on a very skewed
  /// histogram. Halving the counts flattens the tree without changing which
  /// symbols are the common ones, and repeating that terminates: once every
  /// count is one the tree is balanced and its depth is the base-two logarithm
  /// of the alphabet, which for the alphabets used here is well inside the
  /// limit.
  /// </remarks>
  private static int[] _CodeLengths(int[] counts, int maxLength) {
    var weights = (int[])counts.Clone();
    for (var attempt = 0; attempt < 64; ++attempt) {
      var lengths = _Huffman(weights);
      var deepest = 0;
      foreach (var length in lengths)
        if (length > deepest)
          deepest = length;
      if (deepest <= maxLength)
        return lengths;

      for (var symbol = 0; symbol < weights.Length; ++symbol)
        if (weights[symbol] > 1)
          weights[symbol] = weights[symbol] + 1 >> 1;
    }

    throw new InvalidOperationException($"No prefix code of depth {maxLength} or less fits this histogram.");
  }

  /// <summary>Plain Huffman bit lengths; symbols with no occurrences get zero.</summary>
  private static int[] _Huffman(int[] weights) {
    var lengths = new int[weights.Length];
    var leaves = new List<int>();
    for (var symbol = 0; symbol < weights.Length; ++symbol)
      if (weights[symbol] > 0)
        leaves.Add(symbol);

    if (leaves.Count == 0)
      return lengths;
    if (leaves.Count == 1) {
      lengths[leaves[0]] = 1;
      return lengths;
    }

    var nodeCount = leaves.Count * 2 - 1;
    var weight = new long[nodeCount];
    var parent = new int[nodeCount];
    var active = new bool[nodeCount];
    for (var i = 0; i < leaves.Count; ++i) {
      weight[i] = weights[leaves[i]];
      parent[i] = -1;
      active[i] = true;
    }

    var next = leaves.Count;
    while (true) {
      var first = -1;
      var second = -1;
      for (var i = 0; i < next; ++i) {
        if (!active[i])
          continue;
        if (first < 0 || weight[i] < weight[first]) {
          second = first;
          first = i;
        } else if (second < 0 || weight[i] < weight[second])
          second = i;
      }
      if (second < 0)
        break;

      weight[next] = weight[first] + weight[second];
      parent[next] = -1;
      active[next] = true;
      parent[first] = next;
      parent[second] = next;
      active[first] = false;
      active[second] = false;
      ++next;
    }

    for (var i = 0; i < leaves.Count; ++i) {
      var depth = 0;
      for (var node = i; parent[node] >= 0; node = parent[node])
        ++depth;
      lengths[leaves[i]] = depth;
    }
    return lengths;
  }

  /// <summary>libjxl <c>FloorLog2Nonzero</c>: the index of the highest set bit.</summary>
  private static int _FloorLog2(uint value) {
    var bits = 0;
    while (value > 1) {
      ++bits;
      value >>= 1;
    }
    return bits;
  }
}
