using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// The boolean entropy decoder every bit of a VP8 frame comes out of (RFC 6386, 7).
/// </summary>
/// <remarks>
/// A binary arithmetic coder. The whole partition is read as the binary expansion of one number
/// <c>x</c> between zero and one; each decoded bool narrows the interval <c>x</c> is known to lie in,
/// in proportion to the probability it was written with. Because the interval is renormalised back
/// above 128 after every read, the state fits in two machine words and a bit counter, and reading a
/// bool costs one multiply and a compare.
/// <para/>
/// The probability given to <see cref="ReadBool"/> is the chance of a zero, out of 256. It has to be
/// the same number the encoder used, which is why so much of the rest of this decoder is bookkeeping
/// for probability tables: get one of them wrong by one and every bool after it in that partition is
/// noise.
/// <para/>
/// Reading past the end of the partition is not an error, and neither is a partition too short to
/// prime the window. A frame in which every macroblock declares itself free of coefficients has
/// nothing to write into its token partitions, and encoders duly write one byte into them, or none;
/// the reference decoder treats the bytes it does not have as an endless supply of zero bits, and so
/// does this. Truncation is caught where it can be told apart from thrift — at the frame header,
/// where the declared partition sizes have to fit in the packet that arrived.
/// <para/>
/// A plain struct rather than a <c>ref struct</c>, because a frame carries up to eight token
/// partitions at once and the macroblock rows take them in turn — which needs them all to be alive
/// together, in an array.
/// </remarks>
internal struct Vp8BoolDecoder {

  private readonly byte[] _data;
  private readonly int _end;
  private int _position;
  private uint _range;
  private uint _value;
  private int _bitCount;

  /// <summary>Opens a partition, priming the window with its first two bytes or with zeroes for the ones it lacks.</summary>
  internal Vp8BoolDecoder(byte[] data, int start, int length) {
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

  /// <summary>Reads a bit written at even odds, which is what the frame header is mostly made of.</summary>
  internal int ReadFlag() => this.ReadBool(128);

  /// <summary>Reads an unsigned value of <paramref name="bits"/> bits, high-order bit first, each at even odds.</summary>
  internal int ReadLiteral(int bits) {
    var value = 0;
    while (bits-- > 0)
      value = (value << 1) | this.ReadBool(128);

    return value;
  }

  /// <summary>
  /// Reads a magnitude followed by a sign bit, which is how every delta in the frame header is written.
  /// </summary>
  /// <remarks>
  /// Sign after magnitude and not before, and a sign of one meaning negative — the opposite of two's
  /// complement. Both spellings are in RFC 6386 and mixing them up costs a delta its sign on roughly
  /// half the frames that carry one.
  /// </remarks>
  internal int ReadSignedValue(int bits) {
    var magnitude = this.ReadLiteral(bits);
    return this.ReadBool(128) != 0 ? -magnitude : magnitude;
  }

  /// <summary>
  /// Walks a coding tree from a chosen node until it reaches a leaf, and answers the leaf's value.
  /// </summary>
  /// <param name="tree">
  /// The tree as RFC 6386 section 8.1 lays one out: pairs of entries, a positive entry being the index
  /// of a deeper pair and a non-positive entry being the negated value of a leaf.
  /// </param>
  /// <param name="probabilities">One probability per interior node, indexed by half the node's position.</param>
  /// <param name="probabilityOffset">Where in <paramref name="probabilities"/> this tree's probabilities start.</param>
  /// <param name="start">The node to begin at, which is the root except where a branch is known not to be taken.</param>
  internal int ReadTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int probabilityOffset, int start = 0) {
    var node = start;
    do
      node = tree[node + this.ReadBool(probabilities[probabilityOffset + (node >> 1)])];
    while (node > 0);

    return -node;
  }
}
