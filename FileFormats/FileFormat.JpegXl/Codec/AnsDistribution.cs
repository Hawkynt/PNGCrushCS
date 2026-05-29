using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// rANS distribution for one entropy context, stored as a spec-conformant alias table
/// (ISO/IEC 18181-1 §C.2; libjxl's <c>AliasTable</c> in <c>ans_common.{h,cc}</c>).
///
/// <para>The total table range is fixed at <see cref="AnsTabSize"/> = 4096
/// (<c>ANS_LOG_TAB_SIZE = 12</c>) — it does NOT vary with <c>log_alpha_size</c>.
/// What varies is the alias-table entry count: <c>1 &lt;&lt; log_alpha_size</c>,
/// each entry covering <c>AnsTabSize / table_size</c> consecutive slots
/// (the "entry_size" or "bucket size").</para>
///
/// <para>Each entry holds at most two symbols: a primary up to <c>cutoff</c> and a
/// secondary above. Lookup splits the rANS-state remainder into bucket-index +
/// position-in-bucket, then uses <c>cutoff</c> to disambiguate primary vs. secondary.</para>
/// </summary>
internal sealed class AnsDistribution {

  /// <summary>JXL fixed rANS log-table-size: every distribution sums to 2^12 = 4096.</summary>
  public const int AnsLogTabSize = 12;

  /// <summary>JXL fixed rANS table size (4096 slots).</summary>
  public const int AnsTabSize = 1 << AnsLogTabSize;

  /// <summary>Per-bucket alias-table entry. Five fields per spec; the
  /// <c>freq1_xor_freq0</c> trick is libjxl's branchless lookup; we keep it
  /// for layout fidelity but the C# lookup is a normal branch.</summary>
  public readonly record struct Entry(
    int Cutoff,        // 0..entry_size; positions in [0, cutoff) → primary symbol i
    int RightValue,    // secondary symbol index when pos >= cutoff
    int Freq0,         // primary symbol frequency (matches distribution[i])
    int Offsets1,      // pre-decremented offset for secondary lookup: actual offset = pos + Offsets1
    int Freq1XorFreq0  // (freq[right_value] ^ freq0) — branchless XOR trick from libjxl
  );

  /// <summary>Power-of-two log of the alias-table entry count.</summary>
  public int LogAlphaSize { get; init; }

  /// <summary>Entry count = 1 &lt;&lt; LogAlphaSize.</summary>
  public int TableSize => 1 << LogAlphaSize;

  /// <summary>Slots per bucket = AnsTabSize / TableSize.</summary>
  public int EntrySize => AnsTabSize >> LogAlphaSize;

  /// <summary>Bit-shift used in lookup: log2(EntrySize) = AnsLogTabSize - LogAlphaSize.</summary>
  public int LogEntrySize => AnsLogTabSize - LogAlphaSize;

  /// <summary>Per-symbol frequencies (sums to AnsTabSize).</summary>
  public int[] Frequencies { get; init; } = [];

  /// <summary>Alias table — TableSize entries.</summary>
  public Entry[] Table { get; init; } = [];

  /// <summary>Number of symbols in the alphabet.</summary>
  public int AlphabetSize { get; init; }

  /// <summary>Reverse encoder lookup: for each symbol s, the AnsTabSize/freq[s]
  /// slots in [0, AnsTabSize) that <see cref="Lookup"/> maps to s. Indexed
  /// <c>[symbol][offset_within_freq] → r</c>. Built lazily on first access.</summary>
  private int[][]? _encoderSlotTable;

  /// <summary>Get the rANS slot in [0, AnsTabSize) corresponding to encoding
  /// <paramref name="symbol"/> with the given <paramref name="offsetWithinFreq"/>
  /// (0 ≤ offsetWithinFreq &lt; Frequencies[symbol]). Lazily builds the reverse
  /// table on first call. Required by <see cref="JxlAnsEncoder"/> because the
  /// spec-conformant alias-table layout is NOT cumulative-frequency-ordered,
  /// so the encoder cannot use a simple prefix-sum offset.</summary>
  public int EncoderSlot(int symbol, int offsetWithinFreq) {
    _encoderSlotTable ??= _BuildEncoderSlotTable();
    return _encoderSlotTable[symbol][offsetWithinFreq];
  }

