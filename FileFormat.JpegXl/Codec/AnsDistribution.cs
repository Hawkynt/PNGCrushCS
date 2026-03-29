using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Represents a single rANS distribution (probability table) for one entropy context.
/// JPEG XL uses rANS with log-bucket-size distribution tables where the total
/// probability sums to a power of 2.
/// </summary>
internal sealed class AnsDistribution {

  /// <summary>Log2 of the distribution table size (bucket count).</summary>
  public int LogBucketSize { get; init; }

  /// <summary>Total table size (1 &lt;&lt; LogBucketSize).</summary>
  public int TableSize => 1 << LogBucketSize;

  /// <summary>Per-symbol frequencies (summing to TableSize).</summary>
  public int[] Frequencies { get; init; } = [];

  /// <summary>Per-symbol cumulative frequencies (prefix sums).</summary>
  public int[] CumulativeFreqs { get; init; } = [];

  /// <summary>Alias table: for each table slot, the symbol it maps to.</summary>
  public int[] Symbols { get; init; } = [];

  /// <summary>Alias table: offset within the bucket for disambiguation.</summary>
  public int[] Offsets { get; init; } = [];

  /// <summary>Alias table: cutoff threshold within each bucket.</summary>
  public int[] Cutoffs { get; init; } = [];

  /// <summary>Number of symbols in the alphabet.</summary>
  public int AlphabetSize { get; init; }

  /// <summary>
  /// Read a distribution from the bitstream for the given alphabet size.
  /// Supports the JPEG XL distribution coding modes:
  /// - Direct (flat) distribution
  /// - Single-symbol distribution (all weight on one symbol)
  /// - Explicit frequency table with optional RLE
  /// </summary>
  public static AnsDistribution Read(JxlBitReader reader, int alphabetSize, int logBucketSize) {
    ArgumentNullException.ThrowIfNull(reader);
    if (alphabetSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(alphabetSize));

    // distribution mode: 0=flat, 1=single symbol
    if (reader.ReadBool()) {
      // single symbol or special distribution
      if (reader.ReadBool()) {
        // two-symbol distribution
        var sym0 = _ReadSymbolIndex(reader, alphabetSize);
        var sym1 = _ReadSymbolIndex(reader, alphabetSize);
        var freqBits = reader.ReadBits(4);
        return _BuildTwoSymbol(sym0, sym1, (int)freqBits, logBucketSize, alphabetSize);
      }
      // single symbol: all probability on one symbol
      var symbol = _ReadSymbolIndex(reader, alphabetSize);
      return _BuildSingleSymbol(symbol, logBucketSize, alphabetSize);
    }

    // Check for flat distribution
    if (reader.ReadBool())
      return _BuildFlat(alphabetSize, logBucketSize);

    // Explicit frequency table
    return _ReadExplicitFrequencies(reader, alphabetSize, logBucketSize);
  }

  /// <summary>Build a distribution where all symbols have equal probability.</summary>
  public static AnsDistribution BuildFlat(int alphabetSize, int logBucketSize) =>
    _BuildFlat(alphabetSize, logBucketSize);

  /// <summary>Build a distribution from explicit frequency array.</summary>
  public static AnsDistribution FromFrequencies(int[] frequencies, int logBucketSize) {
    var tableSize = 1 << logBucketSize;
    var alphabetSize = frequencies.Length;
    var cumulative = new int[alphabetSize + 1];
    for (var i = 0; i < alphabetSize; ++i)
      cumulative[i + 1] = cumulative[i] + frequencies[i];

    var symbols = new int[tableSize];
    var offsets = new int[tableSize];
    var cutoffs = new int[tableSize];
    _BuildAliasTable(frequencies, cumulative, alphabetSize, tableSize, symbols, offsets, cutoffs);

    return new() {
      LogBucketSize = logBucketSize,
      AlphabetSize = alphabetSize,
      Frequencies = frequencies,
      CumulativeFreqs = cumulative,
      Symbols = symbols,
      Offsets = offsets,
      Cutoffs = cutoffs,
    };
  }

  private static int _ReadSymbolIndex(JxlBitReader reader, int alphabetSize) {
    if (alphabetSize <= 1)
      return 0;

    var bits = _Log2Ceil(alphabetSize);
    return (int)reader.ReadBits(bits);
  }

  private static AnsDistribution _BuildSingleSymbol(int symbol, int logBucketSize, int alphabetSize) {
    var tableSize = 1 << logBucketSize;
    var frequencies = new int[alphabetSize];
    frequencies[symbol] = tableSize;
    var cumulative = new int[alphabetSize + 1];
    for (var i = 0; i < alphabetSize; ++i)
      cumulative[i + 1] = cumulative[i] + frequencies[i];

    var symbols = new int[tableSize];
    var offsets = new int[tableSize];
    var cutoffs = new int[tableSize];
    Array.Fill(symbols, symbol);

    return new() {
      LogBucketSize = logBucketSize,
      AlphabetSize = alphabetSize,
      Frequencies = frequencies,
      CumulativeFreqs = cumulative,
      Symbols = symbols,
      Offsets = offsets,
      Cutoffs = cutoffs,
    };
  }

