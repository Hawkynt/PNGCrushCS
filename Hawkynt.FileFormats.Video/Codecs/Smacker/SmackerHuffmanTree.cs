using System;
using System.IO;

namespace FileFormat.Codecs.Smacker;

/// <summary>
/// An eight-bit Huffman tree, packed the way RAD's own description lays out under "Packed Huffman
/// Trees": a pre-order walk of flag bits — one for an internal node, whose two children follow, or zero
/// for a leaf, whose eight-bit value follows raw — read directly off the bitstream with no further
/// compression of its own.
/// </summary>
/// <remarks>
/// These are never one of the format's four real tables. They exist only as the pair of byte
/// sub-decoders a <see cref="SmackerSymbolTable"/> reads each of its own sixteen-bit values through,
/// low byte first and then high, and each of the two is optional: a sub-decoder the file declines to
/// send stands for a constant zero byte, and one that came out as a single leaf stands for that leaf's
/// own byte. Both of those decode without consuming a bit, which is the point of sending them that way.
/// </remarks>
internal sealed class SmackerByteTree {

  /// <summary>The deepest a code may be, which is FFmpeg's own limit for this tree and the depth its
  /// nine-bit lookup can still resolve in three steps.</summary>
  private const int _MAXIMUM_DEPTH = 27;

  private const int _MAXIMUM_LEAVES = 256;

  private readonly int[] _left;
  private readonly int[] _right;
  private readonly byte[] _value;
  private readonly bool[] _isLeaf;
  private readonly byte _constant;
  private readonly bool _isConstant;

  private SmackerByteTree(byte constant) {
    this._isConstant = true;
    this._constant = constant;
    this._left = [];
    this._right = [];
    this._value = [];
    this._isLeaf = [];
  }

  private SmackerByteTree(int[] left, int[] right, byte[] value, bool[] isLeaf) {
    this._left = left;
    this._right = right;
    this._value = value;
    this._isLeaf = isLeaf;
  }

  /// <summary>A sub-decoder that stands for one byte and reads nothing — either because the file said
  /// it sends no tree here, in which case the byte is zero, or because the tree it sent has a single
  /// leaf and so no branch to choose.</summary>
  internal static SmackerByteTree Constant(byte value) => new(value);

  /// <summary>Reads one sub-decoder: its own presence bit, then, when it is present, the packed tree
  /// and the one padding bit that follows it.</summary>
  internal static SmackerByteTree Read(ref SmackerBitReader reader) {
    if (reader.ReadBit() == 0)
      return Constant(0);

    var builder = new _Builder();
    builder.BuildNode(ref reader, 0);
    reader.SkipBit();

    return builder.ToTree();
  }

  /// <summary>Follows one code from the root down, a bit a branch, zero left and one right — the codes
  /// the packed structure itself states, most significant bit first.</summary>
  internal byte Decode(ref SmackerBitReader reader) {
    if (this._isConstant)
      return this._constant;

    var node = 0;
    while (!this._isLeaf[node])
      node = reader.ReadBit() == 0 ? this._left[node] : this._right[node];

    return this._value[node];
  }

  /// <summary>A tree of at most 256 leaves has at most 511 nodes, so the whole of one is allocated up
  /// front and nothing has to grow part way down a walk.</summary>
  private const int _MAXIMUM_NODES = 2 * _MAXIMUM_LEAVES - 1;

  private struct _Builder() {
    private readonly int[] _left = new int[_MAXIMUM_NODES];
    private readonly int[] _right = new int[_MAXIMUM_NODES];
    private readonly byte[] _value = new byte[_MAXIMUM_NODES];
    private readonly bool[] _isLeaf = new bool[_MAXIMUM_NODES];
    private int _count = 0;
    private int _leaves = 0;

    internal int BuildNode(ref SmackerBitReader reader, int depth) {
      if (depth > _MAXIMUM_DEPTH)
        throw new InvalidDataException(
          $"A Smacker byte tree nests more than {_MAXIMUM_DEPTH} levels deep, which is deeper than any "
          + "code this format can express.");

      if (this._count >= _MAXIMUM_NODES)
        throw new InvalidDataException(
          $"A Smacker byte tree states more than {_MAXIMUM_LEAVES} leaves, which is more distinct values "
          + "than a byte has.");

      var node = this._count++;
      if (reader.ReadBit() != 0) {
        this._left[node] = this.BuildNode(ref reader, depth + 1);
        this._right[node] = this.BuildNode(ref reader, depth + 1);
        return node;
      }

      if (++this._leaves > _MAXIMUM_LEAVES)
        throw new InvalidDataException(
          $"A Smacker byte tree states more than {_MAXIMUM_LEAVES} leaves, which is more distinct values "
          + "than a byte has.");

      this._isLeaf[node] = true;
      this._value[node] = reader.ReadByte();
      return node;
    }

