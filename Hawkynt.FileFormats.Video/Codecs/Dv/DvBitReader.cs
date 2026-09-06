using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// Reads a DV block's bits, most significant first, and stops dead at the block's own budget.
/// </summary>
/// <remarks>
/// The budget is the point of this type. A DV block is allotted a fixed number of bits — 112 for a
/// luma block, 80 for a colour one — and the coefficients that do not fit are written into whatever
/// space the block's neighbours left over, so the reader has to know exactly where its own allowance
/// ends. Bits at and past the limit read as zero, which does not change what is decoded: a code lying
/// wholly inside the limit is identified by bits that are all real, and a code reaching past it is
/// abandoned whatever the padding says.
/// </remarks>
internal readonly ref struct DvBitReader {

  private readonly ReadOnlySpan<byte> _data;

  internal DvBitReader(ReadOnlySpan<byte> data, int bitLimit) {
    this._data = data;
    this.BitLimit = bitLimit;
  }

  /// <summary>The bit past the last one this reader will hand back.</summary>
  internal int BitLimit { get; }

  /// <summary>
  /// The thirty-two bits starting at a bit position, left-aligned, zero past the limit.
  /// </summary>
  /// <remarks>
  /// A window rather than a stream cursor, because the block layer decides how many bits a code took
  /// only after it has looked at it: the reader hands over enough to identify the longest code there
  /// is — fifteen bits and a sign — and the caller advances by what it turned out to be.
  /// </remarks>
  internal uint PeekWord(int bitIndex) {
    var available = this.BitLimit - bitIndex;
    if (available <= 0)
      return 0;

    var byteIndex = bitIndex >> 3;
    var shift = bitIndex & 7;

    // Five bytes cover any 32-bit window whatever its bit alignment. Anything outside the block reads
    // as zero at both ends: a continued block's window begins before the buffer by however many bits
    // of a split codeword it is carrying.
    ulong accumulator = 0;
    for (var i = 0; i < 5; ++i) {
      var at = byteIndex + i;
      accumulator = (accumulator << 8) | (uint)(at >= 0 && at < this._data.Length ? this._data[at] : 0);
    }

    var word = (uint)(accumulator >> (8 - shift));
    return available >= 32 ? word : word & (uint.MaxValue << (32 - available));
  }

  /// <summary>Reads an unsigned field.</summary>
  internal uint ReadBits(ref int bitIndex, int count) {
    var value = this.PeekWord(bitIndex) >> (32 - count);
    bitIndex += count;
    return value;
  }

  /// <summary>Reads a two's-complement field, sign-extended.</summary>
  internal int ReadSigned(ref int bitIndex, int count) {
    var value = (int)this.PeekWord(bitIndex);
    bitIndex += count;
    return value >> (32 - count);
  }
}
