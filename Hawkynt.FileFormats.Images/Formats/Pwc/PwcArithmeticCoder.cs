using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pwc;

/// <summary>The two counts behind one binary decision: how many zeros were seen, and how many decisions in all.</summary>
/// <remarks>Both start at one zero out of two, so a fresh context predicts an even split.</remarks>
internal struct PwcStatistic {

  public byte Zeros;
  public byte Total;

  public static PwcStatistic Fresh => new() { Zeros = 1, Total = 2 };

  /// <summary>A context that predicts zero with certainty; the symbol coder uses it for paths no value can take.</summary>
  public static PwcStatistic Certain => new() { Zeros = 1, Total = 1 };
}

/// <summary>
/// The carry-free binary arithmetic coder of the PWC bitstream: a 16-bit interval split by a count ratio taken
/// from a 256×256 table, emitting a byte whenever the two interval ends agree above their lowest byte.
/// </summary>
/// <remarks>
/// One instance either decodes or encodes; every decision method takes the bit to encode and returns the bit
/// that was coded, so a model can be written once and run in both directions.
/// <para/>
/// The interval ends are kept in 32 bits whose upper half is never cleared: both ends always agree there, so
/// the garbage cancels in every difference and comparison, exactly as in the shipped coder.
/// </remarks>
internal sealed class PwcArithmeticCoder {

  /// <summary>The split table: for <c>zeros</c> out of <c>total</c>, the share of the interval (out of 256, minus one) given to zero.</summary>
  /// <remarks>
  /// The shipped coder ships these 65536 bytes as a table; they are exactly a rounded ratio, which a test pins
  /// against the SHA-256 of the original table.
  /// </remarks>
  internal static readonly byte[] SplitTable = _BuildSplitTable();

  private readonly byte[]? _input;
  private int _position;
  private readonly List<byte>? _output;
  private readonly int _maxLps;
  private uint _low;
  private uint _high;
  private uint _value;

  private PwcArithmeticCoder(byte[]? input, int position, List<byte>? output, int maxLps) {
    this._input = input;
    this._position = position;
    this._output = output;
    this._maxLps = maxLps;
    this._high = output == null ? 0u : 0xFFFFu;
  }

  public static PwcArithmeticCoder ForDecoding(byte[] data, int offset, int maxLps) => new(data, offset, null, maxLps);

  public static PwcArithmeticCoder ForEncoding(List<byte> output, int maxLps) => new(null, 0, output, maxLps);

  public bool IsEncoding => this._output != null;

  /// <summary>Bytes the decoder has consumed so far, counted from the start of the buffer.</summary>
  public int Position => this._position;

  /// <summary>
  /// Whether a decode ended where its encoder did: the code value has come to rest on the interval's lower
  /// end, which is exactly what the encoder's two flush bytes pin it to.
  /// </summary>
  public bool EndedCleanly => (this._value & 0xFFFF) == (this._low & 0xFFFF);

  /// <summary>Codes a decision under a context that halves its counts once the less probable side exceeds the limit.</summary>
  public int Adaptive(ref PwcStatistic statistic, int bit) {
    bit = this._Code(statistic, bit);
    var total = (byte)(statistic.Total + 1);
    var zeros = bit == 0 ? (byte)(statistic.Zeros + 1) : statistic.Zeros;
    if ((zeros > this._maxLps && total - zeros > this._maxLps) || total == 0xFF) {
      total = (byte)(((total - 1) >> 1) + 1);
      zeros = (byte)(((zeros - 1) >> 1) + 1);
    }

    statistic.Total = total;
    statistic.Zeros = zeros;
    return bit;
  }

  /// <summary>Codes a decision under a context whose counts only halve when the total would overflow a byte.</summary>
  public int Fixed(ref PwcStatistic statistic, int bit) {
    bit = this._Code(statistic, bit);
    var zeros = statistic.Zeros;
    var total = (byte)(statistic.Total + 1);
    if (total == 0) {
      var ones = (byte)((byte)-zeros >> 1);
      zeros = (byte)((byte)(zeros + 1) >> 1);
      total = (byte)(ones + zeros + 1);
    }

    if (bit == 0)
      ++zeros;

    statistic.Total = total;
    statistic.Zeros = zeros;
    return bit;
  }

  /// <summary>Writes the two bytes that pin the final interval.</summary>
  public void Flush() {
    if (this._output == null)
      return;

    this._output.Add((byte)(this._low >> 8));
    this._output.Add((byte)this._low);
  }

  private int _Code(PwcStatistic statistic, int bit) {
    while ((this._high & 0xFFFFFF00u) <= this._low) {
      if (this._output != null)
        this._output.Add((byte)(this._high >> 8));
      else
        this._value = this._value << 8 | this._NextByte();

      this._low <<= 8;
      this._high = this._high << 8 | 0xFF;
    }

    var share = SplitTable[statistic.Zeros << 8 | statistic.Total] + 1u;
    var split = (share * (this._high - this._low) >> 8) + this._low;
    if (this._output == null)
      bit = this._value > split ? 1 : 0;

    if (bit != 0)
      this._low = split + 1;
    else
      this._high = split;

    return bit;
  }

  private uint _NextByte() {
    var input = this._input!;
    if (this._position >= input.Length)
      throw new InvalidDataException("PWC data ends inside the arithmetic-coded picture.");

    return input[this._position++];
  }

  private static byte[] _BuildSplitTable() {
    var table = new byte[256 * 256];
    for (var zeros = 1; zeros < 256; ++zeros)
    for (var total = 0; total < 256; ++total)
      table[zeros << 8 | total] = zeros >= total ? (byte)255 : (byte)Math.Max(0, (512 * zeros + total) / (2 * total) - 1);

    Array.Fill(table, (byte)255, 0, 256);
    return table;
  }
}
