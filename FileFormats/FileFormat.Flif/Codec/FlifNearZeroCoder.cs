using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Flif.Codec;

/// <summary>
/// Near-zero integer coder context for FLIF. Encodes integers that are likely near zero
/// using an adaptive bit-level scheme. Each leaf in the MANIAC tree holds one of these.
///
/// Encoding scheme for value v in range [min, max]:
///   1. If min == max, value is implicit (no bits needed).
///   2. Encode sign: if min &lt; 0 and max &gt; 0, encode whether v is positive, negative, or zero.
///   3. Encode magnitude using adaptive per-bit-position chances.
///   4. Each bit position has an adaptive chance table that learns from the data.
/// </summary>
internal sealed class FlifNearZeroContext {

  /// <summary>Number of chance entries (sign + exponent + mantissa positions).</summary>
  private const int _TABLE_SIZE = 32;

  /// <summary>Initial chance value (50/50 = 2048 out of 4096).</summary>
  private const int _INITIAL_CHANCE = 2048;

  /// <summary>Adaptation speed: how quickly chances adjust (higher = faster).</summary>
  private const int _ADAPT_SPEED = 24;

  /// <summary>Adaptive chance table: each entry is a probability out of 4096.</summary>
  private readonly int[] _chances;

  public FlifNearZeroContext() {
    _chances = new int[_TABLE_SIZE];
    for (var i = 0; i < _TABLE_SIZE; ++i)
      _chances[i] = _INITIAL_CHANCE;
  }

  /// <summary>Creates a deep copy of this context.</summary>
  public FlifNearZeroContext Clone() {
    var clone = new FlifNearZeroContext();
    Array.Copy(_chances, clone._chances, _TABLE_SIZE);
    return clone;
  }

  /// <summary>
  /// Decodes an integer in the range [min, max] using the range decoder.
  /// </summary>
  public int Decode(FlifRangeDecoder decoder, int min, int max) {
    if (min == max)
      return min;

    // Shift so we work with non-negative values: decode in [0, max-min], then add min
    var range = max - min;
    var shifted = _DecodeUnsigned(decoder, range);
    return min + shifted;
  }

  /// <summary>
  /// Encodes an integer in the range [min, max] using the range encoder.
  /// </summary>
  public void Encode(FlifRangeEncoder encoder, int value, int min, int max) {
    if (min == max)
      return;

    // Shift so we work with non-negative values: encode (value - min) in [0, max - min]
    var range = max - min;
    var shifted = value - min;
    _EncodeUnsigned(encoder, shifted, range);
  }

  /// <summary>Decodes an unsigned integer in [0, max] using adaptive per-bit chances.</summary>
  private int _DecodeUnsigned(FlifRangeDecoder decoder, int max) {
    if (max == 0)
      return 0;

    var bits = _BitLength(max);
    var value = 0;

    for (var i = bits - 1; i >= 0; --i) {
      var chanceIdx = Math.Min(bits - 1 - i, _TABLE_SIZE - 1);
      var bit = decoder.DecodeBit(_chances[chanceIdx]);
      _Adapt(chanceIdx, bit);
      value |= bit << i;

      if (value > max) {
        value = max;
        break;
      }
    }

    return Math.Min(value, max);
  }

  /// <summary>Encodes an unsigned integer in [0, max].</summary>
  private void _EncodeUnsigned(FlifRangeEncoder encoder, int value, int max) {
    if (max == 0)
      return;

    var bits = _BitLength(max);

    for (var i = bits - 1; i >= 0; --i) {
      var chanceIdx = Math.Min(bits - 1 - i, _TABLE_SIZE - 1);
      var bit = (value >> i) & 1;
      encoder.EncodeBit(bit, _chances[chanceIdx]);
      _Adapt(chanceIdx, bit);
    }
  }

  /// <summary>Adapts the chance at the given index toward the observed bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Adapt(int index, int bit) {
    if (bit != 0)
      _chances[index] += (4096 - _chances[index] + _ADAPT_SPEED / 2) / _ADAPT_SPEED;
    else
      _chances[index] -= (_chances[index] + _ADAPT_SPEED / 2) / _ADAPT_SPEED;

    // Clamp to valid range [1, 4095] to avoid degenerate probabilities
    _chances[index] = Math.Clamp(_chances[index], 1, 4095);
  }

  /// <summary>Returns the number of bits needed to represent a non-negative integer.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _BitLength(int value) {
    if (value <= 0)
      return 1;

    var bits = 0;
    var v = (uint)value;
    while (v > 0) {
      ++bits;
      v >>= 1;
    }

    return bits;
  }
}
