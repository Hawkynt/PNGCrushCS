using System;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// Reads the compressed payload of a coding unit, most significant bit of each byte first.
/// </summary>
/// <remarks>
/// SMPTE ST 2019-1:2016, 7.3.1 and Figure 28 give the mapping: a value of <c>l</c> bits is written
/// most significant bit first into ascending bytes, which is the plain big-endian bit order and needs
/// no word swapping — unlike the editing codecs this sits beside in the library.
/// <para/>
/// The reader can be positioned at a byte, because the format expects it to be. 7.2.11 puts a table
/// of byte offsets in the header, one for each macroblock scan line, and 7.3.1 pads the end of every
/// scan line so the next one starts on a four-byte boundary. A decoder is therefore meant to jump to
/// each scan line rather than to run continuously through the payload, which is what makes the scan
/// lines independently decodable and a damaged one recoverable from.
/// </remarks>
internal sealed class DnxHdBitReader {

  private readonly ReadOnlyMemory<byte> _data;
  private readonly int _sizeInBits;
  private int _position;

  internal DnxHdBitReader(ReadOnlyMemory<byte> data) {
    this._data = data;
    this._sizeInBits = data.Length * 8;
  }

  /// <summary>Moves to the start of a byte, as a macroblock scan index names one.</summary>
  internal void SeekToByte(int offset) {
    if (offset < 0 || offset > this._data.Length)
      throw new InvalidDataException(
        $"A VC-3 macroblock scan index points at byte {offset} of a {this._data.Length}-byte compressed payload.");

    this._position = offset * 8;
  }

  /// <summary>Reads one bit.</summary>
  internal int Bit() {
    if (this._position >= this._sizeInBits)
      throw new InvalidDataException("A VC-3 coding unit ended in the middle of a codeword.");

    var bit = (this._data.Span[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;

    return bit;
  }

  /// <summary>Reads an unsigned value of up to thirty-two bits, most significant bit first.</summary>
  internal int Bits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.Bit();

    return value;
  }
}