    internal readonly SmackerByteTree ToTree()
      // A tree of one leaf is a constant: there is no branch to choose, so nothing is read for it.
      => this._count == 1
        ? Constant(this._value[0])
        : new(this._left, this._right, this._value, this._isLeaf);
  }
}

/// <summary>
/// One of Smacker's four sixteen-bit tables — <c>MMap</c>, <c>MClr</c>, <c>Full</c> or <c>Type</c> —
/// unpacked into the flat array of nodes and values its own move-to-front cache is defined over.
/// </summary>
/// <remarks>
/// <b>The composition this format's own description does not survive contact with.</b> RAD's
/// "Optimized Compression" section says a sixteen-bit table is built from two byte sub-decoders, three
/// marker values and a cache of three recent codes, and that much is true. What the prose leaves out is
/// what makes the section parse at all, and what makes a straight reading of it consume a percent or
/// two of a real file's tree section and then run into nonsense:
/// <list type="bullet">
///   <item>each of the two byte sub-decoders carries its own presence bit ahead of it and one padding
///     bit after it, so a reading that expects two bare trees back to back is out of step before the
///     first marker;</item>
///   <item>the three markers are sixteen raw bits each, not values read through the sub-decoders;</item>
///   <item>one more padding bit follows the sixteen-bit tree itself, before the next of the four
///     tables begins;</item>
///   <item>the tree is a flat array of <c>(Size + 3) / 4</c> slots, not the <c>Size / 8</c> the
///     header's own byte counts invite.</item>
/// </list>
/// Those four are why this codec sat undecoded here: every reading of the published prose alone leaves
/// the great majority of a real file's declared tree section unaccounted for. They are taken from
/// FFmpeg's <c>libavcodec/smacker.c</c> — see the notice beside this file — which is the only place
/// they are written down.
/// <para/>
/// <b>The cache is three positions in the array, not three values beside it.</b> A marker met while
/// unpacking does not become a leaf holding that marker: it becomes a leaf holding zero, and its
/// position is remembered as one of the table's three cache slots. Decoding a symbol then rewrites
/// those three slots in place — the value just decoded into the first, and each previous occupant one
/// slot along — so the three most recently distinct values sit at whatever short codes those leaves
/// happen to have. Only the <i>last</i> leaf matching a given marker is remembered: an earlier one
/// keeps the zero it was given and never moves again. Every table's three slots are reset to zero at
/// the start of every frame, and a marker that never appeared in the tree at all is given a slot of its
/// own past the tree's last real node, which is why the array is allocated three longer than the
/// header's own count.
/// </remarks>
internal sealed class SmackerSymbolTable {

  /// <summary>Set on an array slot that is an internal node rather than a value; the rest of the slot
  /// is then how far ahead of the node's own slot its one-branch subtree starts.</summary>
  private const int _NODE = unchecked((int)0x80000000);

  private const int _MAXIMUM_DEPTH = 500;

  private readonly int[] _slots;
  private readonly int[] _cache = new int[3];

  private SmackerSymbolTable(int[] slots, int[] cache, bool isAbsent) {
    this._slots = slots;
    this.IsAbsent = isAbsent;
    cache.CopyTo(this._cache, 0);
  }

  /// <summary>Whether the file stated it sends no tree for this table at all, in which case every
  /// symbol read from it is zero and none of them costs a bit.</summary>
  internal bool IsAbsent { get; }

  /// <summary>A table the file states it does not send, which decodes as a constant zero and reads no
  /// bits at all.</summary>
  private static SmackerSymbolTable _Absent() => new([0, 0], [1, 1, 1], true);