  private int[][] _BuildEncoderSlotTable() {
    var slots = new int[AlphabetSize][];
    for (var s = 0; s < AlphabetSize; ++s)
      slots[s] = Frequencies[s] > 0 ? new int[Frequencies[s]] : System.Array.Empty<int>();
    var written = new int[AlphabetSize];
    for (var r = 0; r < AnsTabSize; ++r) {
      var (sym, off, _) = Lookup(r);
      // off is the position of r within sym's collective slot set;
      // that's exactly the index we need for inverse lookup.
      slots[sym][off] = r;
      ++written[sym];
    }
    return slots;
  }

  /// <summary>Read a distribution from the bitstream per ISO/IEC 18181-1 §C.3
  /// (libjxl <c>ReadHistogram</c> in <c>lib/jxl/dec_ans.cc</c>). Mode dispatch:
  /// <list type="bullet">
  ///   <item><c>1</c> → simple-symbol mode (1 or 2 symbols, both read as
  ///         <c>DecodeVarLenUint8</c>; 2-symbol carries an explicit
  ///         <c>precision_bits</c>=12 freq0).</item>
  ///   <item><c>01</c> → flat distribution; alphabet read as
  ///         <c>DecodeVarLenUint8 + 1</c>.</item>
  ///   <item><c>00</c> → explicit frequencies with a Pareto-style
  ///         <c>shift</c> precision parameter, run-length sentinel, and a
  ///         128-entry static prefix code over <c>logcount</c> values.</item>
  /// </list>
  /// </summary>
  public static AnsDistribution Read(JxlBitReader reader, int alphabetSize, int logAlphaSize) {
    ArgumentNullException.ThrowIfNull(reader);
    if (alphabetSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(alphabetSize));

    var simpleCode = reader.ReadBits(1);
    if (simpleCode == 1) {
      // Simple mode: 1 or 2 symbols, indices via DecodeVarLenUint8 (NOT log_alpha_size).
      var numSymbols = (int)reader.ReadBits(1) + 1;
      var sym0 = _DecodeVarLenUint8(reader);
      if (numSymbols == 1)
        return _BuildSingleSymbol(sym0, alphabetSize, logAlphaSize);
      var sym1 = _DecodeVarLenUint8(reader);
      if (sym0 == sym1)
        throw new System.IO.InvalidDataException("Simple two-symbol distribution: sym0 == sym1.");
      // freq0 is read as ANS_LOG_TAB_SIZE (=12) bits, NOT 4.
      var freq0 = (int)reader.ReadBits(AnsLogTabSize);
      var freq1 = AnsTabSize - freq0;
      if (freq0 < 0 || freq1 < 0 || freq0 > AnsTabSize)
        throw new System.IO.InvalidDataException(
          $"Simple two-symbol freq0={freq0} out of range [0,{AnsTabSize}].");
      return _BuildExplicitTwoSymbol(sym0, sym1, freq0, freq1, alphabetSize, logAlphaSize);
    }

    var isFlat = reader.ReadBits(1);
    if (isFlat == 1) {
      // Flat distribution. Alphabet count = DecodeVarLenUint8 + 1 (NOT log_alpha_size).
      var alphCount = _DecodeVarLenUint8(reader) + 1;
      if (alphCount > AnsTabSize)
        throw new System.IO.InvalidDataException(
          $"Flat distribution alphabet={alphCount} > ANS_TAB_SIZE={AnsTabSize}.");
      // The container alphabetSize (1<<log_alpha_size) is the maximum;
      // the actual symbol set size is alphCount, but Frequencies length
      // matches the runtime-known alphabetSize for indexing safety.
      var effective = System.Math.Min(alphCount, alphabetSize);
      return _BuildFlat(effective, logAlphaSize);
    }

    return _ReadExplicitFrequencies(reader, alphabetSize, logAlphaSize);
  }

