using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>
/// AV1 multi-symbol arithmetic decoder (specification section 8.2). A CDF is an array of cumulative
/// probabilities scaled to 32768 with the terminating 32768 included; the entry one past the last
/// symbol is the saturating adaptation counter the update process in 8.2.6 keeps.
/// </summary>
internal sealed class Av1SymbolDecoder {

  private const uint _CDF_PROB_TOP = 1u << 15;
  private const int _EC_PROB_SHIFT = 6;
  private const uint _EC_MIN_PROB = 4;

  private readonly byte[] _data;
  private readonly int _endBitOffset;
  private readonly bool _disableCdfUpdate;
  private int _bitOffset;

  private uint _range;
  private uint _value;
  private int _maxBits;

  public Av1SymbolDecoder(byte[] data, int offset, int length, bool disableCdfUpdate) {
    ArgumentNullException.ThrowIfNull(data);
    if (offset < 0 || length < 0 || offset > data.Length - length)
      throw new ArgumentOutOfRangeException(nameof(offset));

    this._data = data;
    this._bitOffset = offset * 8;
    this._endBitOffset = (offset + length) * 8;
    this._disableCdfUpdate = disableCdfUpdate;

    // AV1 8.2.2 init_symbol().
    var numBits = Math.Min(length * 8, 15);
    var buffer = this._ReadBits(numBits);
    this._value = (_CDF_PROB_TOP - 1) ^ (buffer << (15 - numBits));
    this._range = _CDF_PROB_TOP;
    this._maxBits = length * 8 - 15;
  }

  /// <summary>AV1 8.2.6 read_symbol() over a CDF slice, with the adaptive update applied.</summary>
  public int ReadSymbol(ushort[] cdf, int offset, int symbolCount) {
    var symbol = this._Decode(cdf.AsSpan(offset, symbolCount), symbolCount);
    if (!this._disableCdfUpdate)
      _UpdateCdf(cdf, offset, symbolCount, symbol);
    return symbol;
  }

  /// <summary>
  /// AV1 8.2.6 read_symbol() over a CDF the caller synthesised for this one read. The partition
  /// edge cases build such a CDF by folding several symbols together, and folded CDFs are never
  /// adapted because there is nothing persistent to adapt.
  /// </summary>
  public int ReadSymbolNoUpdate(ReadOnlySpan<ushort> cdf, int symbolCount) => this._Decode(cdf, symbolCount);

  /// <summary>AV1 8.2.3 read_bool(): one equiprobable arithmetic-coded bit.</summary>
  public int ReadLiteralBit() {
    ReadOnlySpan<ushort> cdf = [1 << 14, (ushort)_CDF_PROB_TOP];
    return this._Decode(cdf, 2);
  }

  /// <summary>AV1 8.2.5 read_literal(n), most significant bit first.</summary>
  public uint ReadLiteral(int n) {
    var result = 0u;
    for (var i = 0; i < n; ++i)
      result = (result << 1) | (uint)this.ReadLiteralBit();
    return result;
  }

  /// <summary>AV1 4.10.7 NS(n): a value in [0, n) coded with as few bits as possible.</summary>
  public uint ReadNs(uint n) {
    if (n <= 1)
      return 0;

    var w = 0;
    while ((1u << w) < n)
      ++w;

    var m = (1u << w) - n;
    var v = this.ReadLiteral(w - 1);
    return v < m ? v : ((v << 1) - m + this.ReadLiteral(1));
  }

  /// <summary>AV1 5.9.27 decode_signed_subexp_with_ref_bool(), used by the loop-restoration parameters.</summary>
  public int ReadSignedSubexpWithRef(int low, int high, int k, int reference) =>
    this._ReadUnsignedSubexpWithRef(high - low, k, reference - low) + low;

  /// <summary>AV1 5.11.39 read_golomb(): the exp-Golomb suffix on large coefficient levels.</summary>
  public int ReadGolomb() {
    var length = 0;
    var bit = 0;
    while (bit == 0) {
      bit = this.ReadLiteralBit();
      ++length;

      // libaom rejects a run this long as a corrupt stream rather than shifting past 32 bits.
      if (length > 20)
        throw new InvalidDataException("AV1: coefficient Golomb prefix exceeds 20 bits.");
    }

    var x = 1;
    for (var i = 0; i < length - 1; ++i)
      x = (x << 1) + this.ReadLiteralBit();
    return x - 1;
  }

