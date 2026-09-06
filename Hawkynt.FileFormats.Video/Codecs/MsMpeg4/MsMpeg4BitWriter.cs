using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// Writes a Microsoft MPEG-4 picture a codeword at a time, most significant bit first.
/// </summary>
/// <remarks>
/// The mirror of the reader the decoder uses, and as plain: there is no escaping of any kind in any of
/// the three bitstreams, so a bit is a shift and nothing has to be looked at twice. The one thing this
/// does that a naive writer would not is pad the last byte with zeroes, which is what makes the
/// trailing extension header findable — a decoder decides whether the header is there by counting how
/// many bits are left, so a picture has to end on a byte boundary for that count to mean anything.
/// </remarks>
internal sealed class MsMpeg4BitWriter {

  private byte[] _bytes = new byte[4096];
  private int _count;
  private int _partial;
  private int _partialBits;

  /// <summary>How many bits have been written, padding not included.</summary>
  internal int BitCount => 8 * this._count + this._partialBits;

  /// <summary>Appends the low <paramref name="length"/> bits of a value, most significant first.</summary>
  internal void Write(int value, int length) {
    for (var i = length - 1; i >= 0; --i)
      this.WriteBit((value >> i) & 1);
  }

  /// <summary>Appends one bit.</summary>
  internal void WriteBit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits < 8)
      return;

    if (this._count == this._bytes.Length)
      Array.Resize(ref this._bytes, 2 * this._bytes.Length);

    this._bytes[this._count++] = (byte)this._partial;
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>The picture as bytes, its last byte padded with zeroes.</summary>
  internal byte[] ToArray() {
    var bytes = this._count;
    if (this._partialBits > 0)
      ++bytes;

    var result = new byte[bytes];
    Array.Copy(this._bytes, result, this._count);
    if (this._partialBits > 0)
      result[this._count] = (byte)(this._partial << (8 - this._partialBits));

    return result;
  }
}
