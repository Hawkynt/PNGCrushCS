using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// The encoder-side counterpart of <see cref="Av1SymbolDecoder"/>: it takes CDFs in the
/// specification's cumulative form, hands the range coder the inverse form it wants, and applies
/// the same adaptation the decoder will apply so that the two stay in step symbol for symbol.
/// </summary>
internal sealed class Av1SymbolEncoder {

  private const int _CDF_PROB_TOP = 1 << 15;

  private readonly Av1RangeEncoder _writer = new();
  private readonly bool _disableCdfUpdate;

  public Av1SymbolEncoder(bool disableCdfUpdate) => this._disableCdfUpdate = disableCdfUpdate;

  /// <summary>Writes a symbol against a CDF slice and adapts it exactly as the decoder does.</summary>
  public void WriteSymbol(ushort[] cdf, int offset, int symbolCount, int symbol) {
    if ((uint)symbol >= (uint)symbolCount)
      throw new ArgumentOutOfRangeException(nameof(symbol));

    Span<ushort> inverse = stackalloc ushort[symbolCount];
    for (var i = 0; i < symbolCount; ++i)
      inverse[i] = (ushort)(_CDF_PROB_TOP - cdf[offset + i]);

    this._writer.WriteSymbol(symbol, inverse);
    if (!this._disableCdfUpdate)
      _UpdateCdf(cdf, offset, symbolCount, symbol);
  }

  /// <summary>
  /// Writes a symbol against a CDF the caller synthesised for this one write, in the same
  /// cumulative form. Such a CDF is never adapted, because there is nothing persistent behind it.
  /// </summary>
  public void WriteSymbolNoUpdate(ReadOnlySpan<ushort> cdf, int symbol) {
    Span<ushort> inverse = stackalloc ushort[cdf.Length];
    for (var i = 0; i < cdf.Length; ++i)
      inverse[i] = (ushort)(_CDF_PROB_TOP - cdf[i]);

    this._writer.WriteSymbol(symbol, inverse);
  }

  /// <summary>AV1 8.2.3 write_bool(): one equiprobable arithmetic-coded bit.</summary>
  public void WriteLiteralBit(int bit) => this._writer.WriteBit(bit);

  /// <summary>AV1 8.2.5 write_literal(n), most significant bit first.</summary>
  public void WriteLiteral(uint value, int bitCount) => this._writer.WriteLiteral(value, bitCount);

  /// <summary>AV1 5.11.39 write_golomb(): the exp-Golomb suffix on large coefficient levels.</summary>
  public void WriteGolomb(int value) {
    var x = value + 1;
    var length = 0;
    for (var probe = x; probe != 0; probe >>= 1)
      ++length;

    for (var i = 0; i < length - 1; ++i)
      this._writer.WriteBit(0);
    this._writer.WriteBit(1);
    for (var i = length - 2; i >= 0; --i)
      this._writer.WriteBit((x >> i) & 1);
  }

  /// <summary>Closes the arithmetic partition and returns its bytes.</summary>
  public byte[] Finish() => this._writer.Finish();

  private static void _UpdateCdf(ushort[] cdf, int offset, int symbolCount, int symbol) {
    // The same rule as Av1SymbolDecoder: keep the two implementations textually alike, because a
    // one-line divergence here desynchronises every symbol after it and nothing else notices.
    var count = cdf[offset + symbolCount];
    var rate = 3 + (count > 15 ? 1 : 0) + (count > 31 ? 1 : 0) + Math.Min(_FloorLog2((uint)symbolCount), 2);

    var target = 0;
    for (var i = 0; i < symbolCount - 1; ++i) {
      if (i == symbol)
        target = _CDF_PROB_TOP;

      int value = cdf[offset + i];
      value += target < value ? -((value - target) >> rate) : (target - value) >> rate;
      cdf[offset + i] = (ushort)value;
    }

    if (count < 32)
      cdf[offset + symbolCount] = (ushort)(count + 1);
  }

  private static int _FloorLog2(uint value) {
    var result = 0;
    while ((value >>= 1) != 0)
      ++result;
    return result;
  }
}