  /// <summary>Build a flat (uniform) distribution.</summary>
  public static AnsDistribution BuildFlat(int alphabetSize, int logAlphaSize)
    => _BuildFlat(alphabetSize, logAlphaSize);

  /// <summary>Build a distribution from explicit per-symbol frequencies. Frequencies must sum to AnsTabSize.</summary>
  public static AnsDistribution FromFrequencies(int[] frequencies, int logAlphaSize) {
    ArgumentNullException.ThrowIfNull(frequencies);
    var sum = 0;
    foreach (var f in frequencies) sum += f;
    if (sum != AnsTabSize)
      throw new ArgumentException($"Distribution frequencies must sum to {AnsTabSize}, got {sum}.", nameof(frequencies));
    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(frequencies, AnsLogTabSize, logAlphaSize, table);
    return new() {
      LogAlphaSize = logAlphaSize,
      AlphabetSize = frequencies.Length,
      Frequencies = (int[])frequencies.Clone(),
      Table = table,
    };
  }

  // ============================================================
  // libjxl two-stack alias-table construction (lib/jxl/ans_common.cc::InitAliasTable)
  // ============================================================

  /// <summary>Build the alias table for the given distribution. <paramref name="distribution"/>
  /// must sum to <c>1 &lt;&lt; logRange</c> and have at most <c>1 &lt;&lt; logAlphaSize</c> entries.
  /// Output is written into <paramref name="table"/> (must have length <c>1 &lt;&lt; logAlphaSize</c>).</summary>
  internal static void InitAliasTable(int[] distribution, int logRange, int logAlphaSize, Entry[] table) {
    var range = 1 << logRange;
    var tableSize = 1 << logAlphaSize;
    if (table.Length != tableSize)
      throw new ArgumentException($"Table length {table.Length} != expected {tableSize}.", nameof(table));

    // Trim trailing zero-frequency symbols.
    var distLen = distribution.Length;
    while (distLen > 0 && distribution[distLen - 1] == 0) --distLen;
    // Empty distribution → fabricate a single-symbol covering the full range
    // (matches libjxl: prevents specially-crafted streams from crashing the decoder).
    var dist = distLen == 0 ? new[] { range } : distribution[..distLen];
    distLen = dist.Length;
    if (distLen > tableSize)
      throw new ArgumentException($"Distribution has {distLen} entries but table_size only {tableSize}.");

    var entrySize = range >> logAlphaSize;

    // Special case: if any symbol has the FULL frequency, all buckets map to it.
    // This guarantees the rANS state doesn't change when decoding from a single-symbol
    // distribution (a property libjxl preserves explicitly).
    var singleSymbol = -1;
    var sum = 0;
    for (var s = 0; s < distLen; ++s) {
      var v = dist[s];
      sum += v;
      if (v == range) {
        if (singleSymbol != -1)
          throw new InvalidOperationException("Multiple full-range symbols in distribution.");
        singleSymbol = s;
      }
    }
    if (sum != range)
      throw new ArgumentException($"Distribution sums to {sum}, expected {range}.");

    if (singleSymbol >= 0) {
      for (var i = 0; i < tableSize; ++i)
        table[i] = new Entry(
          Cutoff: 0,
          RightValue: singleSymbol,
          Offsets1: entrySize * i,
          Freq0: 0,
          Freq1XorFreq0: range
        );
      return;
    }

    // Two-stack rebalance.
    var cutoffs = new int[tableSize];
    var underfull = new System.Collections.Generic.Stack<int>();
    var overfull = new System.Collections.Generic.Stack<int>();
    var rightValue = new int[tableSize];
    var offsets1 = new int[tableSize];

    for (var i = 0; i < distLen; ++i) {
      cutoffs[i] = dist[i];
      rightValue[i] = i; // initially every bucket holds only its own symbol
      if (cutoffs[i] > entrySize) overfull.Push(i);
      else if (cutoffs[i] < entrySize) underfull.Push(i);
    }
    for (var i = distLen; i < tableSize; ++i) {
      cutoffs[i] = 0;
      rightValue[i] = i;
      underfull.Push(i);
    }

    while (overfull.Count > 0) {
      var oi = overfull.Pop();
      if (underfull.Count == 0)
        throw new InvalidOperationException("Alias-table balance failed: overfull stack non-empty but underfull empty.");
      var ui = underfull.Pop();
      var underfullBy = entrySize - cutoffs[ui];
      cutoffs[oi] -= underfullBy;
      // The slots past underfull's primary fill come from the END of overfull's symbols,
      // so the secondary's effective offset is (overfull's new cutoff) — i.e. the
      // count of overfull's symbol still resident in its own bucket.
      rightValue[ui] = oi;
      offsets1[ui] = cutoffs[oi];
      if (cutoffs[oi] < entrySize) underfull.Push(oi);
      else if (cutoffs[oi] > entrySize) overfull.Push(oi);
    }

    // Finalize.
    for (var i = 0; i < tableSize; ++i) {
      int cutoff;
      int rv;
      int off1;
      if (cutoffs[i] == entrySize) {
        // Bucket is exactly full of its own symbol; secondary unused.
        rv = i;
        cutoff = 0;
        off1 = 0;
      } else {
        rv = rightValue[i];
        cutoff = cutoffs[i];
        // libjxl decrements offsets1 by cutoff so that the lookup formula
        // is (pos + offsets1) directly without further subtraction.
        off1 = offsets1[i] - cutoff;
      }
      var freq0 = i < distLen ? dist[i] : 0;
      var i1 = rv;
      var freq1 = i1 < distLen ? dist[i1] : 0;
      table[i] = new Entry(
        Cutoff: cutoff,
        RightValue: rv,
        Offsets1: off1,
        Freq0: freq0,
        Freq1XorFreq0: freq1 ^ freq0
      );
    }
  }

