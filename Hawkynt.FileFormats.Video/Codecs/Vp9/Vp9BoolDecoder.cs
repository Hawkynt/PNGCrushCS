using System;
using System.IO;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// The boolean entropy decoder the compressed header and every tile is read through
/// (specification 9.2).
/// </summary>
/// <remarks>
/// The same binary arithmetic coder VP8 uses, with the same split arithmetic and the same
/// renormalisation, and it is written here the same way: a sixteen-bit window holding the
/// specification's <c>BoolValue</c> in its high byte and the next eight bits of lookahead in its low
/// byte. Comparing the window against <c>split &lt;&lt; 8</c> answers exactly the question
/// specification 9.2.2 asks of <c>BoolValue</c> against <c>split</c>, because the low byte can never
/// close a gap of a whole unit in the high one — and it saves refilling the window a bit at a time.
/// <para/>
/// Priming with two bytes rather than one is the same identity read the other way. Specification
/// 9.2.1 reads one byte into <c>BoolValue</c> and leaves the rest in the stream; this reads that byte
/// and the one behind it, which is where the first renormalisation would have found it.
/// <para/>
/// The marker bit that 9.2.1 requires is read by the caller, because the value it must have — zero —
/// is a statement about the stream and belongs where the stream is being judged.
/// <para/>
/// Reading past the end yields zero bits rather than throwing. The specification calls that a
/// conformance failure, but a decoder cannot tell it apart from an encoder that stopped writing once
/// every remaining bool was determined, and the sizes that matter — the compressed header's and each
/// tile's — are checked against the packet before any of this is reached.
/// <para/>
/// A struct rather than a <c>ref struct</c> because a frame with several tiles needs several of these
/// alive at once, in an array.
/// </remarks>
internal struct Vp9BoolDecoder {

  private readonly byte[] _data;
  private readonly int _end;
  private int _position;
  private uint _range;
  private uint _value;
  private int _bitCount;

  internal Vp9BoolDecoder(byte[] data, int start, int length) {
    if (length < 1)
      throw new InvalidDataException(
        "A VP9 arithmetic-coded partition is at least one byte long; this one states zero. Specification 9.2.1 "
        + "forbids a bitstream that asks for a shorter one.");

    var end = start + length;
    this._data = data;
    this._end = end;
    this._value = ((start < end ? (uint)data[start] : 0u) << 8) | (start + 1 < end ? data[start + 1] : 0u);
    this._position = start + 2;
    this._range = 255;
    this._bitCount = 0;
  }

  /// <summary>Reads one bool written with <paramref name="probability"/>/256 of being zero.</summary>
  internal int ReadBool(int probability) {
    var split = 1 + (((this._range - 1) * (uint)probability) >> 8);
    var bigSplit = split << 8;

    int result;
    if (this._value >= bigSplit) {
      result = 1;
      this._range -= split;
      this._value -= bigSplit;
    } else {
      result = 0;
      this._range = split;
    }

    while (this._range < 128) {
      this._value <<= 1;
      this._range <<= 1;
      if (++this._bitCount != 8)
        continue;

      this._bitCount = 0;
      this._value |= this._position < this._end ? this._data[this._position] : 0u;
      ++this._position;
    }

    return result;
  }

  /// <summary>Reads <c>L(n)</c>: an unsigned value of <paramref name="bits"/> bits, each at even odds (specification 9.2.4).</summary>
  internal int ReadLiteral(int bits) {
    var value = 0;
    while (bits-- > 0)
      value = (value << 1) | this.ReadBool(128);

    return value;
  }

  /// <summary>Reads the zero bit specification 9.2.1 requires at the head of every partition.</summary>
  internal void ReadMarker() {
    if (this.ReadBool(128) != 0)
      throw new InvalidDataException(
        "A VP9 arithmetic-coded partition begins with a marker bit that specification 9.2.1 requires to be zero, and "
        + "this one is not. Either the partition boundaries were computed wrongly or the packet is not VP9.");
  }

  /// <summary>
  /// Walks a coding tree from the root until it reaches a leaf, taking each interior node's
  /// probability from <paramref name="probabilities"/> (specification 9.3.3).
  /// </summary>
  /// <param name="tree">
  /// Pairs of entries, a positive entry being the index of a deeper pair and a non-positive entry the
  /// negated value of a leaf.
  /// </param>
  /// <param name="probabilities">One probability per interior node, indexed by half the node's position.</param>
  internal int ReadTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities) {
    var node = 0;
    do
      node = tree[node + this.ReadBool(probabilities[node >> 1])];
    while (node > 0);

    return -node;
  }
}
