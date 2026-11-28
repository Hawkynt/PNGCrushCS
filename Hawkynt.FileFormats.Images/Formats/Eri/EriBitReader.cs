using System;
using System.IO;

namespace FileFormat.Eri;

/// <summary>Reads an ERI code stream most significant bit first.</summary>
/// <remarks>
/// The reference decoder fetches the stream four bytes at a time, padding the last fetch with
/// zeros, so up to three zero bytes past the end are part of the stream; anything beyond that is
/// the end of the data.
/// </remarks>
internal sealed class EriBitReader {

  private readonly byte[] _data;
  private readonly long _limitBits;
  private long _position;

  public EriBitReader(byte[] data, int offset = 0) {
    this._data = data;
    this._position = (long)offset * 8;
    this._limitBits = ((long)data.Length + 3) / 4 * 4 * 8;
  }

  /// <summary>Whether the stream has been read to its end.</summary>
  public bool AtEnd => this._position >= this._limitBits;

  /// <summary>The next bit, or <c>-1</c> once the stream is exhausted.</summary>
  public int TryReadBit() {
    if (this._position >= this._limitBits)
      return -1;

    var index = (int)(this._position >> 3);
    var bit = index < this._data.Length ? (this._data[index] >> (7 - (int)(this._position & 7))) & 1 : 0;
    ++this._position;
    return bit;
  }

  public int ReadBit() {
    var bit = this.TryReadBit();
    if (bit < 0)
      throw new InvalidDataException("ERI code stream ends before the picture does.");
    return bit;
  }

  public int ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.ReadBit();
    return value;
  }

  /// <summary>Reads an Elias-gamma style code: 0 is 1, 1x0 is 2–3, 1x1x0 is 4–7, and so on.</summary>
  public int ReadGamma() {
    if (this.ReadBit() == 0)
      return 1;

    var value = 0;
    var bias = 2;
    while (true) {
      value = (value << 1) | this.ReadBit();
      if (this.ReadBit() == 0)
        return value + bias;
      if (bias >= 1 << 28)
        throw new InvalidDataException("ERI gamma code is longer than any count the format can hold.");
      bias <<= 1;
    }
  }
}
