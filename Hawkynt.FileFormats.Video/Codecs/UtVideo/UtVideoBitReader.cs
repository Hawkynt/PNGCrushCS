using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.UtVideo;

/// <summary>
/// Reads a Ut Video slice's bits: most significant first, out of a stream of little-endian
/// thirty-two bit words.
/// </summary>
/// <remarks>
/// The word order is not documented anywhere the format is described — the author's readme and the
/// community write-up both stop at "the codes are Huffman" — so it was established by measurement.
/// Reading the bytes in file order decodes the first dozen samples of a frame correctly and then
/// wanders, which is the signature of a bit stream that is right in blocks of four bytes and
/// scrambled between them. Turning every four bytes round decodes whole planes exactly.
/// <para/>
/// A slice whose length is not a whole number of words is padded with zeroes before the swap, which
/// is what the coder itself did: it wrote words, so the last one exists whether or not the codes
/// reached the end of it.
/// <para/>
/// One reader is built per slice rather than per plane, because a slice is where the coding starts
/// again — its first code begins at the first bit of its first word, and the bits left over at the
/// end of the slice before it are not part of it.
/// </remarks>
internal sealed class UtVideoBitReader {

  private readonly byte[] _data;
  private readonly int _bitLength;
  private int _position;

  internal UtVideoBitReader(ReadOnlySpan<byte> slice) {
    var words = (slice.Length + 3) / 4;
    this._data = new byte[words * 4];
    slice.CopyTo(this._data);

    for (var i = 0; i < words; ++i) {
      var offset = i * 4;
      BinaryPrimitives.WriteUInt32BigEndian(
        this._data.AsSpan(offset, 4),
        BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(offset, 4)));
    }

    this._bitLength = this._data.Length * 8;
  }

  /// <summary>The next bit, or zero once the slice has run out.</summary>
  /// <remarks>
  /// Zero rather than a throw, because the last word of a slice is padding whose bits a decoder may
  /// legitimately look at while finishing the last code of the last row. Running out for real is
  /// caught where it means something — a code that no table entry completes — and refused there.
  /// </remarks>
  internal int Bit() {
    if (this._position >= this._bitLength)
      return 0;

    var bit = (this._data[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }
}
