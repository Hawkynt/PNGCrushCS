using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Reads an H.264 raw byte sequence payload: bits most significant first, plus the two variable
/// length codings the syntax is written in (ITU-T H.264, clause 9.1).
/// </summary>
/// <remarks>
/// The bytes handed to this have already had their emulation prevention removed
/// (<see cref="H264NalUnit"/>), so there is nothing between the bits and the bytes here and a bit is
/// a shift. That separation is deliberate: unescaping is a property of the NAL unit and escaping
/// inside a syntax element would make every field's length depend on its own contents.
/// <para/>
/// <see cref="MoreRbspData"/> is why the stop bit is found once at construction rather than searched
/// for on each call. An RBSP ends with a single one bit followed by zeroes to the byte boundary and
/// then any number of trailing zero bytes (clause 7.4.1); slice data is terminated by running out of
/// data rather than by a count, so every macroblock loop asks this question and it has to be cheap.
/// </remarks>
internal ref struct H264BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private readonly int _stopBitPosition;
  private int _bitPosition;

  public H264BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitPosition = 0;
    this._stopBitPosition = _FindStopBit(data);
  }

  /// <summary>The bit the next read will take, counted from the first bit of the first byte.</summary>
  public readonly int BitPosition => this._bitPosition;

  /// <summary>How many bits are left before the end of the data.</summary>
  public readonly int BitsRemaining => (this._data.Length << 3) - this._bitPosition;

  /// <summary>
  /// Whether any syntax element remains before the <c>rbsp_stop_one_bit</c> — <c>more_rbsp_data()</c>,
  /// clause 7.2.
  /// </summary>
  public readonly bool MoreRbspData => this._bitPosition < this._stopBitPosition;

  /// <summary>Takes one bit.</summary>
  public int ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3)
      throw new InvalidDataException("The H.264 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first — <c>u(n)</c>.</summary>
  public int ReadBits(int count) {
    if (count <= 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The H.264 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      value = (value << 1) | ((this._data[position >> 3] >> (7 - (position & 7))) & 1);
    }

    this._bitPosition += count;
    return value;
  }

  /// <summary>Looks at the next <paramref name="count"/> bits without consuming them, zero past the end.</summary>
  public readonly int NextBits(int count) {
    var value = 0;
    var limit = this._data.Length << 3;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      var bit = position < limit ? (this._data[position >> 3] >> (7 - (position & 7))) & 1 : 0;
      value = (value << 1) | bit;
    }

    return value;
  }

  /// <summary>Drops <paramref name="count"/> bits.</summary>
  public void Skip(int count) => this._bitPosition += count;

  /// <summary>Moves to the next byte boundary, dropping the bits in between.</summary>
  public void AlignToByte() => this._bitPosition = (this._bitPosition + 7) & ~7;

  /// <summary>
  /// Counts the zero bits up to and including the next one bit — the prefix half of an Exp-Golomb
  /// code, and on its own the syntax element <c>level_prefix</c> (clause 9.2.2.1).
  /// </summary>
  public int ReadLeadingZeroBits() {
    var zeroes = 0;
    while (this.ReadBit() == 0) {
      ++zeroes;

      // A prefix this long cannot occur in any conforming stream — level_prefix is bounded well
      // below it and an Exp-Golomb code num of 2^32 exceeds every field that uses one. Reaching it
      // means the reader has walked into padding or into a NAL it should not be in, and counting on
      // to the end of the buffer would turn that into a very slow nothing rather than an error.
      if (zeroes > 32)
        throw new InvalidDataException(
          "An H.264 variable-length code began with more than 32 zero bits, which no conforming "
          + "stream contains. The bitstream position is not on a syntax element.");
    }

    return zeroes;
  }

  /// <summary>An unsigned Exp-Golomb code — <c>ue(v)</c>, clause 9.1.</summary>
  public int ReadUnsignedExpGolomb() {
    var leadingZeroBits = this.ReadLeadingZeroBits();
    if (leadingZeroBits == 0)
      return 0;

    // codeNum = 2^leadingZeroBits - 1 + read_bits(leadingZeroBits), equation 9-1. Read in two halves
    // because a 32-bit suffix does not fit the accumulator a single ReadBits call returns.
    var suffix = leadingZeroBits <= 30
      ? (long)this.ReadBits(leadingZeroBits)
      : ((long)this.ReadBits(leadingZeroBits - 16) << 16) | (uint)this.ReadBits(16);

    var value = (1L << leadingZeroBits) - 1 + suffix;
    if (value > int.MaxValue)
      throw new InvalidDataException(
        $"An H.264 ue(v) syntax element decoded to {value}, which exceeds every field the standard "
        + "defines one for. The bitstream position is not on a syntax element.");

    return (int)value;
  }

  /// <summary>A signed Exp-Golomb code — <c>se(v)</c>, clause 9.1.1.</summary>
  public int ReadSignedExpGolomb() {
    var codeNum = this.ReadUnsignedExpGolomb();

    // (-1)^(k+1) * Ceil(k / 2), equation 9-3: odd code numbers are positive, even ones negative.
    return (codeNum & 1) != 0 ? (codeNum + 1) >> 1 : -(codeNum >> 1);
  }

  /// <summary>
  /// A truncated Exp-Golomb code — <c>te(v)</c>, clause 9.1: one inverted bit when the range is one,
  /// and an ordinary <c>ue(v)</c> otherwise.
  /// </summary>
  public int ReadTruncatedExpGolomb(int range) => range == 1 ? 1 - this.ReadBit() : this.ReadUnsignedExpGolomb();

  /// <summary>
  /// Finds the <c>rbsp_stop_one_bit</c>: the last one bit of the payload, which is the last bit any
  /// syntax element can occupy (clause 7.4.1.1).
  /// </summary>
  /// <remarks>
  /// Returns the length in bits when there is no one bit anywhere, which is a payload of nothing but
  /// zeroes. That is not a conforming RBSP, but answering "no data remains" for it lets the caller
  /// refuse it where the missing syntax element is named rather than here, where all that could be
  /// said is that the bytes were zero.
  /// </remarks>
  private static int _FindStopBit(ReadOnlySpan<byte> data) {
    for (var index = data.Length - 1; index >= 0; --index) {
      var octet = data[index];
      if (octet == 0)
        continue;

      var bit = 7;
      while (((octet >> (7 - bit)) & 1) == 0)
        --bit;

      return (index << 3) + bit;
    }

    return 0;
  }
}
