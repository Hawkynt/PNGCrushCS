using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Reads an MPEG-1 video bitstream: most significant bit first, with no escaping of any kind.
/// </summary>
/// <remarks>
/// There is nothing between the bits and the bytes. Unlike H.264 and its descendants an MPEG-1 video
/// stream carries no emulation prevention, because the encoder is instead required never to produce
/// twenty-three consecutive zeroes inside coded data (ISO/IEC 11172-2, 2.4.2.1). So a start code can
/// be found by a byte search and a bit can be read by a shift, and this type does no unescaping.
/// <para/>
/// <see cref="NextBits"/> looks ahead without consuming, which the syntax needs in three places: the
/// <c>extra_bit_picture</c> and <c>extra_bit_slice</c> loops both terminate on a zero that is only
/// then consumed, and every variable-length code is decoded by peeking the longest code in its table
/// and consuming only as many bits as the match turned out to be.
/// </remarks>
internal ref struct MpegBitReader {

  private readonly ReadOnlySpan<byte> _data;
  private int _bitPosition;

  public MpegBitReader(ReadOnlySpan<byte> data) {
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
      throw new InvalidDataException("The MPEG-1 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first.</summary>
  public int ReadBits(int count) {
    if (count == 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The MPEG-1 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

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
  /// <remarks>
  /// Padding rather than throwing, because this is what the variable-length decoders peek with: the
  /// last code in a packet is shorter than the widest code in its table, so a peek of the widest
  /// always runs past the end and the match is still unambiguous. The bits that are not there are
  /// zero, and a code that then appeared to match on them would be a code the table says is invalid —
  /// which is refused where the match is made, with the table's name in the message.
  /// </remarks>
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
