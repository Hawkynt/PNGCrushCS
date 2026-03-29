using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Context-modeled integer decoder combining rANS with hybrid integer coding
/// and optional context clustering. JPEG XL uses "hybrid integers": a token
/// decoded via rANS followed by extra raw bits. The split point between token
/// and extra bits is configurable per cluster.
/// </summary>
internal sealed class JxlEntropyDecoder {

  private readonly AnsDistribution[] _distributions;
  private readonly int[] _clusterMap;
  private readonly int[] _splitExponent;
  private readonly int[] _msb;
  private readonly int[] _lsb;
  private readonly JxlAnsDecoder _ansDecoder;
  private readonly JxlBitReader _reader;
  private readonly bool _usePrefixCode;
  private readonly int[][] _prefixLengths;
  private readonly int[][] _prefixSymbols;
  private readonly bool _lz77Enabled;
  #pragma warning disable CS0649 // LZ77 fields assigned when LZ77 mode is fully implemented
  private int _lz77RepeatCount;
  private int _lz77RepeatValue;
  #pragma warning restore CS0649

  private JxlEntropyDecoder(
    AnsDistribution[] distributions,
    int[] clusterMap,
    int[] splitExponent,
    int[] msb,
    int[] lsb,
    JxlAnsDecoder ansDecoder,
    JxlBitReader reader,
    bool usePrefixCode,
    int[][] prefixLengths,
    int[][] prefixSymbols,
    bool lz77Enabled
  ) {
    _distributions = distributions;
    _clusterMap = clusterMap;
    _splitExponent = splitExponent;
    _msb = msb;
    _lsb = lsb;
    _ansDecoder = ansDecoder;
    _reader = reader;
    _usePrefixCode = usePrefixCode;
    _prefixLengths = prefixLengths;
    _prefixSymbols = prefixSymbols;
    _lz77Enabled = lz77Enabled;
  }

  /// <summary>
  /// Read the entropy coding configuration from the bitstream and create a decoder.
  /// Parses: use_prefix_codes flag, LZ77 config, cluster map, distributions, and
  /// hybrid integer configuration.
  /// </summary>
  public static JxlEntropyDecoder Read(JxlBitReader reader, int numContexts) {
    ArgumentNullException.ThrowIfNull(reader);
    if (numContexts <= 0)
      throw new ArgumentOutOfRangeException(nameof(numContexts));

    // LZ77 enabled flag
    var lz77Enabled = reader.ReadBool();
    if (lz77Enabled) {
      // Read LZ77 min_symbol and min_length (we skip detailed LZ77 for now)
      var _minSymbol = reader.ReadU32(224, 0, 512, 0, 4096, 0, 8, 15);
      var _minLength = reader.ReadU32(3, 0, 4, 0, 5, 2, 9, 8);
    }

    // Use prefix codes (Huffman) or rANS
    var usePrefixCode = reader.ReadBool();

    // Number of clusters
    var numClusters = 1;
    if (numContexts > 1)
      numClusters = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 1, 6);

    if (numClusters > numContexts)
      numClusters = numContexts;

    // Cluster map: maps each context to a cluster
    var clusterMap = new int[numContexts];
    if (numClusters > 1 && numContexts > 1)
      _ReadClusterMap(reader, clusterMap, numContexts, numClusters);

