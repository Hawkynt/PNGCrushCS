using System.IO;

namespace FileFormat.WhyPic;

/// <summary>The encoding half of <see cref="WhyPicRangeDecoder"/>: the same narrowing, emitting what the decoder shifts in.</summary>
/// <remarks>
/// A shift taken while the bounds straddle the middle cannot say which way it will settle, so it is
/// counted and paid for later, inverted, after the next bit that does settle.
/// </remarks>
internal sealed class WhyPicRangeEncoder {

  private readonly MemoryStream _output = new();
  private uint _low;
  private uint _high = uint.MaxValue;
  private int _pending;
  private int _byte;
  private int _bits;

  private void _PutBit(bool one) {
    this._byte = (this._byte << 1) | (one ? 1 : 0);
    if (++this._bits != 8)
      return;

    this._output.WriteByte((byte)this._byte);
    this._byte = 0;
    this._bits = 0;
  }

  /// <summary>Narrows the interval to [<paramref name="low"/>, <paramref name="high"/>) out of <paramref name="total"/>.</summary>
  public void Encode(int low, int high, int total) {
    if (low < 0 || high <= low || high > total)
      throw new InvalidDataException($"WhyPic cannot code the empty interval [{low}, {high}) of {total}.");

    var range = (double)(this._high - this._low) + 1;
    this._high = (uint)(range * high / total - 1) + this._low;
    this._low += (uint)(range * low / total);

    if (((this._high ^ this._low) & 0x80000000) == 0) {
      this._PutBit((this._low & 0x80000000) != 0);
      for (; this._pending > 0; --this._pending)
        this._PutBit((this._low & 0x80000000) == 0);

      this._low <<= 1;
      this._high = (this._high << 1) | 1;
      while (((this._high ^ this._low) & 0x80000000) == 0) {
        this._PutBit((this._low & 0x80000000) != 0);
        this._low <<= 1;
        this._high = (this._high << 1) | 1;
      }
    }

    while ((this._low & 0x40000000) != 0 && (this._high & 0x40000000) == 0) {
      ++this._pending;
      this._low = (this._low << 1) & 0x7FFFFFFF;
      this._high = (this._high << 1) | 0x80000001;
    }
  }

  /// <summary>Writes a value spread evenly over <paramref name="total"/> outcomes.</summary>
  public void Uniform(int value, int total) => this.Encode(value, value + 1, total);

  /// <summary>Settles the interval and pads the last byte with zeros.</summary>
  public byte[] Finish() {
    ++this._pending;
    this._PutBit((this._low & 0x40000000) != 0);
    for (; this._pending > 0; --this._pending)
      this._PutBit((this._low & 0x40000000) == 0);

    if (this._bits > 0)
      this._output.WriteByte((byte)(this._byte << (8 - this._bits)));

    return this._output.ToArray();
  }
}