  /// <summary>Spec-conformant lookup from rANS state remainder.
  /// Returns <c>(symbol, offset, freq)</c> matching libjxl's <c>AliasTable::Lookup</c>.</summary>
  public (int Symbol, int Offset, int Freq) Lookup(int value) {
    var i = value >> LogEntrySize;
    var pos = value & (EntrySize - 1);
    var entry = Table[i];
    var greater = pos >= entry.Cutoff;
    var symbol = greater ? entry.RightValue : i;
    var offset = greater ? entry.Offsets1 + pos : pos;
    var freq = greater ? entry.Freq0 ^ entry.Freq1XorFreq0 : entry.Freq0;
    return (symbol, offset, freq);
  }

  // ============================================================
  // Helpers — distribution builders for the simple cases.
  // Each constructs a frequency vector summing to AnsTabSize and feeds InitAliasTable.
  // ============================================================

  private static int _ReadSymbolIndex(JxlBitReader reader, int logAlphaSize) {
    // Spec: symbol index is fixed log_alpha_size bits (5..8).
    return (int)reader.ReadBits(logAlphaSize);
  }

  private static AnsDistribution _BuildSingleSymbol(int symbol, int alphabetSize, int logAlphaSize) {
    var freq = new int[alphabetSize];
    freq[symbol] = AnsTabSize;
    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(freq, AnsLogTabSize, logAlphaSize, table);
    return new() { LogAlphaSize = logAlphaSize, AlphabetSize = alphabetSize, Frequencies = freq, Table = table };
  }

  private static AnsDistribution _BuildTwoSymbol(int sym0, int sym1, int freqBits, int alphabetSize, int logAlphaSize) {
    // freqBits is currently a 4-bit value from the legacy Read path. Real spec
    // reads log_precision (12) bits; tracked as audit issue 3.10.
    var freq0 = Math.Min((freqBits + 1) << 8, AnsTabSize - 1);
    if (freq0 == 0) freq0 = 1;
    var freq1 = AnsTabSize - freq0;
    var freq = new int[alphabetSize];
    freq[sym0] = freq0;
    freq[sym1] = freq1;
    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(freq, AnsLogTabSize, logAlphaSize, table);
    return new() { LogAlphaSize = logAlphaSize, AlphabetSize = alphabetSize, Frequencies = freq, Table = table };
  }