  private static AnsDistribution _BuildTwoSymbol(int sym0, int sym1, int freqBits, int logBucketSize, int alphabetSize) {
    var tableSize = 1 << logBucketSize;
    var freq0 = freqBits + 1;
    if (freq0 > tableSize)
      freq0 = tableSize;
    var freq1 = tableSize - freq0;

    var frequencies = new int[alphabetSize];
    frequencies[sym0] = freq0;
    frequencies[sym1] = freq1;

    var cumulative = new int[alphabetSize + 1];
    for (var i = 0; i < alphabetSize; ++i)
      cumulative[i + 1] = cumulative[i] + frequencies[i];

    var symbols = new int[tableSize];
    var offsets = new int[tableSize];
    var cutoffs = new int[tableSize];
    _BuildAliasTable(frequencies, cumulative, alphabetSize, tableSize, symbols, offsets, cutoffs);

    return new() {
      LogBucketSize = logBucketSize,
      AlphabetSize = alphabetSize,
      Frequencies = frequencies,
      CumulativeFreqs = cumulative,
      Symbols = symbols,
      Offsets = offsets,
      Cutoffs = cutoffs,
    };
  }

  private static AnsDistribution _BuildFlat(int alphabetSize, int logBucketSize) {
    var tableSize = 1 << logBucketSize;
    var frequencies = new int[alphabetSize];
    var baseFreq = tableSize / alphabetSize;
    var remainder = tableSize - baseFreq * alphabetSize;

    for (var i = 0; i < alphabetSize; ++i)
      frequencies[i] = baseFreq + (i < remainder ? 1 : 0);

    var cumulative = new int[alphabetSize + 1];
    for (var i = 0; i < alphabetSize; ++i)
      cumulative[i + 1] = cumulative[i] + frequencies[i];

    var symbols = new int[tableSize];
    var offsets = new int[tableSize];
    var cutoffs = new int[tableSize];
    _BuildAliasTable(frequencies, cumulative, alphabetSize, tableSize, symbols, offsets, cutoffs);

    return new() {
      LogBucketSize = logBucketSize,
      AlphabetSize = alphabetSize,
      Frequencies = frequencies,
      CumulativeFreqs = cumulative,
      Symbols = symbols,
      Offsets = offsets,
      Cutoffs = cutoffs,
    };
  }

  private static AnsDistribution _ReadExplicitFrequencies(JxlBitReader reader, int alphabetSize, int logBucketSize) {
    var tableSize = 1 << logBucketSize;
    var frequencies = new int[alphabetSize];
    var remaining = tableSize;

    for (var i = 0; i < alphabetSize && remaining > 0; ++i) {
      if (i == alphabetSize - 1) {
        frequencies[i] = remaining;
        break;
      }
      var bits = _Log2Ceil(remaining + 1);
      var freq = (int)reader.ReadBits(bits);
      if (freq > remaining)
        freq = remaining;
      frequencies[i] = freq;
      remaining -= freq;
    }

    var cumulative = new int[alphabetSize + 1];
    for (var i = 0; i < alphabetSize; ++i)
      cumulative[i + 1] = cumulative[i] + frequencies[i];

    var symbols = new int[tableSize];
    var offsets = new int[tableSize];
    var cutoffs = new int[tableSize];
    _BuildAliasTable(frequencies, cumulative, alphabetSize, tableSize, symbols, offsets, cutoffs);

    return new() {
      LogBucketSize = logBucketSize,
      AlphabetSize = alphabetSize,
      Frequencies = frequencies,
      CumulativeFreqs = cumulative,
      Symbols = symbols,
      Offsets = offsets,
      Cutoffs = cutoffs,
    };
  }

  /// <summary>
  /// Build an alias table for O(1) rANS decoding.
  /// Maps each table slot to a symbol, with cutoff/offset for sub-bucket disambiguation.
  /// </summary>
  private static void _BuildAliasTable(int[] frequencies, int[] cumulative, int alphabetSize, int tableSize, int[] symbols, int[] offsets, int[] cutoffs) {
    var pos = 0;
    for (var s = 0; s < alphabetSize; ++s)
      for (var j = 0; j < frequencies[s]; ++j) {
        if (pos < tableSize) {
          symbols[pos] = s;
          offsets[pos] = j;
          cutoffs[pos] = frequencies[s];
        }
        ++pos;
      }
  }

  private static int _Log2Ceil(int value) {
    if (value <= 1)
      return 0;
    var bits = 0;
    var v = value - 1;
    while (v > 0) {
      ++bits;
      v >>= 1;
    }
    return bits;
  }
}
