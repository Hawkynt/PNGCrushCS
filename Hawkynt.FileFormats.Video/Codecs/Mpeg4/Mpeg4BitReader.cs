using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Reads an MPEG-4 Part 2 visual bitstream: most significant bit first, with no escaping of any kind.
/// </summary>
/// <remarks>
/// There is nothing between the bits and the bytes. Unlike H.264 and its descendants an MPEG-4 Part 2
/// stream carries no emulation prevention, because the encoder is instead required never to produce
/// twenty-three consecutive zeroes inside coded data (ISO/IEC 14496-2, 6.2.1). So a start code can be
/// found by a byte search and a bit by a shift, and this type does no unescaping.
/// <para/>
/// <see cref="NextBits"/> looks ahead without consuming, which the syntax needs in several places:
/// <c>modulo_time_base</c> is a run of ones ended by a zero, a resync marker has to be recognised
/// before it is taken, and every variable-length code is decoded by peeking the longest code in its
/// table and consuming only as many bits as the match turned out to be. Past the end of the data the
/// peek pads with zeroes rather than throwing, because the last code of a packet is shorter than the
/// widest code in its table and a peek of the widest always runs past the end.
/// </remarks>
internal ref struct Mpeg4BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private int _bitPosition;

  public Mpeg4BitReader(ReadOnlySpan<byte> data) {
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
      throw new InvalidDataException("The MPEG-4 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first.</summary>
  public int ReadBits(int count) {
    if (count == 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The MPEG-4 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      value = (value << 1) | ((this._data[position >> 3] >> (7 - (position & 7))) & 1);
    }

    this._bitPosition += count;
    return value;
  }

  /// <summary>
  /// Takes a bit the standard fixes at one, and refuses the stream when it is not.
  /// </summary>
  /// <remarks>
  /// The marker bits of ISO/IEC 14496-2 exist so that a header cannot produce a run of zeroes long
  /// enough to look like a start code. Reading one and not checking it throws away the only
  /// inexpensive check a decoder has that it is still where it thinks it is in the header, which for
  /// a header of thirteen-bit fields with a marker between them is exactly where a slip is otherwise
  /// invisible.
  /// </remarks>
  public void ReadMarkerBit(string field) {
    if (this.ReadBit() != 1)
      throw new InvalidDataException(
        $"The marker bit {field} of this MPEG-4 header is zero. ISO/IEC 14496-2 fixes every marker bit at one, so "
        + "either this is not the header it was read as or the stream is corrupt.");
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

  /// <summary>Moves to the next byte boundary, dropping the bits in between.</summary>
  public void AlignToByte() => this._bitPosition = (this._bitPosition + 7) & ~7;
}