  private static AnsDistribution _BuildFlat(int alphabetSize, int logAlphaSize) {
    var freq = new int[alphabetSize];
    var baseFreq = AnsTabSize / alphabetSize;
    var remainder = AnsTabSize - baseFreq * alphabetSize;
    for (var i = 0; i < alphabetSize; ++i)
      freq[i] = baseFreq + (i < remainder ? 1 : 0);
    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(freq, AnsLogTabSize, logAlphaSize, table);
    return new() { LogAlphaSize = logAlphaSize, AlphabetSize = alphabetSize, Frequencies = freq, Table = table };
  }

  /// <summary>
  /// Spec §C.3 explicit-frequencies path. Mirrors libjxl
  /// <c>ReadHistogram</c>'s "else" branch (`dec_ans.cc`):
  /// <list type="number">
  ///   <item>Read <c>shift</c> via the unary-prefix variable-width encoding
  ///         (<c>upper_bound_log = FloorLog2Nonzero(ANS_LOG_TAB_SIZE+1) = 3</c>).
  ///         <c>shift = (read_bits(log) | (1 &lt;&lt; log)) - 1</c>, range [0, 13].</item>
  ///   <item>Read <c>length = DecodeVarLenUint8 + 3</c> (size of the count vector,
  ///         capped at <paramref name="alphabetSize"/>).</item>
  ///   <item>For each symbol read a 7-bit peek into the static <c>huff[128][2]</c>
  ///         table → (<c>code_length</c>, <c>logcount</c>). <c>logcount = ANS_LOG_TAB_SIZE</c>
  ///         is the RLE sentinel: read <c>rle_length = DecodeVarLenUint8</c> and
  ///         repeat the previous count for <c>rle_length + 4</c> additional entries.</item>
  ///   <item>One symbol — <c>omit_pos</c>, the one with the largest <c>logcount</c>
  ///         that wasn't an RLE entry — is the "fill"; its count equals
  ///         <c>ANS_TAB_SIZE - sum(others)</c>. The frame after <c>omit_pos</c>
  ///         must not begin with an RLE entry (spec invariant).</item>
  ///   <item>For non-omit, non-RLE entries with <c>shift &gt; 0</c> and
  ///         <c>logcount &gt; 0</c>: read <c>bitcount = GetPopulationCountPrecision(logcount, shift)</c>
  ///         extra bits and reconstruct
  ///         <c>count = (1 &lt;&lt; logcount) + (extra &lt;&lt; (logcount - bitcount))</c>.</item>
  /// </list>
  /// </summary>
  private static AnsDistribution _ReadExplicitFrequencies(JxlBitReader reader, int alphabetSize, int logAlphaSize) {
    // (1) Read the shift parameter via unary-prefix coding.
    // upper_bound_log = FloorLog2Nonzero(ANS_LOG_TAB_SIZE + 1) = FloorLog2Nonzero(13) = 3.
    var upperBoundLog = _FloorLog2Nonzero(AnsLogTabSize + 1);
    var log = 0;
    while (log < upperBoundLog && reader.ReadBits(1) == 1)
      ++log;
    var shift = ((int)reader.ReadBits(log) | (1 << log)) - 1;
    if (shift > AnsLogTabSize + 1)
      throw new System.IO.InvalidDataException($"Invalid shift value {shift} > {AnsLogTabSize + 1}.");

    // (2) Length of the histogram (count vector).
    var length = _DecodeVarLenUint8(reader) + 3;
    if (length > alphabetSize)
      throw new System.IO.InvalidDataException(
        $"Histogram length {length} exceeds alphabet size {alphabetSize}.");

    var counts = new int[length];
    var logcounts = new int[length];
    var same = new int[length]; // RLE run lengths (0 = no RLE here)
    var omitLog = -1;
    var omitPos = -1;

    for (var i = 0; i < length; ++i) {
      _ = _DecodeStaticHuffPrefix(reader, out var lc);
      logcounts[i] = lc - 1;
      if (logcounts[i] == AnsLogTabSize) {
        // RLE sentinel. Spec: rle_length = DecodeVarLenUint8; replicate
        // the previous count for (rle_length + 4) entries total
        // (i.e. rle_length + 3 ADDITIONAL slots after this one).
        var rleLength = _DecodeVarLenUint8(reader);
        same[i] = rleLength + 5;
        i += rleLength + 3;
        if (i >= length)
          throw new System.IO.InvalidDataException(
            $"RLE run extends past histogram length ({i} >= {length}).");
        continue;
      }
      if (logcounts[i] > omitLog) {
        omitLog = logcounts[i];
        omitPos = i;
      }
    }

    if (omitPos < 0)
      throw new System.IO.InvalidDataException("Invalid histogram: no omit position found.");
    if (omitPos + 1 < length && logcounts[omitPos + 1] == AnsLogTabSize)
      throw new System.IO.InvalidDataException(
        "Invalid histogram: omit position immediately followed by RLE entry.");

    var totalCount = 0;
    var prev = 0;
    var numSame = 0;
    for (var i = 0; i < length; ++i) {
      if (same[i] != 0) {
        numSame = same[i] - 1;
        prev = i > 0 ? counts[i - 1] : 0;
      }
      if (numSame > 0) {
        counts[i] = prev;
        --numSame;
      } else {
        var code = logcounts[i];
        if (i == omitPos || code < 0)
          continue;
        if (shift == 0 || code == 0) {
          counts[i] = 1 << code;
        } else {
          var bitcount = _GetPopulationCountPrecision(code, shift);
          counts[i] = (1 << code) + ((int)reader.ReadBits(bitcount) << (code - bitcount));
        }
      }
      totalCount += counts[i];
    }

    counts[omitPos] = AnsTabSize - totalCount;
    if (counts[omitPos] <= 0)
      throw new System.IO.InvalidDataException(
        $"Invalid histogram: omit-fill count {counts[omitPos]} is non-positive (over-allocated).");

    // Pad up to alphabetSize so downstream indexing is uniform regardless of
    // declared length. Trailing zeros are trimmed inside InitAliasTable.
    int[] freq;
    if (length == alphabetSize) {
      freq = counts;
    } else {
      freq = new int[alphabetSize];
      System.Array.Copy(counts, freq, length);
    }

    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(freq, AnsLogTabSize, logAlphaSize, table);
    return new() { LogAlphaSize = logAlphaSize, AlphabetSize = alphabetSize, Frequencies = freq, Table = table };
  }

