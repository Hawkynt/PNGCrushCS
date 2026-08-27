using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>
/// AV1 multi-symbol arithmetic decoder implementing the symbol parsing process from AV1 section 8.2.
/// CDF arrays contain N cumulative probabilities followed by the adaptation counter; cdf[N - 1]
/// is always 32768 and cdf[N] is the count used by the adaptive update process.
/// </summary>
internal sealed class Av1AnsDecoder {

  private const uint _CDF_PROB_TOP = 1u << 15;
  private const int _EC_PROB_SHIFT = 6;
  private const uint _EC_MIN_PROB = 4;

  private readonly byte[] _data;
  private readonly int _endBitOffset;
  private readonly bool _disableCdfUpdate;
  private int _bitOffset;

  // AV1 section 8.2 symbol decoder state.
  private uint _range;
  private uint _value;
  private int _maxBits;

  public Av1AnsDecoder(byte[] data, int offset, int length, bool disableCdfUpdate = false) {
    ArgumentNullException.ThrowIfNull(data);
    if (offset < 0 || length < 0 || offset > data.Length - length)
      throw new ArgumentOutOfRangeException(nameof(offset));

    _data = data;
    _bitOffset = checked(offset * 8);
    _endBitOffset = checked((offset + length) * 8);
    _disableCdfUpdate = disableCdfUpdate;

    // AV1 8.2.2 init_symbol(sz).
    var numBits = Math.Min(length * 8, 15);
    var buffer = _ReadBits(numBits);
    var paddedBuffer = buffer << (15 - numBits);
    _value = (_CDF_PROB_TOP - 1) ^ paddedBuffer;
    _range = _CDF_PROB_TOP;
    _maxBits = length * 8 - 15;
  }

  /// <summary>
  /// Whether all physical bytes in the arithmetic partition have been consumed. The AV1 arithmetic
  /// decoder may still synthesize zero padding after this point, so syntax must not use this as an
  /// alternative to its normative stopping condition.
  /// </summary>
  public bool IsAtEnd => _bitOffset >= _endBitOffset && _maxBits <= 0;

  /// <summary>Decodes a binary symbol whose zero-symbol cumulative probability is <paramref name="prob"/>/32768.</summary>
  public bool DecodeBool(int prob) {
    if (prob <= 0 || prob >= _CDF_PROB_TOP)
      throw new ArgumentOutOfRangeException(nameof(prob));

    ushort[] cdf = [(ushort)prob, (ushort)_CDF_PROB_TOP, 0];
    return DecodeSymbol(cdf, 2) != 0;
  }

  /// <summary>AV1 section 8.2.3 read_bool(): decodes one equiprobable arithmetic-coded bit.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeLiteral() {
    ushort[] cdf = [1 << 14, (ushort)_CDF_PROB_TOP, 0];
    return DecodeSymbol(cdf, 2);
  }

  /// <summary>AV1 section 8.2.5 read_literal(n), MSB first.</summary>
  public uint DecodeLiteralBits(int n) {
    if (n is < 0 or > 32)
      throw new ArgumentOutOfRangeException(nameof(n));

    var result = 0u;
    for (var i = 0; i < n; ++i)
      result = (result << 1) | (uint)DecodeLiteral();
    return result;
  }

  /// <summary>
  /// AV1 section 8.2.6 read_symbol(cdf). The first <paramref name="nsymbs"/> CDF entries describe
  /// the symbol distribution and cdf[nsymbs] stores the adaptive-use counter.
  /// </summary>
  public int DecodeSymbol(ushort[] cdf, int nsymbs) {
    ArgumentNullException.ThrowIfNull(cdf);
    if (nsymbs <= 1 || cdf.Length < nsymbs + 1)
      throw new ArgumentOutOfRangeException(nameof(nsymbs));
    if (cdf[nsymbs - 1] != _CDF_PROB_TOP)
      throw new ArgumentException("AV1 CDF must terminate at 32768 before its adaptation counter.", nameof(cdf));

    for (var i = 1; i < nsymbs; ++i)
      if (cdf[i] < cdf[i - 1])
        throw new ArgumentException("AV1 CDF entries must be non-decreasing.", nameof(cdf));

    // AV1 8.2.6: select the first interval for which SymbolValue >= cur.
    var cur = _range;
    uint prev;
    var symbol = -1;
    do {
      ++symbol;
      if (symbol >= nsymbs)
        throw new InvalidDataException("AV1 arithmetic decoder left the supplied CDF.");

      prev = cur;
      var f = _CDF_PROB_TOP - cdf[symbol];
      cur = ((_range >> 8) * (f >> _EC_PROB_SHIFT)) >> (7 - _EC_PROB_SHIFT);
      cur += _EC_MIN_PROB * (uint)(nsymbs - symbol - 1);
    } while (_value < cur);

    _range = prev - cur;
    _value -= cur;
    _Renormalize();

    if (!_disableCdfUpdate)
      _UpdateCdf(cdf, nsymbs, symbol);

    return symbol;
  }

  /// <summary>
  /// Compatibility entry point retained for callers written against the previous decoder. AV1 has
  /// one normative CDF symbol decoder, so the former approximate path now delegates to it.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeSymbolSimple(ushort[] cdf, int nsymbs) => DecodeSymbol(cdf, nsymbs);

  private static void _UpdateCdf(ushort[] cdf, int nsymbs, int symbol) {
    // AV1 8.2.6 adaptive CDF update. The final probability entry remains 32768; the entry after it
    // is the saturating adaptation counter.
    var rate = 3
      + (cdf[nsymbs] > 15 ? 1 : 0)
      + (cdf[nsymbs] > 31 ? 1 : 0)
      + Math.Min(_FloorLog2((uint)nsymbs), 2);

    var target = 0;
    for (var i = 0; i < nsymbs - 1; ++i) {
      if (i == symbol)
        target = (int)_CDF_PROB_TOP;

      var value = (int)cdf[i];
      if (target < value)
        value -= (value - target) >> rate;
      else
        value += (target - value) >> rate;
      cdf[i] = (ushort)value;
    }

    if (cdf[nsymbs] < 32)
      ++cdf[nsymbs];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Renormalize() {
    // AV1 8.2.6 ordered renormalization steps.
    var bits = 15 - _FloorLog2(_range);
    _range <<= bits;

    var numBits = Math.Min(bits, Math.Max(0, _maxBits));
    var newData = _ReadBits(numBits);
    var paddedData = newData << (bits - numBits);
    _value = paddedData ^ (((_value + 1) << bits) - 1);
    _maxBits -= bits;

    if (_range is < _CDF_PROB_TOP or > 0xFFFF || _value >= _range)
      throw new InvalidDataException("AV1 arithmetic decoder state is outside the section 8.2.6 invariants.");
  }

  private uint _ReadBits(int count) {
    var result = 0u;
    for (var i = 0; i < count; ++i) {
      if (_bitOffset >= _endBitOffset)
        throw new EndOfStreamException("AV1 arithmetic partition ended while reading symbol data.");

      var byteIndex = _bitOffset >> 3;
      var bitIndex = 7 - (_bitOffset & 7);
      result = (result << 1) | (uint)((_data[byteIndex] >> bitIndex) & 1);
      ++_bitOffset;
    }
    return result;
  }

  private static int _FloorLog2(uint value) {
    if (value == 0)
      throw new InvalidDataException("AV1 arithmetic range became zero.");

    var result = 0;
    while ((value >>= 1) != 0)
      ++result;
    return result;
  }
}
