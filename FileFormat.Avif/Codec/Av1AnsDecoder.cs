using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>Multi-symbol arithmetic (CDF-based) entropy decoder for AV1 coefficient coding.
/// AV1 uses a CDF-based multi-symbol arithmetic coder (not rANS, but similar in spirit).</summary>
internal sealed class Av1AnsDecoder {

  private const int _SYMBOL_CDF_PROB_BITS = 15;
  private const int _SYMBOL_CDF_PROB_TOP = 1 << _SYMBOL_CDF_PROB_BITS;
  private const int _CDF_UPDATE_RATE_SHIFT = 5;

  private readonly byte[] _data;
  private int _byteOffset;
  private readonly int _endByte;

  // Arithmetic coder state
  private uint _range;
  private uint _value;
  private int _count; // number of cached bits

  public Av1AnsDecoder(byte[] data, int offset, int length) {
    _data = data;
    _byteOffset = offset;
    _endByte = offset + length;

    // Initialize arithmetic coder state
    _range = (1u << 16) - 1; // 0xFFFF
    _value = 0;
    _count = -15;

    // Fill value from bitstream
    for (var i = 0; i < 2; ++i) {
      if (_byteOffset < _endByte) {
        _value = (_value << 8) | _data[_byteOffset++];
        _count += 8;
      }
    }
    _value = (_value << 1) & 0xFFFF;
    _range = (1u << 15);
    _count = -1;
  }

  /// <summary>Whether the decoder has reached the end.</summary>
  public bool IsAtEnd => _byteOffset >= _endByte && _count <= 0;

  /// <summary>Decodes a single boolean with probability p/32768.</summary>
  public bool DecodeBool(int prob) {
    var split = (uint)((_range * prob) >> 15);
    if (split == 0)
      split = 1;

    bool result;
    if (_value < split) {
      _range = split;
      result = false;
    } else {
      _range -= split;
      _value -= split;
      result = true;
    }

    _Renormalize();
    return result;
  }

  /// <summary>Decodes a single bit with equal probability.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public int DecodeLiteral() {
    var half = _range >> 1;
    int bit;
    if (_value < half) {
      _range = half;
      bit = 0;
    } else {
      _range -= half;
      _value -= half;
      bit = 1;
    }
    _Renormalize();
    return bit;
  }

  /// <summary>Reads n literal bits.</summary>
  public uint DecodeLiteralBits(int n) {
    var result = 0u;
    for (var i = n - 1; i >= 0; --i)
      result |= (uint)DecodeLiteral() << i;
    return result;
  }

  /// <summary>Decodes a symbol using a CDF (cumulative distribution function) table.
  /// The CDF array has nsymbs+1 entries where cdf[i] = cumulative prob of symbols &lt; i.
  /// cdf[0] is typically the total (e.g. 32768) and cdf[nsymbs] = 0.
  /// AV1 uses a descending CDF: cdf[0] >= cdf[1] >= ... >= cdf[n] = 0.</summary>
  public int DecodeSymbol(ushort[] cdf, int nsymbs) {
    // Normalize range into 15-bit space
    var cur = _range;
    var symbol = -1;
    var prevCdf = (uint)(cdf[0] >> 6);
    if (prevCdf == 0)
      prevCdf = 1;

    for (var i = 0; i < nsymbs; ++i) {
      var cdfVal = i < nsymbs - 1 ? (uint)(cdf[i + 1] >> 6) : 0u;
      var scaledPrev = (cur * prevCdf) >> 9;
      var scaledCur = (cur * cdfVal) >> 9;

      if (scaledPrev == 0)
        scaledPrev = 1;

      if (_value < scaledPrev && _value >= scaledCur) {
        symbol = i;
        _value -= scaledCur;
        _range = scaledPrev - scaledCur;
        if (_range == 0)
          _range = 1;
        break;
      }
      prevCdf = cdfVal;
    }

    if (symbol < 0) {
      symbol = nsymbs - 1;
      _range = 1;
      _value = 0;
    }

    _Renormalize();
    _UpdateCdf(cdf, nsymbs, symbol);

    return symbol;
  }

  /// <summary>Decodes a symbol using a simplified CDF-based method matching AV1 spec more closely.</summary>
  public int DecodeSymbolSimple(ushort[] cdf, int nsymbs) {
    // Simple method for small alphabets
    for (var i = 0; i < nsymbs - 1; ++i) {
      var prob = cdf[i];
      if (DecodeBool(prob >> 1))
        continue;

      _UpdateCdf(cdf, nsymbs, i);
      return i;
    }

    _UpdateCdf(cdf, nsymbs, nsymbs - 1);
    return nsymbs - 1;
  }

  /// <summary>Updates CDF values after decoding a symbol (exponential moving average).</summary>
  private static void _UpdateCdf(ushort[] cdf, int nsymbs, int symbol) {
    // AV1 uses exponential moving average CDF update
    var rate = 3 + (nsymbs > 2 ? 1 : 0) + (cdf[nsymbs] > 15 ? 1 : 0);

    // nsymbs position stores the count
    if (cdf[nsymbs] < 32)
      ++cdf[nsymbs];

    for (var i = 0; i < nsymbs; ++i) {
      var target = i < symbol ? (ushort)0 : _SYMBOL_CDF_PROB_TOP;
      cdf[i] = (ushort)(cdf[i] + ((target - cdf[i]) >> rate));
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Renormalize() {
    while (_range < (1u << 8)) {
      _range <<= 1;
      _value <<= 1;
      --_count;
      if (_count < 0) {
        if (_byteOffset < _endByte)
          _value |= _data[_byteOffset++];
        _count = 7;
      }
    }
  }
}
