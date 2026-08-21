using System;
using System.IO;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// The other entropy coder FFV1 has: Golomb-Rice codes with a per-context adaptive parameter
/// (RFC 9043 §3.8.2).
/// </summary>
/// <remarks>
/// Faster than the range coder and slightly worse at it, which is why the specification keeps both.
/// A context here is four running numbers rather than thirty-two states: how far the predictions
/// have drifted, how big the errors have been, a bias, and a count. The Rice parameter is worked out
/// from the last two before every symbol, so the code adapts without anything being transmitted.
/// <para/>
/// The bits are read most significant first out of the plain byte stream, not out of the range
/// coder — but the slice header before them is range coded, so where they start is decided by how
/// far that coder read.
/// </remarks>
internal sealed class Ffv1GolombDecoder {

  /// <summary>How long a run each index stands for, as an exponent (RFC 9043 §3.8.2.2).</summary>
  /// <remarks>The same table JPEG-LS uses, which is where FFV1's run mode comes from.</remarks>
  private static ReadOnlySpan<byte> _Log2Run => [
    0, 0, 0, 0, 1, 1, 1, 1,
    2, 2, 2, 2, 3, 3, 3, 3,
    4, 4, 5, 5, 6, 6, 7, 7,
    8, 9, 10, 11, 12, 13, 14, 15,
    16, 17, 18, 19, 20, 21, 22, 23,
    24,
  ];

  private readonly ReadOnlyMemory<byte> _data;
  private int _position;

  internal Ffv1GolombDecoder(ReadOnlyMemory<byte> data, int startByte) {
    this._data = data;
    this._position = startByte * 8;
  }

  internal int Bit() {
    if (this._position >= this._data.Length * 8)
      throw new InvalidDataException("A slice ran out of bits before its samples did.");

    var bit = (this._data.Span[this._position >> 3] >> (7 - (this._position & 7))) & 1;
    ++this._position;
    return bit;
  }

  internal int Bits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.Bit();

    return value;
  }

  /// <summary>The length of a run at an index, as the number of bits that state it.</summary>
  internal static int Log2Run(int index) => _Log2Run[Math.Min(index, _Log2Run.Length - 1)];

  /// <summary>The largest run index there is a length for.</summary>
  internal static int MaximumRunIndex => _Log2Run.Length - 1;

  /// <summary>
  /// One unsigned Golomb-Rice code: a unary prefix, then <paramref name="k"/> bits, with an escape.
  /// </summary>
  /// <remarks>
  /// Twelve zero bits are the escape, and what follows it is the value less eleven written out in
  /// full. Without it a single wild sample could cost thousands of bits.
  /// </remarks>
  internal int UnsignedGolomb(int k, int bits) {
    for (var prefix = 0; prefix < 12; ++prefix)
      if (this.Bit() != 0)
        return this.Bits(k) + (prefix << k);

    return this.Bits(bits) + 11;
  }

  /// <summary>The same code with the sign folded into the low bit, as a zigzag.</summary>
  internal int SignedGolomb(int k, int bits) {
    var value = this.UnsignedGolomb(k, bits);
    return (value & 1) != 0 ? -(value >> 1) - 1 : value >> 1;
  }

  /// <summary>
  /// One sample difference, with the context's four numbers deciding the code and then moving.
  /// </summary>
  /// <remarks>
  /// The Rice parameter is the smallest <i>k</i> for which the count doubled <i>k</i> times reaches
  /// the accumulated error, which is a running estimate of the size of the differences this context
  /// produces. The bias correction and the halving at 128 are the part that keeps it tracking rather
  /// than averaging over the whole picture.
  /// </remarks>
  internal int Symbol(Ffv1GolombState state, int bits) {
    var i = state.Count;
    var k = 0;
    while (i < state.ErrorSum) {
      ++k;
      i += i;
    }

    var value = this.SignedGolomb(k, bits);
    if (2 * state.Drift < -state.Count)
      value = -1 - value;

    var result = _SignExtend(value + state.Bias, bits);

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

    return result;
  }

  private static int _SignExtend(int value, int bits) {
    var negative = 1 << (bits - 1);
    var masked = value & (negative - 1);
    return (value & negative) != 0 ? masked - negative : masked;
  }
}

/// <summary>One Golomb-Rice context: what it has seen and how it is leaning (RFC 9043 §3.8.2.5).</summary>
internal sealed class Ffv1GolombState {

  internal int Drift;
  internal int ErrorSum = 4;
  internal int Bias;
  internal int Count = 1;

  internal void Reset() {
    this.Drift = 0;
    this.ErrorSum = 4;
    this.Bias = 0;
    this.Count = 1;
  }
}
