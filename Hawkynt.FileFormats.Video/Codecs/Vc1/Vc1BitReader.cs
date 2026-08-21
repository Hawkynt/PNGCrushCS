using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// Reads a VC-1 bitstream: most significant bit first, with no escaping of any kind.
/// </summary>
/// <remarks>
/// No escaping because this reads Simple and Main profile, whose pictures arrive one to a container
/// packet and carry no start codes. The emulation prevention bytes of SMPTE 421M Annex E belong to
/// the Advanced profile's byte stream, where a picture has to be found inside a run of bytes rather
/// than handed over already delimited; a reader that removed them here would corrupt every payload
/// that happened to hold three particular bytes in a row.
/// <para/>
/// <see cref="Peek"/> pads with zeroes past the end of the data rather than throwing. Every
/// variable-length code is read by peeking the width of the longest code in its table and consuming
/// only what matched, so the last code of a picture always peeks past the end; the bits that are not
/// there are zero, and a code that appeared to match on them is one the table refuses by name.
/// </remarks>
internal ref struct Vc1BitReader {

  private readonly ReadOnlySpan<byte> _data;
  private int _bitPosition;

  internal Vc1BitReader(ReadOnlySpan<byte> data) {
    this._data = data;
    this._bitPosition = 0;
  }

  /// <summary>The bit the next read will take, counted from the first bit of the first byte.</summary>
  internal readonly int BitPosition => this._bitPosition;

  /// <summary>How many bits are left.</summary>
  internal readonly int BitsRemaining => (this._data.Length << 3) - this._bitPosition;

  /// <summary>Takes one bit.</summary>
  internal int ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3)
      throw new InvalidDataException("The VC-1 bitstream ended in the middle of a syntax element.");

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  /// <summary>Takes <paramref name="count"/> bits as an unsigned number, most significant first.</summary>
  internal int ReadBits(int count) {
    if (count <= 0)
      return 0;

    if (this._bitPosition + count > this._data.Length << 3)
      throw new InvalidDataException(
        $"The VC-1 bitstream ended {count - this.BitsRemaining} bit(s) short of a {count}-bit field.");

    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.ReadBit();

    return value;
  }

  /// <summary>
  /// Looks at the next <paramref name="count"/> bits without consuming them, padding with zeroes past
  /// the end of the data.
  /// </summary>
  internal readonly int Peek(int count) {
    var value = 0;
    var total = this._data.Length << 3;

    for (var i = 0; i < count; ++i) {
      var position = this._bitPosition + i;
      var bit = position >= total ? 0 : (this._data[position >> 3] >> (7 - (position & 7))) & 1;
      value = (value << 1) | bit;
    }

    return value;
  }

  /// <summary>Steps over bits already accounted for by a peek.</summary>
  internal void Skip(int count) => this._bitPosition += count;
}
