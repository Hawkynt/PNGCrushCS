using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>
/// Reads a HuffYUV frame's bits: most significant first, out of a stream of little-endian
/// thirty-two bit words.
/// </summary>
/// <remarks>
/// The word order is the whole of the trick, and it is not a decoration. HuffYUV was written for
/// Video for Windows on a little-endian machine and its coder wrote whole machine words, so the
/// bytes of every four sit in the file back to front. Reading them in file order gives a bit stream
/// that is scrambled in blocks of four bytes and decodes into noise that looks like a picture.
/// <para/>
/// The evidence is in the first pixel of every frame, which is stored raw. A planar 4:2:2 frame's
/// first four bytes come out as V, then the second Y, then U, then the first Y — which is the
/// <c>Y U Y V</c> of memory read backwards, exactly as a little-endian word would be. A 4:4:4:4
/// frame's first pixel comes out alpha, red, green, blue for the same reason.
/// <para/>
/// A frame whose length is not a whole number of words is padded with zeroes before the swap, which
/// is what the coder itself did: it wrote words, so the last one exists whether or not the bits
/// reached the end of it.
/// </remarks>
internal sealed class HuffYuvBitReader {

  private readonly byte[] _data;
  private readonly int _bitLength;
  private int _position;

  internal HuffYuvBitReader(ReadOnlySpan<byte> frame) {
    var words = (frame.Length + 3) / 4;
    this._data = new byte[words * 4];
    frame.CopyTo(this._data);

    for (var i = 0; i < words; ++i) {
      var offset = i * 4;
      BinaryPrimitives.WriteUInt32BigEndian(
        this._data.AsSpan(offset, 4),
        BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(offset, 4)));
    }

    this._bitLength = this._data.Length * 8;
  }

  /// <summary>
  /// The frame's bytes with the swap already done, which is where a frame that carries its own
  /// Huffman tables carries them.
  /// </summary>
  /// <remarks>
  /// The tables are inside the swap and not in front of it. A frame that states its own tables has
  /// them at its first byte after the words have been turned round, byte aligned, and the picture
  /// begins at the byte after the last of them — which is why they are read from here rather than
  /// from the packet as it lies in the file.
  /// </remarks>
  internal ReadOnlySpan<byte> Swapped => this._data;

  /// <summary>Moves the reader to a byte boundary, for a picture that begins after a frame's tables.</summary>
  internal void SeekToByte(int offset) {
    if (offset < 0 || offset > this._data.Length)
      throw new InvalidDataException($"A frame of {this._data.Length} bytes states tables that end at byte {offset}.");

    this._position = offset * 8;
  }

  /// <summary>Whether the reader has run past the end of the frame.</summary>
  internal bool Exhausted => this._position >= this._bitLength;

  /// <summary>The next bit, or zero once the frame has run out.</summary>
  /// <remarks>
  /// Zero rather than a throw, because the last word of a frame is padding whose bits a decoder may
  /// legitimately look at while finishing the last symbol of the last row. Running out for real is
  /// caught where it means something — a Huffman code that never completes, or a row that ends
  /// early — and refused there with a message that says which.
  /// </remarks>
  internal int Bit() {
    if (this._position >= this._bitLength)
      return 0;

    var bit = (this._data[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }

  /// <summary>The next <paramref name="count"/> bits as a number, most significant first.</summary>
  internal int Bits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.Bit();

    return value;
  }

  /// <summary>Refuses a frame that ran out before the picture did.</summary>
  internal void RefuseIfExhausted(string what) {
    if (this._position <= this._bitLength)
      return;

    throw new InvalidDataException($"The frame ran out of bits while reading {what}.");
  }
}
