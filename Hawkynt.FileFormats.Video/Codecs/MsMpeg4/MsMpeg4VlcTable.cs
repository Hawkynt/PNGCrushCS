using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// One of Microsoft's variable-length code tables, held as the binary tree its codewords describe.
/// </summary>
/// <remarks>
/// A tree rather than the flat lookup the MPEG-4 decoder beside this one uses, because these tables
/// are the wrong shape for a flat one: a motion vector table pairs a single codeword with a whole
/// vector over eleven hundred entries and reaches seventeen bits, and the predicted-picture macroblock
/// table reaches twenty-one, so a lookup indexed by the longest codeword would be two million entries
/// to hold a hundred and twenty-eight values. The tree is one node per codeword prefix and is walked a
/// bit at a time over bits peeked in one go, so the reader is moved once per codeword rather than once
/// per bit.
/// <para/>
/// Two ways in, because Microsoft's tables arrive both ways. Most are written out as a codeword and
/// its length; the motion vector tables are written out as lengths alone, with the codewords implied
/// by assigning them in order, shortest first — which is the canonical assignment, and stating a table
/// that way is possible only because it is complete.
/// </remarks>
internal sealed class MsMpeg4VlcTable {

  /// <summary>Two entries per node: where bit nought and bit one lead, or nought for nowhere.</summary>
  private readonly int[] _children;

  /// <summary>The value a node stands for, or minus one where the node is only a prefix.</summary>
  private readonly int[] _values;

  private readonly int _maxLength;
  private readonly string _name;

  private MsMpeg4VlcTable(string name, int[] children, int[] values, int maxLength) {
    this._name = name;
    this._children = children;
    this._values = values;
    this._maxLength = maxLength;
  }

  /// <summary>The longest codeword in the table, which is how far ahead a read has to look.</summary>
  internal int MaxLength => this._maxLength;

  /// <summary>Builds a table from a codeword and a length per value.</summary>
  /// <param name="name">What to call the table in a refusal.</param>
  /// <param name="codes">The codeword of each value, right-aligned in its length.</param>
  /// <param name="lengths">How many bits each codeword occupies; nought means the value is not coded.</param>
  /// <param name="values">What each entry stands for, or <c>null</c> where the entry's index is the value.</param>
  internal static MsMpeg4VlcTable FromCodes(string name, int[] codes, int[] lengths, int[]? values = null) {
    ArgumentNullException.ThrowIfNull(codes);
    ArgumentNullException.ThrowIfNull(lengths);

    var builder = new _Builder(name, codes.Length);
    for (var i = 0; i < codes.Length; ++i)
      if (lengths[i] > 0)
        builder.Add(codes[i], lengths[i], values == null ? i : values[i]);

    return builder.Build();
  }

  /// <summary>
  /// Builds a table from lengths alone, assigning the codewords in the order the lengths are given.
  /// </summary>
  /// <remarks>
  /// The assignment is the one a canonical code makes: a counter of the codeword space consumed so
  /// far, read off at each entry's length and advanced by what that length costs. It reproduces the
  /// codewords only while the table is complete — Kraft's sum exactly one — which is checked here
  /// rather than assumed, because a table that overruns would silently give every entry after the
  /// overrun a codeword that means something else.
  /// </remarks>
  internal static MsMpeg4VlcTable FromLengths(string name, int[] lengths, int[] values) {
    ArgumentNullException.ThrowIfNull(lengths);
    ArgumentNullException.ThrowIfNull(values);

    var builder = new _Builder(name, lengths.Length);
    var consumed = 0L;
    for (var i = 0; i < lengths.Length; ++i) {
      var length = lengths[i];
      if (length <= 0)
        continue;

      if (consumed >= 1L << 32)
        throw new InvalidOperationException(
          $"{name}: the codeword space is exhausted at entry {i}, so the table's lengths do not describe a code.");

      builder.Add((int)(uint)(consumed >> (32 - length)), length, values[i]);
      consumed += 1L << (32 - length);
    }

    return builder.Build();
  }

  /// <summary>Reads one codeword and returns the value it stands for.</summary>
  /// <exception cref="InvalidDataException">The next bits are not a codeword of this table.</exception>
  internal int Read(ref Mpeg4BitReader reader) {
    // Peeked in one go and past the end of the data padded with zeroes, because the last codeword of a
    // picture is shorter than the longest in its table and a look at the longest always overruns.
    var bits = reader.NextBits(this._maxLength);
    var node = 0;

    for (var length = 1; length <= this._maxLength; ++length) {
      var bit = (bits >> (this._maxLength - length)) & 1;
      node = this._children[2 * node + bit];
      if (node == 0)
        break;

      var value = this._values[node];
      if (value < 0)
        continue;

      reader.Skip(length);
      return value;
    }

    throw new InvalidDataException(
      $"Bit {reader.BitPosition} of this Microsoft MPEG-4 picture holds "
      + $"{Convert.ToString(bits, 2).PadLeft(this._maxLength, '0')}, which begins with no codeword of {this._name}.");
  }

  /// <summary>Grows the tree one codeword at a time; the arrays it fills are what the table reads.</summary>
  private sealed class _Builder {

    private readonly string _name;
    private int[] _children;
    private int[] _values;
    private int _nodes = 1;
    private int _maxLength;

    internal _Builder(string name, int capacity) {
      this._name = name;

      // Two nodes per codeword covers the worst case of a tree with no shared prefixes at all; it is
      // grown rather than fixed because a length-only table's entries may be skipped.
      var size = Math.Max(64, 2 * capacity + 2);
      this._children = new int[2 * size];
      this._values = new int[size];
      Array.Fill(this._values, -1);
    }

    internal void Add(int code, int length, int value) {
      if (length > 31)
        throw new InvalidOperationException($"{this._name}: a codeword of {length} bits is longer than this reader looks.");

      this._maxLength = Math.Max(this._maxLength, length);

      var node = 0;
      for (var i = length - 1; i >= 0; --i) {
        if (this._values[node] >= 0)
          throw new InvalidOperationException(
            $"{this._name}: the codeword for {value} continues past the codeword for {this._values[node]}, so the "
            + "table is not a prefix code.");

        var bit = (code >> i) & 1;
        var next = this._children[2 * node + bit];
        if (next == 0) {
          if (this._nodes == this._values.Length)
            this._Grow();

          next = this._nodes++;
          this._children[2 * node + bit] = next;
        }

        node = next;
      }

      if (this._values[node] >= 0 || this._children[2 * node] != 0 || this._children[2 * node + 1] != 0)
        throw new InvalidOperationException(
          $"{this._name}: the codeword for {value} collides with one already in the table.");

      this._values[node] = value;
    }

    internal MsMpeg4VlcTable Build() => new(this._name, this._children, this._values, this._maxLength);

    private void _Grow() {
      var size = 2 * this._values.Length;
      Array.Resize(ref this._children, 2 * size);
      var values = this._values;
      Array.Resize(ref values, size);
      Array.Fill(values, -1, this._nodes, size - this._nodes);
      this._values = values;
    }
  }
}
