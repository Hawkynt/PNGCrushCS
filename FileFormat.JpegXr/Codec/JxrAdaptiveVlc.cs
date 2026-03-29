using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Adaptive Variable-Length Coding (VLC) engine for JPEG XR (ITU-T T.832).
/// Provides context-adaptive entropy coding for DC, LP, and HP coefficient bands.
/// </summary>
/// <remarks>
/// JPEG XR uses a set of predefined Huffman-like VLC tables that adapt based
/// on the distribution of recently decoded symbols. The adaptation mechanism
/// selects among multiple table sets depending on whether the data is
/// concentrated around small or large values.
///
/// The codec uses three independent VLC contexts:
/// - DC band: typically small residuals after DPCM prediction
/// - LP band: moderate-magnitude lowpass coefficients
/// - HP band: larger-magnitude highpass coefficients (or zero for smooth regions)
///
/// Each context maintains its own adaptation state and can switch between
/// table indices to best match the local statistics.
/// </remarks>
internal sealed class JxrAdaptiveVlcEngine {

  /// <summary>Number of predefined VLC table sets.</summary>
  private const int _TABLE_COUNT = 4;

  /// <summary>Number of symbols between adaptation checks.</summary>
  private const int _ADAPT_INTERVAL = 16;

  /// <summary>Maximum code prefix bits for table lookup.</summary>
  private const int _MAX_PREFIX_BITS = 8;

  /// <summary>Table lookup size (2^_MAX_PREFIX_BITS).</summary>
  private const int _TABLE_SIZE = 1 << _MAX_PREFIX_BITS;

  private readonly JxrVlcContext _dcContext;
  private readonly JxrVlcContext _lpContext;
  private readonly JxrVlcContext _hpContext;

  public JxrAdaptiveVlcEngine() {
    _dcContext = new(0); // DC starts with small-value table
    _lpContext = new(1); // LP starts with moderate table
    _hpContext = new(2); // HP starts with larger-value table
  }

  /// <summary>Decodes a DC-band coefficient from the bitstream.</summary>
  public int DecodeDc(JxrBitReader reader) => _dcContext.Decode(reader);

  /// <summary>Decodes an LP-band coefficient from the bitstream.</summary>
  public int DecodeLp(JxrBitReader reader) => _lpContext.Decode(reader);

  /// <summary>Decodes an HP-band coefficient from the bitstream.</summary>
  public int DecodeHp(JxrBitReader reader) => _hpContext.Decode(reader);

  /// <summary>Encodes a DC-band coefficient to the bitstream.</summary>
  public void EncodeDc(JxrBitWriter writer, int value) => _dcContext.Encode(writer, value);

  /// <summary>Encodes an LP-band coefficient to the bitstream.</summary>
  public void EncodeLp(JxrBitWriter writer, int value) => _lpContext.Encode(writer, value);

  /// <summary>Encodes an HP-band coefficient to the bitstream.</summary>
  public void EncodeHp(JxrBitWriter writer, int value) => _hpContext.Encode(writer, value);

  /// <summary>Resets all VLC contexts to their initial state (e.g., at tile boundaries).</summary>
  public void Reset() {
    _dcContext.Reset(0);
    _lpContext.Reset(1);
    _hpContext.Reset(2);
  }

  /// <summary>Resets only the HP context (useful at macroblock row boundaries).</summary>
  public void ResetHp() => _hpContext.Reset(2);
}

/// <summary>
/// A single adaptive VLC context that tracks symbol statistics and switches
/// between predefined table sets.
/// </summary>
internal sealed class JxrVlcContext {

  private const int _ADAPT_INTERVAL = 16;
  private const int _TABLE_COUNT = 4;

  private int _tableIndex;
  private int _symbolCount;
  private long _absSum;

  /// <summary>Current table index for diagnostics.</summary>
  internal int TableIndex => _tableIndex;

  public JxrVlcContext(int initialTableIndex) {
    _tableIndex = Math.Clamp(initialTableIndex, 0, _TABLE_COUNT - 1);
  }

  /// <summary>
  /// Decodes a single signed coefficient from the bitstream.
  /// Format: unary magnitude prefix + optional suffix bits + sign bit.
  /// </summary>
  public int Decode(JxrBitReader reader) {
    if (reader.IsEof)
      return 0;

    var absValue = _DecodeAbsValue(reader);

    // Sign bit for non-zero values
    var sign = absValue > 0 && !reader.IsEof ? reader.ReadBit() : 0;
    var value = sign != 0 ? -absValue : absValue;

    _AdaptAfterDecode(absValue);
    return value;
  }

  /// <summary>
  /// Encodes a single signed coefficient to the bitstream.
  /// </summary>
  public void Encode(JxrBitWriter writer, int value) {
    var absValue = Math.Abs(value);

    _EncodeAbsValue(writer, absValue);

    // Sign bit for non-zero values
    if (absValue > 0)
      writer.WriteBit(value < 0 ? 1 : 0);

    _AdaptAfterDecode(absValue);
  }

  /// <summary>Resets the adaptation state.</summary>
  public void Reset(int initialTableIndex) {
    _tableIndex = Math.Clamp(initialTableIndex, 0, _TABLE_COUNT - 1);
    _symbolCount = 0;
    _absSum = 0;
  }