  /// <summary>Build a 2-symbol distribution with explicit (sym0, sym1) frequencies.
  /// Used by the simple-mode path where freq0 was read directly from the stream.</summary>
  private static AnsDistribution _BuildExplicitTwoSymbol(int sym0, int sym1, int freq0, int freq1, int alphabetSize, int logAlphaSize) {
    if ((uint)sym0 >= (uint)alphabetSize || (uint)sym1 >= (uint)alphabetSize)
      throw new System.IO.InvalidDataException(
        $"Simple two-symbol indices out of range: sym0={sym0} sym1={sym1} alphabet={alphabetSize}.");
    var freq = new int[alphabetSize];
    freq[sym0] = freq0;
    freq[sym1] = freq1;
    var table = new Entry[1 << logAlphaSize];
    InitAliasTable(freq, AnsLogTabSize, logAlphaSize, table);
    return new() { LogAlphaSize = logAlphaSize, AlphabetSize = alphabetSize, Frequencies = freq, Table = table };
  }

  /// <summary>libjxl <c>DecodeVarLenUint8</c>: 1-bit prefix; if 0 → returns 0; if 1
  /// reads a 3-bit <c>nbits</c>. <c>nbits == 0</c> → returns 1; else returns
  /// <c>ReadBits(nbits) + (1 &lt;&lt; nbits)</c>. Range: [0, 255], cost: 1-11 bits.</summary>
  private static int _DecodeVarLenUint8(JxlBitReader reader) {
    if (reader.ReadBits(1) == 0)
      return 0;
    var nbits = (int)reader.ReadBits(3);
    if (nbits == 0)
      return 1;
    return (int)reader.ReadBits(nbits) + (1 << nbits);
  }