  private int _ReadUnsignedSubexpWithRef(int mx, int k, int r) {
    var v = this._ReadSubexp(mx, k);
    return r << 1 <= mx ? _InverseRecenter(r, v) : mx - 1 - _InverseRecenter(mx - 1 - r, v);
  }

  private int _ReadSubexp(int numSyms, int k) {
    var i = 0;
    var mk = 0;
    while (true) {
      var b2 = i != 0 ? k + i - 1 : k;
      var a = 1 << b2;
      if (numSyms <= mk + 3 * a)
        return (int)this.ReadNs((uint)(numSyms - mk)) + mk;

      if (this.ReadLiteralBit() != 0) {
        ++i;
        mk += a;
      } else
        return (int)this.ReadLiteral(b2) + mk;
    }
  }

  private static int _InverseRecenter(int r, int v) {
    if (v > 2 * r)
      return v;
    if ((v & 1) != 0)
      return r - ((v + 1) >> 1);
    return r + (v >> 1);
  }

  private int _Decode(ReadOnlySpan<ushort> cdf, int symbolCount) {
    // AV1 8.2.6: walk the CDF until the arithmetic value falls inside an interval.
    var cur = this._range;
    uint prev;
    var symbol = -1;
    do {
      ++symbol;
      if (symbol >= symbolCount)
        throw new InvalidDataException("AV1: arithmetic decoder left the supplied CDF.");

      prev = cur;
      var f = _CDF_PROB_TOP - cdf[symbol];
      cur = ((this._range >> 8) * (f >> _EC_PROB_SHIFT)) >> (7 - _EC_PROB_SHIFT);
      cur += _EC_MIN_PROB * (uint)(symbolCount - symbol - 1);
    } while (this._value < cur);

    this._range = prev - cur;
    this._value -= cur;
    this._Renormalize();
    return symbol;
  }

  private static void _UpdateCdf(ushort[] cdf, int offset, int symbolCount, int symbol) {
    // AV1 8.2.6 adaptive update. cdf[symbolCount - 1] stays 32768; the slot after it counts how
    // often this CDF has been used and slows adaptation down as it saturates.
    var count = cdf[offset + symbolCount];
    var rate = 3 + (count > 15 ? 1 : 0) + (count > 31 ? 1 : 0) + Math.Min(_FloorLog2((uint)symbolCount), 2);

    var target = 0;
    for (var i = 0; i < symbolCount - 1; ++i) {
      if (i == symbol)
        target = (int)_CDF_PROB_TOP;

      int value = cdf[offset + i];
      value += target < value ? -((value - target) >> rate) : (target - value) >> rate;
      cdf[offset + i] = (ushort)value;
    }

    if (count < 32)
      cdf[offset + symbolCount] = (ushort)(count + 1);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void _Renormalize() {
    // AV1 8.2.6 renormalization. Once the physical bits run out the decoder keeps going on
    // synthesized zeroes, which is normative: a tile's final symbols may sit past its last byte.
    var bits = 15 - _FloorLog2(this._range);
    this._range <<= bits;

    var numBits = Math.Min(bits, Math.Max(0, this._maxBits));
    var newData = this._ReadBits(numBits);
    this._value = (newData << (bits - numBits)) ^ (((this._value + 1) << bits) - 1);
    this._maxBits -= bits;
  }

  private uint _ReadBits(int count) {
    var result = 0u;
    for (var i = 0; i < count; ++i) {
      if (this._bitOffset >= this._endBitOffset)
        throw new EndOfStreamException("AV1: arithmetic partition ended while reading symbol data.");

      result = (result << 1) | (uint)((this._data[this._bitOffset >> 3] >> (7 - (this._bitOffset & 7))) & 1);
      ++this._bitOffset;
    }
    return result;
  }

  private static int _FloorLog2(uint value) {
    var result = 0;
    while ((value >>= 1) != 0)
      ++result;
    return result;
  }
}
