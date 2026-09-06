using System;

namespace FileFormat.Codecs.H261;

/// <summary>
/// Writes an H.261 picture a codeword at a time, most significant bit first.
/// </summary>
/// <remarks>
/// The mirror of <see cref="H263.H263BitReader"/>, and as plain as it is: ITU-T H.261 escapes nothing
/// anywhere in its bitstream, so a bit is a shift and no byte is ever looked at twice.
/// <para/>
/// The last byte is padded with zeroes, which is not merely tidiness. A picture handed to a container
/// is a whole number of bytes whatever the coding came to, and the zeroes that make it one are also
/// what the next picture's start code may be found behind: the code is twenty bits of which the first
/// sixteen are zero, so a run of zeroes in front of it only lengthens the run a start-code search is
/// looking for and cannot hide it.
/// </remarks>
internal sealed class H261BitWriter {

  private byte[] _bytes = new byte[4096];
  private int _count;
  private int _partial;
  private int _partialBits;

  /// <summary>How many bits have been written, the final padding not included.</summary>
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

  /// <summary>Appends one codeword from a variable-length table.</summary>
  internal void WriteCode((int Code, int Length) code) => this.Write(code.Code, code.Length);

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
