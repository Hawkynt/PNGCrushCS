using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>
/// AV1 multi-symbol range encoder (AV1 section 8.2), used by tile syntax.
/// </summary>
/// <remarks>
/// This is a managed adaptation of the arithmetic writer used by rav1e/libaom. The storage and
/// carry-propagation logic follows rav1e's BSD-2-Clause <c>src/ec.rs</c>, while the public surface is
/// intentionally limited to the operations needed by a still-picture encoder.
///
/// CDFs use AV1's encoder-side inverse-Q15 representation: for N symbols, the span contains N
/// entries, monotonically non-increasing from at most 32768 to a final value whose probability part
/// is zero. The low six bits of that final entry may contain the adaptation counter and are ignored
/// by the probability calculation. A flat binary CDF is therefore <c>[16384, 0]</c>.
/// </remarks>
internal sealed class Av1RangeEncoder {

  private const uint _CDF_PROB_TOP = 1u << 15;
  private const int _EC_PROB_SHIFT = 6;
  private const uint _EC_MIN_PROB = 4;

  private readonly List<ushort> _preCarry = [];
  private ushort _range = 0x8000;
  private int _count = -9;
  private uint _low;
  private bool _finished;

  /// <summary>Writes one equiprobable arithmetic-coded bit.</summary>
  internal void WriteBit(int bit) {
    if ((uint)bit > 1)
      throw new ArgumentOutOfRangeException(nameof(bit));

    Span<ushort> cdf = stackalloc ushort[] { 16384, 0 };
    this.WriteSymbol(bit, cdf);
  }

  /// <summary>Writes a literal value, most-significant bit first.</summary>
  internal void WriteLiteral(uint value, int bitCount) {
    if (bitCount is < 0 or > 32)
      throw new ArgumentOutOfRangeException(nameof(bitCount));
    if (bitCount < 32 && value >= (1u << bitCount))
      throw new ArgumentOutOfRangeException(nameof(value));

    for (var bit = bitCount - 1; bit >= 0; --bit)
      this.WriteBit((int)((value >> bit) & 1));
  }

  /// <summary>
  /// Writes <paramref name="symbol"/> using an AV1 inverse Q15 CDF without adapting the supplied CDF.
  /// </summary>
  internal void WriteSymbol(int symbol, ReadOnlySpan<ushort> inverseCdf) {
    if (this._finished)
      throw new InvalidOperationException("The AV1 arithmetic partition has already been finalized.");
    if (inverseCdf.Length < 2 || inverseCdf.Length > 16)
      throw new ArgumentOutOfRangeException(nameof(inverseCdf));
    if ((uint)symbol >= (uint)inverseCdf.Length)
      throw new ArgumentOutOfRangeException(nameof(symbol));

    var previous = _CDF_PROB_TOP;
    for (var i = 0; i < inverseCdf.Length; ++i) {
      var value = i == inverseCdf.Length - 1
        ? (uint)(inverseCdf[i] & ~((1 << _EC_PROB_SHIFT) - 1))
        : inverseCdf[i];
      if (value > previous)
        throw new ArgumentException("AV1 inverse CDF entries must be non-increasing.", nameof(inverseCdf));
      previous = value;
    }

    var symbolsAtOrAbove = inverseCdf.Length - symbol;
    var high = symbol > 0 ? (uint)inverseCdf[symbol - 1] : _CDF_PROB_TOP;
    var low = symbol == inverseCdf.Length - 1
      ? 0u
      : (uint)inverseCdf[symbol];

    // The final entry's low six bits carry the adaptation count, not probability bits.
    if (symbol == inverseCdf.Length - 1)
      low &= ~((1u << _EC_PROB_SHIFT) - 1);

    this._Store((ushort)high, (ushort)low, (ushort)symbolsAtOrAbove);
  }

  /// <summary>
  /// Finalizes the arithmetic partition and returns the minimum byte sequence that identifies all
  /// symbols written so far regardless of following bytes.
  /// </summary>
  internal byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("The AV1 arithmetic partition has already been finalized.");
    this._finished = true;

    var low = this._low;
    var count = this._count;
    var pending = 10 + count;
    const uint mask = 0x3FFF;
    var end = ((low + mask) & ~mask) | (mask + 1);

    if (pending > 0) {
      uint keepMask = (1u << (count + 16)) - 1;
      while (true) {
        this._preCarry.Add((ushort)(end >> (count + 16)));
        end &= keepMask;
        pending -= 8;
        count -= 8;
        keepMask >>= 8;
        if (pending <= 0)
          break;
      }
    }

    var output = new byte[this._preCarry.Count];
    uint carry = 0;
    for (var i = this._preCarry.Count - 1; i >= 0; --i) {
      carry += this._preCarry[i];
      output[i] = (byte)carry;
      carry >>= 8;
    }

    return output;
  }

  private void _Store(ushort high, ushort low, ushort symbolsAtOrAbove) {
    var currentRange = (uint)this._range;
    if (currentRange < _CDF_PROB_TOP)
      throw new InvalidOperationException("AV1 arithmetic range lost normalization.");

    var scaledHigh = (((currentRange >> 8) * ((uint)high >> _EC_PROB_SHIFT))
                      >> (7 - _EC_PROB_SHIFT))
                     + _EC_MIN_PROB * symbolsAtOrAbove;
    if (high >= _CDF_PROB_TOP)
      scaledHigh = currentRange;

    var scaledLow = (((currentRange >> 8) * ((uint)low >> _EC_PROB_SHIFT))
                     >> (7 - _EC_PROB_SHIFT))
                    + _EC_MIN_PROB * (symbolsAtOrAbove - 1u);

    var intervalLow = currentRange - scaledHigh;
    var intervalRange = scaledHigh - scaledLow;
    if (intervalRange == 0 || intervalRange > ushort.MaxValue)
      throw new InvalidOperationException("AV1 arithmetic symbol produced an empty interval.");

    var accumulatedLow = this._low + intervalLow;
    var count = this._count;
    var shift = _LeadingZeroCount16((ushort)intervalRange);
    var nextCount = count + shift;

    if (nextCount >= 0) {
      count += 16;
      uint keepMask = (1u << count) - 1;
      if (nextCount >= 8) {
        this._preCarry.Add((ushort)(accumulatedLow >> count));
        accumulatedLow &= keepMask;
        count -= 8;
        keepMask >>= 8;
      }

      this._preCarry.Add((ushort)(accumulatedLow >> count));
      nextCount = count + shift - 24;
      accumulatedLow &= keepMask;
    }

    this._low = accumulatedLow << shift;
    this._range = (ushort)(intervalRange << shift);
    this._count = nextCount;
  }

  private static int _LeadingZeroCount16(ushort value) {
    if (value == 0)
      return 16;

    var count = 0;
    for (var bit = 0x8000; (value & bit) == 0; bit >>= 1)
      ++count;
    return count;
  }
}
