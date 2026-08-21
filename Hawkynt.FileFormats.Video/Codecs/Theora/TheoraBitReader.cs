using System;
using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// Reads Theora's bit fields out of a packet, most significant bit first.
/// </summary>
/// <remarks>
/// Theora specification section 5.2. The direction is worth stating plainly because Theora's sister
/// codec goes the other way: Vorbis packs its integers least significant bit first into the least
/// significant bit of each byte, and Theora packs them most significant bit first into the most
/// significant bit. A reader written for one produces perfectly plausible values from the other.
/// <para/>
/// Reading past the end of a packet is an end-of-packet condition rather than an error, and every
/// read after it is one too (section 5.2.4). Theora does not use truncated packets as a normal mode
/// of operation, so this reader remembers that it happened and the decoder refuses the frame by name
/// rather than reconstructing a picture out of the zeroes that were returned. That is the whole
/// reason the flag exists: bits read past the end are zeroes, zeroes are a valid bitstream, and a
/// decoder without the flag turns a truncated packet into a picture nobody can tell is wrong.
/// </remarks>
internal sealed class TheoraBitReader {

  private readonly ReadOnlyMemory<byte> _data;
  private int _bitPosition;
  private bool _exhausted;

  internal TheoraBitReader(ReadOnlyMemory<byte> data) => this._data = data;

  /// <summary>Whether a read has run off the end of the packet.</summary>
  internal bool EndOfPacket => this._exhausted;

  /// <summary>
  /// Reads an unsigned integer of the given width, most significant bit first.
  /// </summary>
  /// <remarks>
  /// A width of zero returns zero and consumes nothing, and does not raise the end-of-packet
  /// condition on its own — section 5.2.5, which exists because the setup header reads fields whose
  /// width is computed and can legitimately come out as zero.
  /// </remarks>
  internal uint ReadBits(int count) {
    if (count == 0)
      return 0;

    var span = this._data.Span;
    var value = 0u;
    for (var i = 0; i < count; ++i) {
      var byteIndex = this._bitPosition >> 3;
      if (byteIndex >= span.Length) {
        this._exhausted = true;
        value <<= 1;
        continue;
      }

      var bit = (span[byteIndex] >> (7 - (this._bitPosition & 7))) & 1;
      value = (value << 1) | (uint)bit;
      ++this._bitPosition;
    }

    return value;
  }

  /// <summary>Reads one bit.</summary>
  internal int ReadBit() => (int)this.ReadBits(1);

  /// <summary>Refuses the packet by name when a read has run off its end.</summary>
  internal void EnsureComplete(string what) {
    if (this._exhausted)
      throw new InvalidDataException(
        $"The packet ends part way through {what}: {this._data.Length} bytes were there and more were needed.");
  }
}
