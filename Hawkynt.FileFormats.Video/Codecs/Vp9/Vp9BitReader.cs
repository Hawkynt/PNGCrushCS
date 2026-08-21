using System;
using System.IO;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Reads the raw, un-arithmetic-coded part of a VP9 frame: the uncompressed header and the superframe
/// index (specification 9.1).
/// </summary>
/// <remarks>
/// <c>f(n)</c> is an unsigned number written most significant bit first, and <c>s(n)</c> a magnitude
/// followed by a sign flag where a set flag means negative. That is the opposite of two's complement
/// and is worth spelling out, because reading it the other way costs half the deltas in a header
/// their sign and produces a picture rather than an error.
/// <para/>
/// Running off the end is an error and not a supply of zeroes. Unlike the token partitions of VP8,
/// every field this reads is one the frame promised to carry, so a packet that ends inside the
/// uncompressed header is a truncated packet and nothing else.
/// </remarks>
internal struct Vp9BitReader {

  private readonly byte[] _data;
  private readonly int _end;
  private int _bitPosition;

  internal Vp9BitReader(byte[] data, int start, int length) {
    this._data = data;
    this._end = (start + length) * 8;
    this._bitPosition = start * 8;
  }

  /// <summary>How many whole bytes have been consumed since the given byte offset.</summary>
  internal readonly int BytesReadFrom(int start) => (this._bitPosition / 8) - start;

  /// <summary>Whether the position is on a byte boundary, which <c>trailing_bits</c> exists to make true.</summary>
  internal readonly bool IsByteAligned => (this._bitPosition & 7) == 0;

  internal int ReadBit() {
    if (this._bitPosition >= this._end)
      throw new InvalidDataException(
        "This VP9 frame ends inside its uncompressed header. Every field of that header is one the frame states it "
        + "carries, so a packet that runs out during it is truncated.");

    var bit = (this._data[this._bitPosition >> 3] >> (7 - (this._bitPosition & 7))) & 1;
    ++this._bitPosition;
    return bit;
  }

  /// <summary>Reads <c>f(n)</c>: an unsigned value of <paramref name="bits"/> bits, high-order bit first.</summary>
  internal int ReadLiteral(int bits) {
    var value = 0;
    while (bits-- > 0)
      value = (value << 1) | this.ReadBit();

    return value;
  }

  /// <summary>Reads <c>s(n)</c>: a magnitude of <paramref name="bits"/> bits and then a sign flag.</summary>
  internal int ReadSignedLiteral(int bits) {
    var magnitude = this.ReadLiteral(bits);
    return this.ReadBit() != 0 ? -magnitude : magnitude;
  }

  /// <summary>Skips to the next byte boundary, which is what <c>trailing_bits</c> does (specification 6.1.1).</summary>
  internal void AlignToByte() {
    while (!this.IsByteAligned)
      this.ReadBit();
  }

  /// <summary>Reads a big-endian unsigned 32-bit value, which is how a tile states its size.</summary>
  internal uint ReadUnsigned32() {
    uint value = 0;
    for (var i = 0; i < 4; ++i)
      value = (value << 8) | (uint)this.ReadLiteral(8);

    return value;
  }

  /// <summary>Where the reader stands, in bytes from the start of the packet.</summary>
  internal readonly int BytePosition => this._bitPosition / 8;

  internal static int ReadUnsigned32(ReadOnlySpan<byte> data)
    => (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
}