  /// <summary>Decode one symbol from the static 128-entry histogram-prefix
  /// Huffman table. Bit-at-a-time matching against the libjxl
  /// <c>huff[128][2]</c> table from <c>dec_ans.cc::ReadHistogram</c>. The
  /// table is keyed by the 7-bit LSB-first window; for our forward-only bit
  /// reader we instead consume bits incrementally and match prefixes.
  /// <para>Returns <c>(consume_bits, logcount + 1)</c> matching the libjxl
  /// table — caller subtracts 1 from the second element to get the actual
  /// <c>logcount</c>.</para></summary>
  private static int _DecodeStaticHuffPrefix(JxlBitReader reader, out int logcountPlus1) {
    // We accumulate up to 7 bits and check against all 128 entries in the
    // table. The first match (with the smallest code length covering the
    // accumulated window) wins. Since codes are prefix-unique, the first
    // entry whose code_length ≤ accumulated_bits AND whose 7-bit pattern
    // matches in the low code_length bits is the answer.
    uint window = 0;
    var bitsHeld = 0;
    for (var step = 0; step < 7; ++step) {
      window |= reader.ReadBits(1) << step;
      ++bitsHeld;
      // Check if any table entry with code_length == bitsHeld matches our window.
      for (var idx = 0; idx < 128; ++idx) {
        var consume = _StaticHistHuff[idx, 0];
        if (consume != bitsHeld)
          continue;
        // The table is indexed by a 7-bit value 'idx'; the relevant code is
        // the low 'consume' bits of idx (LSB-first). Match if window == low bits of idx.
        var mask = (1u << consume) - 1;
        if (((uint)idx & mask) != window)
          continue;
        logcountPlus1 = _StaticHistHuff[idx, 1];
        return consume;
      }
    }
    throw new System.IO.InvalidDataException("Static histogram-prefix Huffman code did not match any table entry.");
  }

  /// <summary>libjxl <c>GetPopulationCountPrecision</c> (`ans_common.h`):
  /// returns the number of extra bits used to refine a histogram count
  /// when the encoder chose precision <paramref name="shift"/>. The clamp
  /// to ≥ 0 reflects the libjxl signed comparison.</summary>
  private static int _GetPopulationCountPrecision(int logcount, int shift) {
    var r = System.Math.Min(logcount, shift - ((AnsLogTabSize - logcount) >> 1));
    return r < 0 ? 0 : r;
  }

  /// <summary>libjxl <c>FloorLog2Nonzero</c>: position of the most-significant 1-bit.</summary>
  private static int _FloorLog2Nonzero(int value) {
    if (value <= 0)
      throw new System.ArgumentOutOfRangeException(nameof(value), "Must be positive.");
    var v = value;
    var n = 0;
    while ((v >>= 1) != 0) ++n;
    return n;
  }

  // ============================================================
  // Static prefix code over logcount values (libjxl `huff[128][2]`,
  // `dec_ans.cc::ReadHistogram`). Indexed by a 7-bit LSB-first peek; each
  // entry is (code_length, logcount + 1). Extracted verbatim from libjxl.
  // ============================================================
  private static readonly int[,] _StaticHistHuff = new int[128, 2] {
    {3, 10}, {7, 12}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {6, 11}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {7, 13}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {6, 11}, {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
    {3, 10}, {5, 0},  {3, 7}, {4, 3}, {3, 6}, {3, 8}, {3, 9}, {4, 5},
    {3, 10}, {4, 4},  {3, 7}, {4, 1}, {3, 6}, {3, 8}, {3, 9}, {4, 2},
  };

  private static int _Log2Ceil(int value) {
    if (value <= 1) return 0;
    var bits = 0;
    var v = value - 1;
    while (v > 0) { ++bits; v >>= 1; }
    return bits;
  }
}