  /// <summary>Reads one of the four tables: its own presence bit, and then, when it is present, the
  /// two byte sub-decoders, the three markers and the tree built out of them.</summary>
  /// <param name="reader">The tree section's bitstream, positioned at this table's presence bit.</param>
  /// <param name="allocationSize">The table's own size field from the file header, which states how
  /// much memory RAD's decoder set aside for it and so, at four bytes a slot, how many slots the tree
  /// may hold.</param>
  internal static SmackerSymbolTable Read(ref SmackerBitReader reader, uint allocationSize) {
    if (reader.ReadBit() == 0)
      return _Absent();

    var low = SmackerByteTree.Read(ref reader);
    var high = SmackerByteTree.Read(ref reader);

    var escapes = new int[3];
    for (var i = 0; i < 3; ++i)
      escapes[i] = (int)reader.ReadBits(16);

    if (allocationSize >= uint.MaxValue >> 4)
      throw new InvalidDataException(
        $"A Smacker header states a {allocationSize}-byte table, which is more memory than the format's "
        + "own size field can mean.");

    var capacity = (int)((allocationSize + 3) >> 2);
    var builder = new _Builder(new int[capacity + 3], low, high, escapes);
    builder.BuildNode(ref reader, 0);
    reader.SkipBit();

    return builder.ToTable();
  }

  /// <summary>Empties the three-entry cache, which every table's own does at the start of every
  /// frame.</summary>
  internal void ResetHistory() {
    var slots = this._slots;
    slots[this._cache[0]] = 0;
    slots[this._cache[1]] = 0;
    slots[this._cache[2]] = 0;
  }

  /// <summary>Follows one code from the root down and then moves what it decoded to the front of the
  /// three-entry cache, unless it is what the cache already held first.</summary>
  internal int Decode(ref SmackerBitReader reader) {
    var slots = this._slots;
    var at = 0;
    while ((slots[at] & _NODE) != 0) {
      if (reader.BitsRemaining < 1)
        throw new InvalidDataException(
          "A Smacker picture ran out of bits part way down a Huffman code, so the frame it states cannot "
          + "be completed.");

      if (reader.ReadBit() != 0)
        at += slots[at] & ~_NODE;

      ++at;
    }

    var value = slots[at];
    if (value == slots[this._cache[0]])
      return value;

    slots[this._cache[2]] = slots[this._cache[1]];
    slots[this._cache[1]] = slots[this._cache[0]];
    slots[this._cache[0]] = value;
    return value;
  }

  private struct _Builder(int[] slots, SmackerByteTree low, SmackerByteTree high, int[] escapes) {
    private readonly int[] _slots = slots;
    private readonly SmackerByteTree _low = low;
    private readonly SmackerByteTree _high = high;
    private readonly int[] _escapes = escapes;
    private readonly int[] _cache = [-1, -1, -1];
    private int _count = 0;

    /// <summary>Unpacks one subtree and answers how many slots it took, which is what an internal node
    /// stores so that decoding can step over its zero-branch to reach its one-branch.</summary>
    internal int BuildNode(ref SmackerBitReader reader, int depth) {
      if (depth > _MAXIMUM_DEPTH)
        throw new InvalidDataException(
          $"A Smacker sixteen-bit tree nests more than {_MAXIMUM_DEPTH} levels deep.");

      if (this._count >= this._slots.Length - 3)
        throw new InvalidDataException(
          "A Smacker sixteen-bit tree holds more nodes than the size its own file header states for it.");

      if (reader.BitsRemaining <= 0)
        throw new InvalidDataException(
          "A Smacker tree section ran out of bits before its four tables were all unpacked.");

      if (reader.ReadBit() != 0) {
        var node = this._count++;
        var zeroBranchLength = this.BuildNode(ref reader, depth + 1);
        this._slots[node] = _NODE | zeroBranchLength;
        return 1 + zeroBranchLength + this.BuildNode(ref reader, depth + 1);
      }

      var value = this._low.Decode(ref reader) | (this._high.Decode(ref reader) << 8);

      // A leaf whose value is one of the three markers is not that value: it is a cache slot, holding
      // zero until the first symbol decoded through this table moves something into it.
      for (var i = 0; i < 3; ++i)
        if (value == this._escapes[i]) {
          this._cache[i] = this._count;
          value = 0;
          break;
        }

      this._slots[this._count++] = value;
      return 1;
    }

    internal SmackerSymbolTable ToTable() {
      // A marker the tree never used still needs a slot to be moved to the front of, so it gets one
      // past the last real node — which is what the three spare slots at the end of the array are for.
      for (var i = 0; i < 3; ++i)
        if (this._cache[i] < 0)
          this._cache[i] = this._count++;

      return new(this._slots, this._cache, false);
    }
  }
}
