using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Reads an H.263 bitstream: most significant bit first, with no escaping of any kind.
/// </summary>
/// <remarks>
/// Start codes are found by looking at bits rather than at bytes, which is the difference between
/// this and the MPEG-1 reader beside it. ITU-T H.263 5.2.1 has an encoder put fewer than eight zero
/// bits in front of a group-of-blocks start code so that the code itself lands on a byte boundary,
/// and those stuffing bits are indistinguishable from the leading zeroes of the code. So a search
/// that aligned first would have to know how much stuffing there was before it could align, and one
/// that looks for sixteen zeroes followed by a one finds the code from wherever the macroblock layer
/// left the position. That search is safe because H.263 forbids a coded macroblock from producing
/// sixteen consecutive zeroes (5.2.2), which is the same guarantee that lets a start code be found
/// at all.
/// <para/>
/// <see cref="NextBits"/> pads with zeroes past the end of the data rather than throwing. Every
/// variable-length code is read by peeking the width of the longest code in its table and consuming
/// only what matched, so the last code of a picture always peeks past the end; the bits that are not
/// there are zero, and a code that appeared to match on them is one the table refuses by name.
/// </remarks>
internal ref struct H263BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private int _bitPosition;

  public H263BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitPosition = 0;
  }

  /// <summary>The bit the next read will take, counted from the first bit of the first byte.</summary>
  public readonly int BitPosition => this._bitPosition;

  /// <summary>How many bits are left.</summary>
  public readonly int BitsRemaining => (this._data.Length << 3) - this._bitPosition;

  /// <summary>Takes one bit.</summary>
  public int ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3)
      throw new InvalidDataException("The H.263 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first.</summary>
  public int ReadBits(int count) {
    if (count == 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The H.263 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      value = (value << 1) | ((this._data[position >> 3] >> (7 - (position & 7))) & 1);
    }

    this._bitPosition += count;
    return value;
  }

  /// <summary>
  /// Looks at the next <paramref name="count"/> bits without consuming them, padding with zeroes past
  /// the end of the data.
  /// </summary>
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

  /// <summary>Moves the position, which the start-code search needs in order to put one back.</summary>
  public void SeekToBit(int position) => this._bitPosition = position;

  /// <summary>
  /// Whether a start code begins here, allowing for the stuffing bits in front of it.
  /// </summary>
  /// <remarks>
  /// Sixteen zeroes is the test and not the whole seventeen-bit code, because the stuffing of H.263
  /// 5.2.1 is itself zeroes: between nought and seven of them sit in front of the code, so the number
  /// of zeroes before the terminating one is not known until it is found. Sixteen is enough to decide
  /// — no coded macroblock may produce that many in a row — and where the code really is follows from
  /// scanning to the one.
  /// </remarks>
  public readonly bool AtStartCode() => this.BitsRemaining >= 17 && this.NextBits(16) == 0;

  /// <summary>
  /// Consumes the stuffing and the seventeen-bit start code, leaving the position at the code's
  /// five-bit group number.
  /// </summary>
  public void ConsumeStartCode() {
    var zeroes = 0;
    while (this.BitsRemaining > 0 && this.ReadBit() == 0)
      ++zeroes;

    if (zeroes < 16)
      throw new InvalidDataException(
        $"An H.263 start code was expected but only {zeroes} zero bit(s) preceded the terminating one; the start code "
        + "of ITU-T H.263 5.2.2 is sixteen zeroes and a one.");
  }
}