    // Hybrid integer config per cluster
    var splitExponent = new int[numClusters];
    var msb = new int[numClusters];
    var lsb = new int[numClusters];
    for (var c = 0; c < numClusters; ++c) {
      splitExponent[c] = (int)reader.ReadU32(0, 0, 4, 0, 8, 0, 0, 4);
      if (splitExponent[c] > 0) {
        msb[c] = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 0, 3);
        lsb[c] = (int)reader.ReadU32(0, 0, 1, 0, 2, 0, 0, 3);
      }
    }

    // Read distributions
    int[][] prefixLengths;
    int[][] prefixSymbols;
    AnsDistribution[] distributions;
    JxlAnsDecoder? ansDecoder = null;

    if (usePrefixCode) {
      prefixLengths = new int[numClusters][];
      prefixSymbols = new int[numClusters][];
      distributions = Array.Empty<AnsDistribution>();

      for (var c = 0; c < numClusters; ++c)
        (prefixLengths[c], prefixSymbols[c]) = _ReadPrefixCode(reader);
    } else {
      prefixLengths = Array.Empty<int[]>();
      prefixSymbols = Array.Empty<int[]>();

      // log_alpha_size for rANS
      var logAlphaSize = 5 + (int)reader.ReadBits(2);
      var logBucketSize = Math.Min(logAlphaSize, 8);

      distributions = new AnsDistribution[numClusters];
      for (var c = 0; c < numClusters; ++c)
        distributions[c] = AnsDistribution.Read(reader, 1 << logAlphaSize, logBucketSize);

      ansDecoder = new JxlAnsDecoder(reader);
      ansDecoder.Init();
    }

    return new(
      distributions,
      clusterMap,
      splitExponent,
      msb,
      lsb,
      ansDecoder!,
      reader,
      usePrefixCode,
      prefixLengths,
      prefixSymbols,
      lz77Enabled
    );
  }

  /// <summary>
  /// Create a simple prefix-code-based decoder for testing/encoding.
  /// Uses a flat distribution with direct symbol coding.
  /// </summary>
  public static JxlEntropyDecoder CreateSimple(JxlBitReader reader, int numContexts, int maxSymbol) {
    ArgumentNullException.ThrowIfNull(reader);
    var clusterMap = new int[numContexts];
    var splitExponent = new int[] { 0 };
    var msb = new int[] { 0 };
    var lsb = new int[] { 0 };

    var bits = _Log2Ceil(maxSymbol + 1);
    var lengths = new int[maxSymbol + 1];
    var symbols = new int[maxSymbol + 1];
    for (var i = 0; i <= maxSymbol; ++i) {
      lengths[i] = bits;
      symbols[i] = i;
    }

    return new(
      Array.Empty<AnsDistribution>(),
      clusterMap,
      splitExponent,
      msb,
      lsb,
      null!,
      reader,
      true,
      new[] { lengths },
      new[] { symbols },
      false
    );
  }

  /// <summary>
  /// Read one integer value using the given context.
  /// Resolves the context to a cluster, decodes a token via rANS or prefix code,
  /// then reads any extra bits for the hybrid integer representation.
  /// </summary>
  public int ReadInt(int context) {
    if (_lz77Enabled && _lz77RepeatCount > 0) {
      --_lz77RepeatCount;
      return _lz77RepeatValue;
    }

    var cluster = context < _clusterMap.Length ? _clusterMap[context] : 0;
    int token;

    if (_usePrefixCode)
      token = _ReadPrefixSymbol(cluster);
    else
      token = _ansDecoder.ReadSymbol(_distributions[cluster]);

    return _ReadHybridInt(token, cluster);
  }

  /// <summary>
  /// Decode a hybrid integer from a token and extra bits.
  /// The split exponent determines how many bits of the token are "direct"
  /// vs how many extra bits follow in the bitstream.
  /// </summary>
  private int _ReadHybridInt(int token, int cluster) {
    var split = cluster < _splitExponent.Length ? _splitExponent[cluster] : 0;

    if (split == 0 || token < (1 << split))
      return token;

    var nExtra = split + ((token - (1 << split)) >> (split > 0 ? split - 1 : 0));
    if (nExtra > 30)
      nExtra = 30;

    var msbVal = cluster < _msb.Length ? _msb[cluster] : 0;
    var lsbVal = cluster < _lsb.Length ? _lsb[cluster] : 0;

    // Simple hybrid integer: token provides MSBs, extra bits provide LSBs
    var extra = _reader.ReadBits(nExtra);
    return (int)(((uint)token << nExtra) | extra);
  }

  private int _ReadPrefixSymbol(int cluster) {
    if (cluster >= _prefixLengths.Length)
      return 0;

    var lengths = _prefixLengths[cluster];
    var symbols = _prefixSymbols[cluster];

    if (lengths.Length == 0)
      return 0;
    if (lengths.Length == 1)
      return symbols[0];

    // Simple canonical Huffman decode: read bits and match
    var code = 0;
    for (var bitLen = 1; bitLen <= 15; ++bitLen) {
      code = (code << 1) | (int)_reader.ReadBits(1);
      for (var s = 0; s < lengths.Length; ++s)
        if (lengths[s] == bitLen && _GetCanonicalCode(lengths, s) == code)
          return symbols[s];
    }

    return 0;
  }

  private static int _GetCanonicalCode(int[] lengths, int symbolIndex) {
    var code = 0;
    var prevLen = 0;
    for (var i = 0; i <= symbolIndex; ++i) {
      if (lengths[i] == 0)
        continue;
      code <<= lengths[i] - prevLen;
      prevLen = lengths[i];
      if (i < symbolIndex) {
        ++code;
        // Find next non-zero length
        for (var j = i + 1; j <= symbolIndex; ++j)
          if (lengths[j] > 0) {
            code <<= lengths[j] - prevLen;
            prevLen = lengths[j];
            break;
          }
      }
    }
    return code;
  }

  private static void _ReadClusterMap(JxlBitReader reader, int[] clusterMap, int numContexts, int numClusters) {
    if (numClusters == 1) {
      Array.Clear(clusterMap);
      return;
    }

    // Simple encoding: each context gets ceil(log2(numClusters)) bits
    var bits = _Log2Ceil(numClusters);
    for (var i = 0; i < numContexts; ++i) {
      clusterMap[i] = (int)reader.ReadBits(bits);
      if (clusterMap[i] >= numClusters)
        clusterMap[i] = 0;
    }
  }

  private static (int[] Lengths, int[] Symbols) _ReadPrefixCode(JxlBitReader reader) {
    // Read a simple prefix code
    var alphabetSize = (int)reader.ReadU32(2, 0, 4, 0, 8, 0, 1, 7) + 1;

    if (alphabetSize == 1)
      return (new[] { 0 }, new[] { 0 });

    // Read code lengths
    var lengths = new int[alphabetSize];
    var symbols = new int[alphabetSize];
    var maxLen = 0;

    for (var i = 0; i < alphabetSize; ++i) {
      lengths[i] = (int)reader.ReadBits(4);
      symbols[i] = i;
      if (lengths[i] > maxLen)
        maxLen = lengths[i];
    }

    return (lengths, symbols);
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
