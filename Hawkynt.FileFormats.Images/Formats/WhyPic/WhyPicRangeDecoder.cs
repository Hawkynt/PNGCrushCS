using System;
using System.IO;

namespace FileFormat.WhyPic;

/// <summary>The 32-bit interval decoder every field after the two letters is read through.</summary>
/// <remarks>
/// The interval is held as two 32-bit bounds and narrowed in double precision, the scale and the
/// product being formed before the division and the one being subtracted after it. That order is
/// the format: a decoder that narrows by any other arithmetic lands one unit away from the encoder
/// often enough to lose the picture, which is why it is spelt out here rather than tidied.
/// <para/>
/// When the top bits of the bounds agree they are shifted out; when the bounds straddle the middle
/// with the second bits pointing inwards the second bit is dropped instead and the code value has
/// its top bit flipped, the usual way of keeping a carry from ever being needed.
/// </remarks>
internal ref struct WhyPicRangeDecoder {

  /// <summary>How many zero bytes past the end may be shifted in before the stream is called short.</summary>
  private const int _LOOKAHEAD_BYTES = 4;

  private readonly ReadOnlySpan<byte> _data;
  private int _position;
  private int _bitInByte;
  private int _current;
  private uint _low;
  private uint _high;
  private uint _code;
  private readonly WhyPicRangeEncoder? _mirror;

  /// <summary>Starts decoding at <paramref name="offset"/>, priming the code value with 32 bits.</summary>
  /// <param name="data">The whole file.</param>
  /// <param name="offset">Where the code starts.</param>
  /// <param name="mirror">
  /// An encoder handed every interval the decoder narrows to, so that the code the decoded
  /// symbols stand for can be compared with the code that was read; <c>null</c> to skip that.
  /// </param>
  public WhyPicRangeDecoder(ReadOnlySpan<byte> data, int offset, WhyPicRangeEncoder? mirror = null) {
    this._data = data;
    this._mirror = mirror;
    this._position = offset;
    this._current = this._position < data.Length ? data[this._position] : 0;
    ++this._position;
    this._bitInByte = 0;
    this._high = uint.MaxValue;
    this._low = 0;
    this._code = 0;
    for (var i = 0; i < 32; ++i)
      this._code = (this._code << 1) + (uint)this._NextBit();
  }

  private int _NextBit() {
    if (this._bitInByte == 8) {
      this._bitInByte = 0;
      this._current = this._position < this._data.Length ? this._data[this._position] : 0;
      ++this._position;
      if (this._position > this._data.Length + _LOOKAHEAD_BYTES)
        throw new InvalidDataException("WhyPic stream ends before its picture does.");
    }

    var bit = (this._current >> 7) & 1;
    this._current = (this._current << 1) & 0xFF;
    ++this._bitInByte;
    return bit;
  }

  /// <summary>Which of <paramref name="total"/> equal steps the code value falls in.</summary>
  /// <remarks>
  /// For a total of 2^24 the rounding of the product can land exactly on <paramref name="total"/>;
  /// callers deal with that case themselves, as the format does.
  /// </remarks>
  public readonly int Target(int total) {
    var range = (double)(this._high - this._low) + 1;
    var value = (((double)(this._code - this._low) + 1) * total - 1) / range;
    if (value < 0 || value > total)
      throw new InvalidDataException("WhyPic stream is corrupt: the code value left its interval.");
    return (int)value;
  }

  /// <summary>Narrows the interval to [<paramref name="low"/>, <paramref name="high"/>) out of <paramref name="total"/>.</summary>
  public void Narrow(int low, int high, int total) {
    if (low < 0 || high <= low || high > total)
      throw new InvalidDataException("WhyPic stream is corrupt: a symbol decoded to an empty interval.");

    this._mirror?.Encode(low, high, total);
    var range = (double)(this._high - this._low) + 1;
    this._high = (uint)(range * high / total - 1) + this._low;
    this._low += (uint)(range * low / total);

    while (((this._high ^ this._low) & 0x80000000) == 0) {
      this._low <<= 1;
      this._high = (this._high << 1) | 1;
      this._code = (this._code << 1) + (uint)this._NextBit();
    }

    while ((this._low & 0x40000000) != 0 && (this._high & 0x40000000) == 0) {
      this._low = (this._low << 1) & 0x7FFFFFFF;
      this._high = (this._high << 1) | 0x80000001;
      this._code = ((this._code << 1) ^ 0x80000000) + (uint)this._NextBit();
    }
  }

  /// <summary>Reads a value spread evenly over <paramref name="total"/> outcomes.</summary>
  public int Uniform(int total) {
    var value = this.Target(total);
    if (value >= total)
      throw new InvalidDataException("WhyPic stream is corrupt: a fixed-width field overflowed.");
    this.Narrow(value, value + 1, total);
    return value;
  }
}
