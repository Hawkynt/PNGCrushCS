using System;
using System.IO;

namespace FileFormat.Codecs.Vp5;

/// <summary>One node of a VP5 entropy tree: where to go on a one, and which probability decides.</summary>
/// <remarks>
/// A positive <paramref name="Value"/> is the distance to jump when the bit reads one; walking to the
/// next node is what a zero does. A value of zero or less ends the walk and the symbol is its
/// negation, which is why zero is a terminator and not a jump of no distance.
/// </remarks>
internal readonly record struct Vp5TreeNode(sbyte Value, byte ProbabilityIndex);

/// <summary>
/// VP5's binary arithmetic decoder.
/// </summary>
/// <remarks>
/// The interval is held as a range in <c>0..255</c> and a code word carrying sixteen fractional bits
/// below it, refilled two bytes at a time. That is FFmpeg's arrangement rather than the byte-at-a-time
/// one On2's VP6 document describes, and it is the arrangement this decoder has to have: the two agree
/// on every decision a well-formed stream asks for, but they disagree on how much of a truncated one
/// they can still read, and the oracle these decoders are measured against is FFmpeg.
/// <para/>
/// <b>The split is <c>1 + (((range - 1) * probability) &gt;&gt; 8)</c>.</b> On2's VP6 specification
/// prints a shift of seven at §7.3 and is wrong: with a shift of seven, an even-odds bit at the
/// initial range of 255 puts the whole interval on one side and decodes as zero for all but one value
/// of the window. Eight halves the interval, which is what an even-odds bit must do.
/// <para/>
/// A probability is the chance of a zero out of 256. Past the end of the partition the code word takes
/// zeroes: the coder has to be able to run a little way beyond the last byte because the encoder's
/// flush is shorter than the window, and a stream that is genuinely truncated is caught by
/// <see cref="IsExhausted"/> rather than by an exception thrown in the middle of a block.
/// </remarks>
internal struct Vp5RangeDecoder {

  /// <summary>How far the range has to be shifted left to bring it back above 127.</summary>
  /// <remarks>Built rather than transcribed: it is the number of leading zeroes of an eight-bit
  /// value, which has one definition and no room for a typo.</remarks>
  private static readonly byte[] _NormalizationShift = _BuildNormalizationShift();

  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _end;
  private int _position;
  private int _range;
  private int _bits;
  private uint _codeWord;
  private int _pastTheEnd;

  /// <summary>Starts a coder over one partition of a coded frame.</summary>
  internal Vp5RangeDecoder(ReadOnlyMemory<byte> data, int start = 0, int? length = null) {
    var actualLength = length ?? data.Length - start;
    if ((uint)start > (uint)data.Length || actualLength < 0 || start + actualLength > data.Length)
      throw new InvalidDataException("A VP5 range-coder partition lies outside the coded packet.");
    if (actualLength < 1)
      throw new InvalidDataException("A VP5 range-coder partition is empty.");

    this._data = data;
    this._end = start + actualLength;
    this._position = start;
    this._range = 255;
    this._bits = -16;
    this._pastTheEnd = 0;
    this._codeWord = ((uint)this._NextByte() << 16) | ((uint)this._NextByte() << 8) | (uint)this._NextByte();
  }

  /// <summary>
  /// Whether the coder has been reading beyond the partition long enough that what it returns is no
  /// longer the stream's.
  /// </summary>
  /// <remarks>
  /// Ten refills past the end is FFmpeg's threshold and this keeps it, because moving it would make
  /// this decoder reject frames FFmpeg decodes or accept ones it rejects, and the oracle is FFmpeg.
  /// The slack exists because the encoder's flush is shorter than the coder's window, so a
  /// well-formed last block does read a few bytes that were never written.
  /// </remarks>
  internal bool IsExhausted {
    get {
      if (this._position >= this._end && this._bits >= 0)
        ++this._pastTheEnd;
      return this._pastTheEnd > 10;
    }
  }

  /// <summary>Reads one bit whose chance of being zero is <paramref name="probability"/> out of 256.</summary>
  internal int ReadBool(int probability) {
    var codeWord = this._Renormalize();
    var low = (uint)(1 + (((this._range - 1) * probability) >> 8));
    var lowShifted = low << 16;

    if (codeWord >= lowShifted) {
      this._range -= (int)low;
      this._codeWord = codeWord - lowShifted;
      return 1;
    }

    this._range = (int)low;
    this._codeWord = codeWord;
    return 0;
  }

  /// <summary>Reads one bit at even odds.</summary>
  /// <remarks>
  /// Written as its own halving rather than as <see cref="ReadBool"/> at 128 because that is how the
  /// format states it. The two agree for every range the coder can hold, which is worth knowing and
  /// not worth relying on.
  /// </remarks>
  internal int ReadFlag() {
    var codeWord = this._Renormalize();
    var low = (uint)((this._range + 1) >> 1);
    var lowShifted = low << 16;

    if (codeWord >= lowShifted) {
      this._range -= (int)low;
      this._codeWord = codeWord - lowShifted;
      return 1;
    }

    this._range = (int)low;
    this._codeWord = codeWord;
    return 0;
  }

  /// <summary>Reads an unsigned literal, most significant bit first, at even odds throughout.</summary>
  internal int ReadLiteral(int bits) {
    var value = 0;
    while (bits-- > 0)
      value = (value << 1) | this.ReadFlag();
    return value;
  }

  /// <summary>Reads a transmitted probability: seven bits doubled, with zero standing for one.</summary>
  /// <remarks>
  /// A transmitted VP5 probability is therefore always even except for the single value one, which is
  /// how the format spends its eighth bit on never letting a probability be zero.
  /// </remarks>
  internal byte ReadProbability() {
    var value = this.ReadLiteral(7) << 1;
    return (byte)(value == 0 ? 1 : value);
  }

  /// <summary>Walks an entropy tree to a symbol.</summary>
  internal int ReadTree(ReadOnlySpan<Vp5TreeNode> tree, ReadOnlySpan<byte> probabilities) {
    var index = 0;
    while (tree[index].Value > 0) {
      index += this.ReadBool(probabilities[tree[index].ProbabilityIndex]) != 0 ? tree[index].Value : 1;
      if ((uint)index >= (uint)tree.Length)
        throw new InvalidDataException("A VP5 entropy tree walked off its own table.");
    }

    return -tree[index].Value;
  }

  private uint _Renormalize() {
    var shift = _NormalizationShift[this._range];
    var codeWord = this._codeWord << shift;
    this._range <<= shift;
    this._bits += shift;

    if (this._bits < 0 || this._position >= this._end)
      return codeWord;

    codeWord |= (uint)((this._NextByte() << 8) | this._NextByte()) << this._bits;
    this._bits -= 16;
    return codeWord;
  }

  /// <summary>The next byte of the partition, or zero once it has run out.</summary>
  private int _NextByte() {
    var at = this._position++;
    return at < this._end ? this._data.Span[at] : 0;
  }

  private static byte[] _BuildNormalizationShift() {
    var table = new byte[256];
    for (var value = 0; value < 256; ++value) {
      var shift = 0;
      while (((value << shift) & 0x80) == 0 && shift < 8)
        ++shift;
      table[value] = (byte)shift;
    }

    return table;
  }
}
