using System;

namespace FileFormat.Codecs.H263;

/// <summary>Writes the shared H.263/RealVideo macroblock syntax most significant bit first.</summary>
internal sealed class H263BitWriter {

  private byte[] _bytes = new byte[4096];
  private int _count;
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="length"/> bits of a value, most significant first.</summary>
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
      Array.Resize(ref this._bytes, this._bytes.Length * 2);

    this._bytes[this._count++] = (byte)this._partial;
    this._partial = 0;
    this._partialBits = 0;
  }

  /// <summary>Appends a VLC written in the grouped notation used by ITU-T H.263.</summary>
  internal void WriteCode(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this.WriteBit(0); break;
        case '1': this.WriteBit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit in an H.263 codeword.", nameof(code));
      }
  }

  /// <summary>Returns the bytes written, padding the last byte with zeroes.</summary>
  internal byte[] ToArray() {
    var length = this._count + (this._partialBits == 0 ? 0 : 1);
    var result = new byte[length];
    Array.Copy(this._bytes, result, this._count);
    if (this._partialBits != 0)
      result[this._count] = (byte)(this._partial << (8 - this._partialBits));

    return result;
  }
}