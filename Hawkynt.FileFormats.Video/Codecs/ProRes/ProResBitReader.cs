using System;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Reads the bits of one sized syntax structure, most significant bit of each byte first.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5, states the convention this implements: bit strings and variable-length codes
/// appear in the bitstream left bit first and numerical values most-significant bit first, which for
/// a big-endian reader are the same thing. There is no word swapping anywhere in ProRes, unlike the
/// editing codecs it sits beside in this library.
/// <para/>
/// The reader is bounded by the structure it was given rather than by the frame, because ProRes asks
/// two questions that only make sense against a stated size. <c>endOfData()</c> — RDD 36:2022, 5 —
/// is how the run-and-level loop of a colour component knows to stop: the coded data of a component
/// ends when fewer than thirty-two bits remain and every one of them is zero. And the sizes are
/// authoritative over the syntax: 6.4 requires a decoder to find the next structure from the stated
/// size rather than from where parsing happened to stop, so that a version variant carrying data
/// this decoder does not recognise is stepped over rather than parsed as coefficients.
/// </remarks>
internal sealed class ProResBitReader {

  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _sizeInBits;
  private int _position;

  internal ProResBitReader(ReadOnlyMemory<byte> data) {
    this._data = data;
    this._sizeInBits = data.Length * 8;
  }

  /// <summary>The number of bits consumed so far, which is what <c>byteAligned()</c> is asked about.</summary>
  internal int Position => this._position;

  /// <summary>Reads one bit.</summary>
  internal int Bit() {
    if (this._position >= this._sizeInBits)
      throw new InvalidDataException("A ProRes syntax structure ended in the middle of a codeword.");

    var bit = (this._data.Span[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }

  /// <summary>Reads an unsigned value of up to thirty-two bits, most significant bit first.</summary>
  internal uint Bits(int count) {
    if (count is < 0 or > 32)
      throw new InvalidDataException($"A ProRes codeword claimed a {count}-bit field, which no syntax element is.");

    var value = 0u;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | (uint)this.Bit();

    return value;
  }

  /// <summary>
  /// Counts the '0' bits before the next '1' without consuming any of them.
  /// </summary>
  /// <remarks>
  /// The code level of a Golomb codeword — RDD 36:2022, 7.1.1.1. It is peeked rather than consumed
  /// because the two branches that follow disagree about what to do with the separator: the
  /// Golomb-Rice branch discards it, while the exponential-Golomb branch keeps it as the leading '1'
  /// of the value it is about to read.
  /// </remarks>
  internal int PeekLevel() {
    var level = 0;
    for (var at = this._position; at < this._sizeInBits; ++at, ++level)
      if (((this._data.Span[at >> 3] >> (7 - (at & 7))) & 1) != 0)
        return level;

    throw new InvalidDataException("A ProRes codeword ran past the end of its syntax structure without a separator bit.");
  }

  /// <summary>Steps over bits already accounted for.</summary>
  internal void Skip(int count) {
    if (this._position + count > this._sizeInBits)
      throw new InvalidDataException("A ProRes syntax structure ended in the middle of a codeword.");

    this._position += count;
  }

  /// <summary>
  /// Whether the coded data of a colour component has ended, as <c>endOfData()</c> defines it.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, 5: true when thirty-one or fewer bits remain and all of them are zero, false when
  /// thirty-two or more remain or any remaining bit is one. Both halves matter. Stopping at the
  /// byte the sizes imply would read the padding zeroes as another run-and-level pair, and stopping
  /// as soon as fewer than thirty-two bits remain would drop the last coefficient of any component
  /// whose data happens to end close to its boundary.
  /// </remarks>
  internal bool EndOfData() {
    var remaining = this._sizeInBits - this._position;
    if (remaining >= 32)
      return false;

    for (var at = this._position; at < this._sizeInBits; ++at)
      if (((this._data.Span[at >> 3] >> (7 - (at & 7))) & 1) != 0)
        return false;

    return true;
  }
}