  /// <summary>
  /// Decodes the absolute value using a table-index-dependent VLC scheme.
  /// The table index controls the crossover point between short and long codes.
  /// </summary>
  private int _DecodeAbsValue(JxrBitReader reader) {
    // Threshold depends on the current table index: higher table = larger expected values
    var threshold = _tableIndex + 2;

    // Read unary prefix: count of 1-bits before the first 0-bit
    var unary = 0;
    while (!reader.IsEof && reader.ReadBit() == 1)
      ++unary;

    if (unary < threshold)
      return unary;

    // For values >= threshold, read suffix bits
    // Number of suffix bits = unary - threshold + 1
    var suffixBits = unary - threshold + 1;
    if (suffixBits > 24)
      suffixBits = 24; // safety cap

    var suffix = reader.IsEof ? 0 : (int)reader.ReadBits(suffixBits);
    return threshold + (1 << suffixBits) - 2 + suffix;
  }

  /// <summary>Encodes the absolute value using the current table's VLC scheme.</summary>
  private void _EncodeAbsValue(JxrBitWriter writer, int absValue) {
    var threshold = _tableIndex + 2;

    if (absValue < threshold) {
      // Short code: unary(absValue) + terminator
      writer.WriteUnary(absValue);
    } else {
      // Long code: unary(threshold+suffixBits-1) + suffix bits
      var remainder = absValue - threshold + 2;
      var suffixBits = _Log2Plus1(remainder) - 1;
      if (suffixBits < 1)
        suffixBits = 1;

      var prefix = threshold + suffixBits - 1;
      writer.WriteUnary(prefix);

      var suffixValue = absValue - threshold - (1 << suffixBits) + 2;
      writer.WriteBits((uint)Math.Max(suffixValue, 0), suffixBits);
    }
  }

  /// <summary>Adapts the table index based on running symbol statistics.</summary>
  private void _AdaptAfterDecode(int absValue) {
    _absSum += absValue;
    ++_symbolCount;

    if (_symbolCount < _ADAPT_INTERVAL)
      return;

    var average = _absSum / _symbolCount;
    _tableIndex = average switch {
      < 2 => 0,
      < 4 => 1,
      < 8 => 2,
      _ => 3
    };

    _symbolCount = 0;
    _absSum = 0;
  }

  /// <summary>Returns ceil(log2(value+1)) for non-negative values.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _Log2Plus1(int value) {
    var bits = 0;
    var v = value;
    while (v > 0) {
      v >>= 1;
      ++bits;
    }
    return bits;
  }
}

/// <summary>
/// Run-length coding helpers for HP coefficient coding in JPEG XR.
/// HP blocks often have many zero coefficients; run-length coding compactly
/// represents the position and count of non-zero values.
/// </summary>
internal static class JxrRunLengthVlc {

  /// <summary>
  /// Decodes a run-length coded HP block from the bitstream.
  /// Returns the number of non-zero coefficients decoded.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the HP block data.</param>
  /// <param name="coefficients">Output span for the decoded coefficients (15 elements for positions 1..15).</param>
  /// <param name="hpContext">VLC context for HP magnitude decoding.</param>
  /// <returns>Count of non-zero coefficients decoded.</returns>
  public static int DecodeHpBlock(JxrBitReader reader, Span<int> coefficients, JxrVlcContext hpContext) {
    if (reader.IsEof) {
      coefficients.Clear();
      return 0;
    }

    coefficients.Clear();
    var nonZeroCount = 0;

    // Read the number of non-zero coefficients (0..15)
    var numNonZero = _DecodeSmallCount(reader);
    if (numNonZero == 0)
      return 0;

    var pos = 0;
    for (var i = 0; i < numNonZero && pos < coefficients.Length; ++i) {
      // Decode run of zeros before this coefficient (always encoded)
      var run = reader.IsEof ? 0 : _DecodeSmallCount(reader);
      pos += run;

      if (pos >= coefficients.Length)
        break;

      // Decode the coefficient value
      coefficients[pos] = hpContext.Decode(reader);
      ++nonZeroCount;
      ++pos;
    }

    return nonZeroCount;
  }

  /// <summary>
  /// Encodes a run-length coded HP block to the bitstream.
  /// </summary>
  /// <param name="writer">Bit writer.</param>
  /// <param name="coefficients">15 HP coefficients (positions 1..15 of a 4x4 block).</param>
  /// <param name="hpContext">VLC context for HP magnitude encoding.</param>
  public static void EncodeHpBlock(JxrBitWriter writer, ReadOnlySpan<int> coefficients, JxrVlcContext hpContext) {
    // Count non-zero coefficients
    var numNonZero = 0;
    for (var i = 0; i < coefficients.Length; ++i)
      if (coefficients[i] != 0)
        ++numNonZero;

    _EncodeSmallCount(writer, numNonZero);
    if (numNonZero == 0)
      return;

    var lastPos = -1;
    for (var i = 0; i < coefficients.Length; ++i) {
      if (coefficients[i] == 0)
        continue;

      // Encode run of zeros (always encoded for every non-zero coefficient)
      var run = i - lastPos - 1;
      _EncodeSmallCount(writer, run);

      // Encode the coefficient
      hpContext.Encode(writer, coefficients[i]);
      lastPos = i;
    }
  }

  /// <summary>Decodes a small non-negative count using unary coding with a cap.</summary>
  private static int _DecodeSmallCount(JxrBitReader reader) {
    var count = 0;
    while (count < 15 && !reader.IsEof && reader.ReadBit() == 1)
      ++count;
    return count;
  }

  /// <summary>Encodes a small non-negative count using unary coding.</summary>
  private static void _EncodeSmallCount(JxrBitWriter writer, int count) {
    var clamped = Math.Clamp(count, 0, 15);
    for (var i = 0; i < clamped; ++i)
      writer.WriteBit(1);
    if (clamped < 15)
      writer.WriteBit(0);
  }
}
