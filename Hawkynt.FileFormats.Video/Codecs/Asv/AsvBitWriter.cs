using System;
using System.Collections.Generic;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// Writes the logical bit order asv1.txt describes, most significant bit first — the exact stream
/// <see cref="H263BitReader"/> reads once each codec's own byte or bit scrambling has been undone.
/// </summary>
/// <remarks>
/// Both ASV codecs keep their one storage oddity in a single place: ASV1 reverses the byte order of
/// every four-byte word and ASV2 reverses the bit order of every byte, and each of those two
/// transformations is its own inverse. So an encoder writes the document's own bit order here and
/// runs the finished buffer through the very function the decoder uses to undo it, which is why there
/// is no second, mirror-image copy of either scrambling anywhere in this package.
/// </remarks>
internal sealed class AsvBitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Writes a fixed-width unsigned field, most significant bit first.</summary>
  internal void Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);
  }

  /// <summary>
  /// Writes a fixed-width field least significant bit first, which is what ASV2's plain binary
  /// numbers need on top of its byte-wide bit reversal.
  /// </summary>
  /// <remarks>
  /// The mirror of <c>Asv2Bitstream.ReadReversedBits</c>: reversing every byte turns a
  /// variable-length code the right way round and a fixed-width number the wrong way round at once,
  /// because a code is read for the order its bits arrive in and a number for their place value.
  /// </remarks>
  internal void ReversedBits(int value, int count) {
    for (var i = 0; i < count; ++i)
      this._Bit((value >> i) & 1);
  }

  /// <summary>Writes one of a table's codes, given as the string the document prints it as.</summary>
  internal void Code(string code) {
    foreach (var character in code) {
      if (character == ' ')
        continue;

      this._Bit(character == '1' ? 1 : 0);
    }
  }

  /// <summary>
  /// Finishes the stream, zero-padding to a whole number of the given number of bytes.
  /// </summary>
  /// <param name="alignment">
  /// Four for ASV1, whose word byte-swap has no meaning on a partial word, and one for ASV2, whose
  /// scrambling is per byte and needs no alignment at all.
  /// </param>
  internal byte[] Finish(int alignment) {
    while (this._partialBits != 0)
      this._Bit(0);

    while (this._bytes.Count % alignment != 0)
      this._bytes.Add(0);

    return this._bytes.ToArray();
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
