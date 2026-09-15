using System;

namespace FileFormat.Codecs.H263;

/// <summary>Writes an H.263 picture a bit at a time, most significant bit first.</summary>
/// <remarks>
/// H.263 escapes nothing at this layer: start codes, fixed-width fields and variable-length codes are
/// all one uninterrupted bitstream. The last byte is padded with zeroes so the next picture can begin
/// byte-aligned as clause 5.1.1 requires.
/// </remarks>
internal sealed class H263BitWriter {

  private byte[] _bytes = new byte[4096];
  private int _count;
  private int _partial;
  private int _partialBits;

  /// <summary>How many bits have been written, excluding final byte padding.</summary>
  internal int BitCount => 8 * this._count + this._partialBits;

  /// <summary>Appends the low <paramref name="length"/> bits of <paramref name="value"/>, most significant first.</summary>
  internal void Write(int value, int length) {
    for (var bit = length - 1; bit >= 0; --bit)
      this.WriteBit((value >> bit) & 1);
  }

  /// <summary>Appends one bit.</summary>
  internal void WriteBit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits != 8)
      return;

    if (this._count == this._bytes.Length)
      Array.Resize(ref this._bytes, 2 * this._bytes.Length);

    this._bytes[this._count++] = (byte)this._partial;
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Appends one codeword from an H.263 variable-length table.</summary>
  internal void WriteCode((int Code, int Length) code) => this.Write(code.Code, code.Length);

  /// <summary>Returns the bytes written, padding the final byte with zeroes when necessary.</summary>
  internal byte[] ToArray() {
    var length = this._count + (this._partialBits == 0 ? 0 : 1);
    var result = new byte[length];
    Array.Copy(this._bytes, result, this._count);

    if (this._partialBits != 0)
      result[this._count] = (byte)(this._partial << (8 - this._partialBits));

    return result;
  }
}
