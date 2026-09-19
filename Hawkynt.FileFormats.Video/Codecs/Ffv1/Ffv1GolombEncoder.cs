using System;

namespace FileFormat.Codecs.Ffv1;

/// <summary>Writes FFV1's adaptive Golomb-Rice code stream (RFC 9043 §3.8.2).</summary>
/// <remarks>
/// This is the inverse of <see cref="Ffv1GolombDecoder"/>. Bits are emitted most significant first;
/// each context derives its Rice parameter from the same four running statistics the decoder owns,
/// and then advances those statistics with the uncoded prediction error. The implementation follows
/// RFC 9043 directly; FFmpeg's LGPL-2.1-or-later FFV1 encoder is used as an interoperability oracle.
/// </remarks>
internal sealed class Ffv1GolombEncoder {

  private byte[] _bytes = new byte[4096];
  private int _position;

  /// <summary>Writes one bit, most significant bit first within a byte.</summary>
  internal void Bit(int value) {
    var byteIndex = this._position >> 3;
    if (byteIndex == this._bytes.Length)
      Array.Resize(ref this._bytes, this._bytes.Length * 2);

    if ((value & 1) != 0)
      this._bytes[byteIndex] |= (byte)(0x80 >> (this._position & 7));

    ++this._position;
  }

  /// <summary>Writes the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal void Bits(int count, int value) {
    for (var bit = count - 1; bit >= 0; --bit)
      this.Bit(value >> bit);
  }

  /// <summary>Writes one signed Golomb-Rice value using the stated Rice parameter.</summary>
  internal void SignedGolomb(int value, int k, int bits) {
    var mapped = value < 0 ? checked(-2 * value - 1) : checked(2 * value);
    var prefix = mapped >> k;

    if (prefix < 12) {
      for (var i = 0; i < prefix; ++i)
        this.Bit(0);

      this.Bit(1);
      this.Bits(k, mapped);
      return;
    }

    for (var i = 0; i < 12; ++i)
      this.Bit(0);

    this.Bits(bits, mapped - 11);
  }

  /// <summary>Writes one prediction error and advances its adaptive context.</summary>
  internal void Symbol(Ffv1GolombState state, int difference, int bits) {
    var value = _SignExtend(difference - state.Bias, bits);

    var scaledCount = state.Count;
    var k = 0;
    while (scaledCount < state.ErrorSum) {
      ++k;
      scaledCount += scaledCount;
    }

    var coded = 2 * state.Drift < -state.Count ? -1 - value : value;
    this.SignedGolomb(coded, k, bits);
    _Update(state, value);
  }

  /// <summary>Hands back the whole byte-aligned run, padding the last byte with zero bits.</summary>
  internal byte[] ToArray() => this._bytes.AsSpan(0, (this._position + 7) >> 3).ToArray();

  private static void _Update(Ffv1GolombState state, int value) {
    state.ErrorSum += Math.Abs(value);
    state.Drift += value;

    if (state.Count == 128) {
      state.Count >>= 1;
      state.Drift >>= 1;
      state.ErrorSum >>= 1;
    }

    ++state.Count;

    if (state.Drift <= -state.Count) {
      state.Bias = Math.Max(state.Bias - 1, -128);
      state.Drift = Math.Max(state.Drift + state.Count, -state.Count + 1);
    } else if (state.Drift > 0) {
      state.Bias = Math.Min(state.Bias + 1, 127);
      state.Drift = Math.Min(state.Drift - state.Count, 0);
    }
  }

  private static int _SignExtend(int value, int bits) {
    var sign = 1 << (bits - 1);
    var masked = value & (sign - 1);
    return (value & sign) != 0 ? masked - sign : masked;
  }
}
