using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The values one entropy-coded block of the codestream carries, held until the
/// code that states them cheapest is known (ISO/IEC 18181-1 §C.1–C.5; libjxl
/// <c>lib/jxl/enc_ans.cc</c>).
/// </summary>
/// <remarks>
/// Every value is split into a token and a tail of raw bits. The token goes
/// through the prefix code and the tail does not, which is what keeps the
/// alphabet small enough to describe in a few dozen bits however large the
/// values get.
///
/// <para>The header and the tokens are not written together. A modular frame
/// states its histograms with the rest of its global setup and only then the
/// group header that the tokens follow, so the two halves are separate calls.</para>
///
/// <para>A frame cut into groups states one code for all of them and each
/// group's tokens at its own offset in the frame, so the values are kept in runs
/// — one run per group — that share the single histogram this block builds.</para>
/// </remarks>
internal sealed class JxlTokenStream {

  /// <summary>
  /// Where the token stops standing for the value itself. Below sixteen a value
  /// is its own token, which is where nearly every prediction residual lands;
  /// above it the token names a power of two and the remainder is raw bits.
  /// </summary>
  private const int _SplitExponent = 4;

  /// <summary>
  /// The values as they were added. The token and its tail are worked out again
  /// when they are written rather than kept alongside: a picture of several
  /// million samples would otherwise hold three lists where one does.
  /// </summary>
  private readonly List<uint> _values;

  /// <summary>Where each run of values starts. A block with no run stated is one run.</summary>
  private readonly List<int> _runStarts = [];

  private int[] _histogram = new int[32];
  private int _alphabetSize = 1;
  private JxlPrefixCode? _code;

  public JxlTokenStream(int capacity = 0) => _values = new List<uint>(capacity);

  /// <summary>How many runs have been begun.</summary>
  public int RunCount => _runStarts.Count;

  /// <summary>Begin a run of values that is written on its own.</summary>
  public void BeginRun() => _runStarts.Add(_values.Count);

  /// <summary>Add one unsigned value to the block.</summary>
  public void Add(uint value) {
    var token = _Token(value);
    if (token >= _histogram.Length)
      Array.Resize(ref _histogram, Math.Max(token + 1, _histogram.Length * 2));
    ++_histogram[token];
    if (token >= _alphabetSize)
      _alphabetSize = token + 1;

    _values.Add(value);
  }

  /// <summary>
  /// Write the block's header: how the values were split, and the code the
  /// tokens are stated in.
  /// </summary>
  /// <param name="writer">Where the header goes.</param>
  /// <param name="contextCount">How many contexts the reader will address. They
  /// all share one code here, but the reader still expects the map when there is
  /// more than one of them.</param>
  public void WriteHeader(JxlBitWriter writer, int contextCount) {
    ArgumentNullException.ThrowIfNull(writer);

    writer.WriteBool(false); // no back references

    // One code for every context, stated in the short form the reader reads
    // when the map is not entropy-coded.
    if (contextCount > 1) {
      writer.WriteBool(true); // simple map
      writer.WriteBits(0, 2); // zero bits per entry, so every context is cluster zero
    }

    writer.WriteBool(true); // prefix codes rather than the arithmetic coder

    // With prefix codes the alphabet is fifteen bits wide, so the split and the
    // two in-token bit counts are stated in four, three and three bits.
    writer.WriteBits(_SplitExponent, 4);
    writer.WriteBits(0, 3); // no most-significant bits inside the token
    writer.WriteBits(0, 3); // no least-significant bits inside the token

    _WriteVarLenUint16(writer, (uint)(_alphabetSize - 1));

    var counts = new int[_alphabetSize];
    Array.Copy(_histogram, counts, _alphabetSize);
    _code = JxlPrefixCode.Build(writer, counts);
  }

  /// <summary>Write every value in the block. <see cref="WriteHeader"/> comes first.</summary>
  public void WriteTokens(JxlBitWriter writer) => _Write(writer, 0, _values.Count);

  /// <summary>
  /// Write one run of the block, which is one group's worth of a frame stated in
  /// several. <see cref="WriteHeader"/> comes first, once, for all of them.
  /// </summary>
  public void WriteRun(JxlBitWriter writer, int run) {
    if (run < 0 || run >= _runStarts.Count)
      throw new ArgumentOutOfRangeException(nameof(run), $"This block has {_runStarts.Count} runs, so there is no run {run}.");

    var from = _runStarts[run];
    var to = run + 1 < _runStarts.Count ? _runStarts[run + 1] : _values.Count;
    _Write(writer, from, to);
  }

  private void _Write(JxlBitWriter writer, int from, int to) {
    ArgumentNullException.ThrowIfNull(writer);
    if (_code == null)
      throw new InvalidOperationException("The block's tokens cannot be written before its header.");

    for (var i = from; i < to; ++i) {
      var token = _Split(_values[i], out var tailBitCount, out var tail);
      _code.Write(writer, token);
      if (tailBitCount > 0)
        writer.WriteBits(tail, tailBitCount);
    }
  }

  /// <summary>The token a value is stated by, without its tail.</summary>
  private static int _Token(uint value) => _Split(value, out _, out _);

  /// <summary>
  /// Split a value into the token the prefix code carries and the raw tail that
  /// follows it (libjxl <c>HybridUintConfig::Encode</c>).
  /// </summary>
  private static int _Split(uint value, out int tailBitCount, out uint tail) {
    if (value < 1u << _SplitExponent) {
      tailBitCount = 0;
      tail = 0;
      return (int)value;
    }

    var exponent = _FloorLog2(value);
    tailBitCount = exponent;
    tail = value - (1u << exponent);
    return (1 << _SplitExponent) + (exponent - _SplitExponent);
  }

  /// <summary>libjxl <c>PackSigned</c>: the zigzag that folds negatives in
  /// between the positives so the entropy coder only ever sees unsigned values.</summary>
  public static uint PackSigned(int value) => (uint)((value << 1) ^ (value >> 31));

  /// <summary>libjxl <c>EncodeVarLenUint16</c>, the inverse of the reader's
  /// <c>DecodeVarLenUint16</c>: a presence bit, then a width, then the value
  /// below its leading one.</summary>
  private static void _WriteVarLenUint16(JxlBitWriter writer, uint value) {
    if (value == 0) {
      writer.WriteBool(false);
      return;
    }

    writer.WriteBool(true);
    if (value == 1) {
      writer.WriteBits(0, 4);
      return;
    }

    var bits = _FloorLog2(value);
    writer.WriteBits((uint)bits, 4);
    writer.WriteBits(value - (1u << bits), bits);
  }

  private static int _FloorLog2(uint value) {
    var bits = 0;
    while (value > 1) {
      ++bits;
      value >>= 1;
    }
    return bits;
  }
}
