using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// One of the eighty Huffman codes that carry Theora's DCT tokens.
/// </summary>
/// <remarks>
/// Stored in the setup header as a binary tree rather than as a list of code lengths, which is
/// unusual and deliberate: a tree written out this way is necessarily full and necessarily
/// prefix-free, so no sequence of bits can fail to decode and no code can be a prefix of another.
/// Theora specification section 6.4.4.
/// <para/>
/// The tree is kept here as the tree the header describes, walked one bit at a time, rather than
/// flattened into a lookup table. A table would have to be built from code lengths this format never
/// states, and it could not express what this one legitimately can: a tree may assign several codes
/// to one token, and may omit tokens entirely.
/// </remarks>
internal sealed class TheoraHuffmanTable {

  /// <summary>The most entries one table may hold — section 6.4.4.</summary>
  private const int _MAX_ENTRIES = 32;

  /// <summary>
  /// The longest code the format permits, which follows from a full tree of at most 32 leaves.
  /// </summary>
  /// <remarks>
  /// Enforced while reading rather than left to the recursion, because a malformed header describing
  /// an unboundedly deep tree is otherwise a stack overflow — the specification says so in as many
  /// words, and says that a decoder should say so too.
  /// </remarks>
  private const int _MAX_CODE_LENGTH = 32;

  /// <summary>
  /// Two slots a node: the entry taken on a zero bit, then the one taken on a one.
  /// </summary>
  /// <remarks>
  /// A non-negative entry is another node's number, whose slots are at twice it; a negative one is
  /// <c>-1 - token</c>. Held flat rather than as objects because eighty of these are built for every
  /// stream and walked once per token for every coefficient of every block of every frame.
  /// </remarks>
  private readonly List<int> _nodes = [];

  private int _root;
  private int _entries;

  private TheoraHuffmanTable() { }

  /// <summary>Reads one table's tree out of the setup header.</summary>
  internal static TheoraHuffmanTable Read(TheoraBitReader reader, int tableIndex) {
    var table = new TheoraHuffmanTable();
    var root = table._ReadNode(reader, 0, tableIndex);

    // A tree that is one leaf gives every code a length of zero, so decoding would return that token
    // for ever without consuming a bit. The format cannot express a useful table this way and no
    // encoder writes one; a header that does is refused rather than hung on.
    if (root < 0)
      throw new InvalidDataException(
        $"Huffman table {tableIndex} of the setup header is a single leaf, which codes its token in no bits at all.");

    table._root = root;
    return table;
  }

  /// <summary>
  /// Reads one node of the tree: a leaf and its token, or an interior node and both its sub-trees.
  /// </summary>
  /// <returns>The entry standing for this node — its number, or the complement of its token.</returns>
  private int _ReadNode(TheoraBitReader reader, int depth, int tableIndex) {
    if (depth > _MAX_CODE_LENGTH)
      throw new InvalidDataException(
        $"Huffman table {tableIndex} of the setup header describes a code longer than {_MAX_CODE_LENGTH} bits, which a full tree of at most {_MAX_ENTRIES} entries cannot have.");

    if (reader.ReadBit() == 1) {
      if (this._entries == _MAX_ENTRIES)
        throw new InvalidDataException(
          $"Huffman table {tableIndex} of the setup header holds more than {_MAX_ENTRIES} entries.");

      ++this._entries;
      return -1 - (int)reader.ReadBits(5);
    }

    // The node's slots are claimed before its children are read, so that the children's own nodes
    // are numbered after it and the tree comes out in the order the header wrote it.
    var node = this._nodes.Count / 2;
    this._nodes.AddRange([0, 0]);

    var zero = this._ReadNode(reader, depth + 1, tableIndex);
    var one = this._ReadNode(reader, depth + 1, tableIndex);
    this._nodes[node * 2] = zero;
    this._nodes[node * 2 + 1] = one;
    return node;
  }

  /// <summary>Reads one bit at a time until a code of this table is recognised.</summary>
  internal int ReadToken(TheoraBitReader reader) {
    var node = this._root;
    for (var depth = 0; depth <= _MAX_CODE_LENGTH; ++depth) {
      var entry = this._nodes[node * 2 + reader.ReadBit()];
      if (entry < 0)
        return -1 - entry;

      node = entry;
    }

    throw new InvalidDataException(
      $"A DCT token code ran past {_MAX_CODE_LENGTH} bits, which no valid Huffman table here can produce.");
  }
}
