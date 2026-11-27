using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Reads an H.265 raw byte sequence payload: bits most significant first, and the two variable
/// length codings the parameter sets and slice headers are written in (ITU-T H.265, clause 9.2).
/// </summary>
/// <remarks>
/// The bytes handed to this have already had their emulation prevention removed
/// (<see cref="H265NalUnit"/>), so a bit is a shift and nothing else. That separation matters more in
/// HEVC than in its predecessor because of the entry points: a slice segment carrying several CABAC
/// substreams states each substream's length in bytes <em>of the escaped unit</em>, and the offsets
/// only line up with the unescaped payload if the removals between them are counted. So this reader
/// never sees an escape, and the one place that has to know where they were —
/// <see cref="H265NalUnit.EmulationPreventionPositions"/> — is the unescaper itself.
/// <para/>
/// <see cref="MoreRbspData"/> is answered from a stop bit found once at construction. HEVC needs it
/// in fewer places than H.264 did — slice data is terminated by <c>end_of_slice_segment_flag</c>
/// rather than by running out of bits — but the parameter set extensions still ask it.
/// </remarks>
internal ref struct H265BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private readonly int _stopBitPosition;
  private int _bitPosition;

  public H265BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitPosition = 0;
    this._stopBitPosition = _FindStopBit(data);
  }

  /// <summary>The bit the next read will take, counted from the first bit of the first byte.</summary>
  public readonly int BitPosition => this._bitPosition;

  /// <summary>The byte the next read will take, valid only on a byte boundary.</summary>
  public readonly int BytePosition => this._bitPosition >> 3;

  /// <summary>Whether the position is on a byte boundary.</summary>
  public readonly bool IsByteAligned => (this._bitPosition & 7) == 0;

  /// <summary>How many bits are left before the end of the data.</summary>
  public readonly int BitsRemaining => (this._data.Length << 3) - this._bitPosition;

  /// <summary>
  /// Whether any syntax element remains before the <c>rbsp_stop_one_bit</c> — <c>more_rbsp_data()</c>,
  /// clause 7.2.
  /// </summary>
  public readonly bool MoreRbspData => this._bitPosition < this._stopBitPosition;

  /// <summary>Takes one bit — <c>u(1)</c>, and read as a flag wherever the syntax says <c>flag</c>.</summary>
  public int ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3)
      throw new InvalidDataException("The H.265 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes one bit as a flag.</summary>
  public bool ReadFlag() => this.ReadBit() != 0;

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first — <c>u(n)</c>.</summary>
  public int ReadBits(int count) {
    if (count <= 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The H.265 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      value = (value << 1) | ((this._data[position >> 3] >> (7 - (position & 7))) & 1);
    }

    this._bitPosition += count;
    return value;
  }

  /// <summary>Takes up to 64 bits, for the fields wider than an <see cref="int"/> holds.</summary>
  public long ReadBitsLong(int count) {
    var value = 0L;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | (uint)this.ReadBit();

    return value;
  }

  /// <summary>Drops <paramref name="count"/> bits.</summary>
  public void Skip(int count) {
    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The H.265 bitstream ended while skipping {count} bit(s) of a syntax structure this decoder does not read.");

    this._bitPosition += count;
  }

  /// <summary>Moves to the next byte boundary, dropping the bits in between — <c>byte_alignment()</c>.</summary>
  public void AlignToByte() => this._bitPosition = (this._bitPosition + 7) & ~7;

  /// <summary>An unsigned Exp-Golomb code — <c>ue(v)</c>, clause 9.2.</summary>
  public int ReadUnsignedExpGolomb() {
    var leadingZeroBits = 0;
    while (this.ReadBit() == 0) {
      ++leadingZeroBits;

      // No conforming stream contains a longer prefix: the widest ue(v) the standard defines is
      // bounded well below 2^32, so a prefix past 32 means the reader is not on a syntax element at
      // all. Counting on to the end of the buffer would turn that into a very slow nothing.
      if (leadingZeroBits > 32)
        throw new InvalidDataException(
          "An H.265 ue(v) code began with more than 32 zero bits, which no conforming stream contains. "
          + "The bitstream position is not on a syntax element.");
    }

    if (leadingZeroBits == 0)
      return 0;

    // codeNum = 2^leadingZeroBits - 1 + read_bits(leadingZeroBits). Read in halves because a 32-bit
    // suffix does not fit the accumulator a single ReadBits call returns.
    var suffix = leadingZeroBits <= 30
      ? (long)this.ReadBits(leadingZeroBits)
      : ((long)this.ReadBits(leadingZeroBits - 16) << 16) | (uint)this.ReadBits(16);

    var value = (1L << leadingZeroBits) - 1 + suffix;
    if (value > int.MaxValue)
      throw new InvalidDataException(
        $"An H.265 ue(v) syntax element decoded to {value}, which exceeds every field the standard defines one "
        + "for. The bitstream position is not on a syntax element.");

    return (int)value;
  }

  /// <summary>A signed Exp-Golomb code — <c>se(v)</c>, clause 9.2.2.</summary>
  public int ReadSignedExpGolomb() {
    var codeNum = this.ReadUnsignedExpGolomb();

    // (-1)^(k+1) * Ceil(k / 2): odd code numbers are positive, even ones negative.
    return (codeNum & 1) != 0 ? (codeNum + 1) >> 1 : -(codeNum >> 1);
  }

  /// <summary>
  /// Finds the <c>rbsp_stop_one_bit</c>: the last one bit of the payload, which is the last bit any
  /// syntax element can occupy (clause 7.4.2.1).
  /// </summary>
  /// <remarks>
  /// Answers zero for a payload of nothing but zero bytes, which is not a conforming RBSP. Saying
  /// "no data remains" for it lets the caller refuse it where the missing syntax element is named,
  /// rather than here where all that could be said is that the bytes were zero.
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
