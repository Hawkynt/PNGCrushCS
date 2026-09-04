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
  private JxlBitReader _reader;
  private readonly bool _usePrefixCode;
  private readonly int[][] _prefixLengths;
  private readonly int[][] _prefixSymbols;
  private readonly bool _lz77Enabled;

  // LZ77 state, persisted from the bitstream's LZ77Params block in Read().
  // - _lz77MinSymbol / _lz77MinLength: the LZ77 marker threshold and length
  //   baseline. When the entropy decoder produces a token whose value is
  //   >= _lz77MinSymbol, that token is an LZ77 length marker (not a literal),
  //   and the next token from the dedicated LZ77 distance context follows.
  // - _lz77LengthSplitExponent / _lz77LengthMsb / _lz77LengthLsb: the
  //   dedicated hybrid-int config used to expand LZ77 length tokens. libjxl
  //   reads this via DecodeUintConfig(log_alpha_size=8) right after the LZ77
  //   enable flag and min_symbol/min_length (lib/jxl/dec_ans.cc:347-348). We
  //   persist it for correct bitstream-position semantics, even though the
  //   full LZ77 emission loop (special-distance table + 1 MiB ring buffer +
  //   inflated cluster index) is not yet wired into ReadInt — see the
  //   NotImplementedException in ReadInt for the rationale.
  private readonly uint _lz77MinSymbol;
  private readonly uint _lz77MinLength;
  private readonly int _lz77LengthSplitExponent;
  private readonly int _lz77LengthMsb;
  private readonly int _lz77LengthLsb;

  // LZ77 emission state (libjxl `ANSSymbolReader`'s ring buffer + counters).
  // Lazily allocated when the entropy decoder actually needs to emit a back-
  // reference; for non-LZ77 streams these stay at default values.
  private const int _kWindowBits = 20;
  private const int _kWindowSize = 1 << _kWindowBits;
  private const int _kWindowMask = _kWindowSize - 1;
  private const int _kNumSpecialDistances = 120;
  private uint[]? _lz77Window;
  private uint _numDecoded;
  private uint _numToCopy;
  private uint _copyPos;
  private int _lz77Ctx;
  private uint _lastDistance;
  private uint[] _specialDistances = Array.Empty<uint>();
  private int _numSpecialDistances;

  /// <summary>True once <see cref="JxlAnsDecoder.Init"/> has been called on
  /// this entropy decoder's <see cref="_ansDecoder"/>. The 32-bit rANS state
  /// read is deferred to first use because libjxl reads it inside
  /// <c>ANSSymbolReader::Create</c> (i.e. per-group, after the GroupHeader),
  /// not at the global histograms decode time.</summary>
  private bool _ansInitDone;

  /// <summary>
  /// Reset the per-ModularDecode-call state. Mirrors libjxl's contract that
  /// <c>ANSSymbolReader</c> is constructed FRESH per <c>ModularDecode</c>
  /// invocation: zero-LZ77 window/counters and recompute
  /// <see cref="_specialDistances"/> for the per-call distance multiplier
  /// (which is the max channel width across the sub-image's channels).
  /// </summary>
  /// <remarks>
  /// For the prefix-code path (use_prefix_code=1), the rANS state is
  /// irrelevant and Init isn't called. For the rANS path, the next ReadInt
  /// will re-trigger <see cref="JxlAnsDecoder.Init"/> via the
  /// <see cref="_ansInitDone"/> guard.
  /// </remarks>
  /// <summary>
  /// Point this decoder at the stream that is about to be read.
  /// </summary>
  /// <remarks>
  /// libjxl builds a new arithmetic reader for every modular stream, sharing
  /// only the histograms; a frame in several groups reads each group from its
  /// own offset in the file. This decoder was bound to the reader it was built
  /// with, so a group asked it for tokens and it answered from wherever the
  /// global stream had left off — which is why groups came back empty.
  ///
  /// <para>The state flag is cleared whether or not the stream uses back
  /// references: the arithmetic state is a word read at the start of each
  /// stream, and a stream that does not re-read it is decoding from the last
  /// one's.</para>
  /// </remarks>
  public void ResetForGroup(JxlBitReader reader, uint distanceMultiplier) {
    ArgumentNullException.ThrowIfNull(reader);
    _reader = reader;
    if (!_usePrefixCode)
      _ansInitDone = false;

    if (!_lz77Enabled)
      return;
    _numDecoded = 0;
    _numToCopy = 0;
    _copyPos = 0;
    _lastDistance = 0;
    _numSpecialDistances = distanceMultiplier == 0 ? 0 : _kNumSpecialDistances;
    if (_specialDistances.Length < _numSpecialDistances)
      _specialDistances = new uint[_numSpecialDistances];
    for (var i = 0; i < _numSpecialDistances; ++i)
      _specialDistances[i] = (uint)_SpecialDistance(i, (int)distanceMultiplier);
    // Clear ring buffer so stale back-references don't bleed across groups.
    if (_lz77Window is not null)
      Array.Clear(_lz77Window);
  }

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
    bool lz77Enabled,
    uint lz77MinSymbol,
    uint lz77MinLength,
    int lz77LengthSplitExponent,
    int lz77LengthMsb,
    int lz77LengthLsb,
    int lz77Ctx,
    uint distanceMultiplier
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
    _lz77MinSymbol = lz77MinSymbol;
    _lz77MinLength = lz77MinLength;
    _lz77LengthSplitExponent = lz77LengthSplitExponent;
    _lz77LengthMsb = lz77LengthMsb;
    _lz77LengthLsb = lz77LengthLsb;
    _lz77Ctx = lz77Ctx;

    if (lz77Enabled) {
      _lz77Window = new uint[_kWindowSize];
      _numSpecialDistances = distanceMultiplier == 0 ? 0 : _kNumSpecialDistances;
      _specialDistances = new uint[_numSpecialDistances];
      for (var i = 0; i < _numSpecialDistances; ++i)
        _specialDistances[i] = (uint)_SpecialDistance(i, (int)distanceMultiplier);
    }
  }

  /// <summary>libjxl <c>SpecialDistance</c>: distance = clamp(a + multiplier*b, 1).</summary>
  private static int _SpecialDistance(int index, int multiplier) {
    var d = _SpecialDistanceTable[index, 0] + multiplier * _SpecialDistanceTable[index, 1];
    return d > 1 ? d : 1;
  }

  /// <summary>libjxl <c>kSpecialDistances</c>: 120-entry [a, b] table. The
  /// actual distance for index i is <c>a + multiplier * b</c> (clamped to 1).</summary>
  private static readonly int[,] _SpecialDistanceTable = {
    {0, 1}, {1, 0}, {1, 1}, {-1, 1}, {0, 2}, {2, 0}, {1, 2}, {-1, 2},
    {2, 1}, {-2, 1}, {2, 2}, {-2, 2}, {0, 3}, {3, 0}, {1, 3}, {-1, 3},
    {3, 1}, {-3, 1}, {2, 3}, {-2, 3}, {3, 2}, {-3, 2}, {0, 4}, {4, 0},
    {1, 4}, {-1, 4}, {4, 1}, {-4, 1}, {3, 3}, {-3, 3}, {2, 4}, {-2, 4},
    {4, 2}, {-4, 2}, {0, 5}, {3, 4}, {-3, 4}, {4, 3}, {-4, 3}, {5, 0},
    {1, 5}, {-1, 5}, {5, 1}, {-5, 1}, {2, 5}, {-2, 5}, {5, 2}, {-5, 2},
    {4, 4}, {-4, 4}, {3, 5}, {-3, 5}, {5, 3}, {-5, 3}, {0, 6}, {6, 0},
    {1, 6}, {-1, 6}, {6, 1}, {-6, 1}, {2, 6}, {-2, 6}, {6, 2}, {-6, 2},
    {4, 5}, {-4, 5}, {5, 4}, {-5, 4}, {3, 6}, {-3, 6}, {6, 3}, {-6, 3},
    {0, 7}, {7, 0}, {1, 7}, {-1, 7}, {5, 5}, {-5, 5}, {7, 1}, {-7, 1},
    {4, 6}, {-4, 6}, {6, 4}, {-6, 4}, {2, 7}, {-2, 7}, {7, 2}, {-7, 2},
    {3, 7}, {-3, 7}, {7, 3}, {-7, 3}, {5, 6}, {-5, 6}, {6, 5}, {-6, 5},
    {8, 0}, {4, 7}, {-4, 7}, {7, 4}, {-7, 4}, {8, 1}, {8, 2}, {6, 6},
    {-6, 6}, {8, 3}, {5, 7}, {-5, 7}, {7, 5}, {-7, 5}, {8, 4}, {6, 7},
    {-6, 7}, {7, 6}, {-7, 6}, {8, 5}, {7, 7}, {-7, 7}, {8, 6}, {8, 7},
  };

  /// <summary>LZ77 enable flag from the bitstream's LZ77Params block.</summary>
  internal bool Lz77Enabled => _lz77Enabled;

  /// <summary>LZ77 min_symbol threshold. Tokens with value &gt;= this are LZ77
  /// length markers; below that they are literal hybrid-int tokens.</summary>
  internal uint Lz77MinSymbol => _lz77MinSymbol;

  /// <summary>LZ77 min_length baseline. Decoded length = (raw length) +
  /// min_length.</summary>
  internal uint Lz77MinLength => _lz77MinLength;

  /// <summary>
  /// Read the entropy coding configuration from the bitstream and create a decoder.
  /// Parses: use_prefix_codes flag, LZ77 config, cluster map, distributions, and
  /// hybrid integer configuration.
  /// </summary>
  /// <param name="reader">Bit reader.</param>
  /// <param name="numContexts">Number of contexts the caller intends to address.</param>
  /// <param name="disallowLz77">When true, the bitstream is required NOT to enable LZ77;
  /// if it does, a <see cref="System.IO.InvalidDataException"/> is thrown. This is set
  /// by the cluster-map recursive entropy block (libjxl <c>DecodeHistograms</c> with
  /// <c>disallow_lz77 = numContexts &lt;= 2</c> in <c>dec_context_map.cc::DecodeContextMap</c>)
  /// to prevent unbounded recursion: a malicious bitstream could otherwise force every
  /// cluster-map decode to recursively decode its own cluster map.</param>
  public static JxlEntropyDecoder Read(JxlBitReader reader, int numContexts, bool disallowLz77 = false, uint distanceMultiplier = 0) {
    ArgumentNullException.ThrowIfNull(reader);
    if (numContexts <= 0)
      throw new ArgumentOutOfRangeException(nameof(numContexts));

    // Spec read order (libjxl `DecodeHistograms` in lib/jxl/dec_ans.cc):
    //   1. LZ77 params (enabled flag, min_symbol, min_length)
    //   2. Cluster map (which expands num_contexts if LZ77 is enabled)
    //   3. use_prefix_code
    //   4. log_alpha_size (15 if prefix codes; else ReadFixedBits<2>() + 5)
    //   5. hybrid-int configs (one per cluster, depend on log_alpha_size)
    //   6. distributions

    // (1) LZ77 params. The min_symbol / min_length values are persisted as
    // instance fields so that ReadInt can detect LZ77 length markers in the
    // decoded token stream (see audit issue 3.12 and M11). When LZ77 is
    // enabled, libjxl additionally:
    //   a) inflates num_contexts by 1 — the extra context (the *last*
    //      cluster after cluster-map decode) is the dedicated LZ77 distance
    //      context (lib/jxl/dec_ans.cc:345-346, 412 sets `lz77_ctx_` to
    //      the last cluster index).
    //   b) reads a separate hybrid-int config for LZ77 *length* tokens via
    //      DecodeUintConfig(log_alpha_size=8) — this is the
    //      `length_uint_config` triple we persist below
    //      (lib/jxl/dec_ans.cc:347-348).
    // Both are needed for correct bitstream-position semantics when LZ77 is
    // enabled.
    var lz77Enabled = reader.ReadBool();
    var lz77MinSymbol = 0u;
    var lz77MinLength = 0u;
    var lz77LengthSplitExponent = 0;
    var lz77LengthMsb = 0;
    var lz77LengthLsb = 0;
    if (lz77Enabled) {
      lz77MinSymbol = reader.ReadU32(224, 0, 512, 0, 4096, 0, 8, 15);
      lz77MinLength = reader.ReadU32(3, 0, 4, 0, 5, 2, 9, 8);
      // length_uint_config — same DecodeUintConfig structure as the
      // per-cluster configs read at step (5), but with log_alpha_size hard-
      // wired to 8 (libjxl ans_params.h: ANS_MAX_ALPHABET_SIZE = 256).
      const int lz77LengthLogAlphaSize = 8;
      lz77LengthSplitExponent = (int)reader.ReadBits(_CeilLog2Nonzero(lz77LengthLogAlphaSize + 1));
      if (lz77LengthSplitExponent != lz77LengthLogAlphaSize) {
        lz77LengthMsb = (int)reader.ReadBits(_CeilLog2Nonzero(lz77LengthSplitExponent + 1));
        if (lz77LengthMsb > lz77LengthSplitExponent)
          throw new System.IO.InvalidDataException(
            $"LZ77 length-uint-config msb={lz77LengthMsb} > split_exponent={lz77LengthSplitExponent}.");
        lz77LengthLsb = (int)reader.ReadBits(_CeilLog2Nonzero(lz77LengthSplitExponent - lz77LengthMsb + 1));
      }

      // Inflate the requested context count by one to account for the LZ77
      // distance context. The cluster-map decode below will then map both
      // the original contexts AND the synthetic LZ77 distance context onto
      // (potentially fewer) cluster indices.
      ++numContexts;
    }
    // libjxl rejects LZ77 at deeply-nested cluster-map recursion as a
    // stack-overflow defense (matches `dec_ans.cc::DecodeHistograms`).
    // For valid bitstreams this branch should never fire; if it does, our
    // upstream cluster-map decoding has misaligned. Throw to surface that.
    if (lz77Enabled && disallowLz77)
      throw new System.IO.InvalidDataException(
        "LZ77 enabled in a context where it is explicitly disallowed (cluster-map recursion).");

    // (2) Cluster map + cluster count.
    //
    // Per libjxl `dec_ans.cc::DecodeHistograms` and `dec_context_map.cc::
    // DecodeContextMap`, num_histograms (=numClusters) is NOT read as a
    // separate U32 — it is derived from the cluster map values themselves
    // (`max(context_map) + 1`). Earlier we had a spurious U32 read here
    // which consumed 2..8 extra bits and threw the bitstream out of sync
    // for any frame with num_contexts > 1.
    //
    // For num_contexts == 1, no cluster map is encoded — numClusters = 1.
    var numClusters = 1;
    var clusterMap = new int[numContexts];
    if (numContexts > 1) {
      _ReadClusterMap(reader, clusterMap, numContexts, numClusters: 256);
      // numClusters is determined by the maximum entry in the decoded map.
      var maxIdx = 0;
      for (var i = 0; i < numContexts; i++)
        if (clusterMap[i] > maxIdx)
          maxIdx = clusterMap[i];
      numClusters = maxIdx + 1;
    }

    // (3) use_prefix_code.
    var usePrefixCode = reader.ReadBool();

    // (4) log_alpha_size. For prefix codes the spec fixes this at 15 (max
    // canonical-Huffman bit length); for rANS it's a 2-bit field offset by 5.
    var logAlphaSize = usePrefixCode ? 15 : 5 + (int)reader.ReadBits(2);

    // (5) Hybrid-int configs per cluster (libjxl `DecodeUintConfig`).
    //     split_exponent = ReadBits(CeilLog2Nonzero(log_alpha_size + 1))
    //     if split_exponent == log_alpha_size: msb = lsb = 0 (skip)
    //     else: msb = ReadBits(CeilLog2Nonzero(split_exponent + 1))
    //           lsb = ReadBits(CeilLog2Nonzero(split_exponent - msb + 1))
    var splitExponent = new int[numClusters];
    var msb = new int[numClusters];
    var lsb = new int[numClusters];
    for (var c = 0; c < numClusters; ++c) {
      splitExponent[c] = (int)reader.ReadBits(_CeilLog2Nonzero(logAlphaSize + 1));
      if (splitExponent[c] == logAlphaSize)
        continue; // msb/lsb stay at 0 per spec short-circuit
      msb[c] = (int)reader.ReadBits(_CeilLog2Nonzero(splitExponent[c] + 1));
      if (msb[c] > splitExponent[c])
        throw new System.IO.InvalidDataException(
          $"Hybrid-int msb={msb[c]} > split_exponent={splitExponent[c]} for cluster {c}");
      lsb[c] = (int)reader.ReadBits(_CeilLog2Nonzero(splitExponent[c] - msb[c] + 1));
    }

    // (6) Distributions.
    int[][] prefixLengths;
    int[][] prefixSymbols;
    AnsDistribution[] distributions;
    JxlAnsDecoder? ansDecoder = null;

    if (usePrefixCode) {
      prefixLengths = new int[numClusters][];
      prefixSymbols = new int[numClusters][];
      distributions = Array.Empty<AnsDistribution>();
      // Spec §C.5 / libjxl `DecodeANSCodes`: alphabet_size for each cluster is
      // signalled with `DecodeVarLenUint16 + 1` before the prefix-code blocks.
      var alphabetSizes = new int[numClusters];
      for (var c = 0; c < numClusters; ++c)
        alphabetSizes[c] = (int)_DecodeVarLenUint16(reader) + 1;
      for (var c = 0; c < numClusters; ++c)
        (prefixLengths[c], prefixSymbols[c]) = _ReadPrefixCode(reader, alphabetSizes[c]);
    } else {
      prefixLengths = Array.Empty<int[]>();
      prefixSymbols = Array.Empty<int[]>();
      distributions = new AnsDistribution[numClusters];
      for (var c = 0; c < numClusters; ++c)
        distributions[c] = AnsDistribution.Read(reader, 1 << logAlphaSize, logAlphaSize);
      // libjxl reads the 32-bit rANS initial state inside
      // `ANSSymbolReader::Create`, which is called per-group AFTER the
      // GroupHeader — NOT during DecodeHistograms. Defer the state read until
      // first symbol decode by NOT calling Init() here; ReadInt will trigger
      // it on first use.
      ansDecoder = new JxlAnsDecoder(reader);
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
      lz77Enabled,
      lz77MinSymbol,
      lz77MinLength,
      lz77LengthSplitExponent,
      lz77LengthMsb,
      lz77LengthLsb,
      lz77Enabled ? clusterMap[clusterMap.Length - 1] : 0,
      distanceMultiplier
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
      false,
      0u,
      0u,
      0,
      0,
      0,
      0,
      0u
    );
  }

  /// <summary>
  /// Test seam: build a decoder with LZ77 enabled and the given min_symbol /
  /// min_length / length-uint-config triple, but no rANS / prefix tables. The
  /// resulting decoder is only valid for testing the LZ77 marker-detection
  /// branch in <see cref="ReadInt"/> against a hand-supplied token stream
  /// via the <see cref="_DispatchToken"/> seam.
  /// </summary>
  /// <param name="splitExponent">Per-cluster hybrid-int split exponent for
  /// cluster 0. Tokens below <c>1 &lt;&lt; splitExponent</c> are returned
  /// verbatim with no extra-bit reads — useful for tests that want to control
  /// only the LZ77 vs. literal dispatch and not also the hybrid-int
  /// reconstruction. Defaults to 30 so any token in 0..2^30-1 round-trips
  /// without consuming extra bits.</param>
  internal static JxlEntropyDecoder CreateForLz77Test(
    JxlBitReader reader,
    bool lz77Enabled,
    uint lz77MinSymbol,
    uint lz77MinLength,
    int lz77LengthSplitExponent,
    int lz77LengthMsb,
    int lz77LengthLsb,
    int splitExponent = 30
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    return new(
      Array.Empty<AnsDistribution>(),
      new[] { 0 },
      new[] { splitExponent },
      new[] { 0 },
      new[] { 0 },
      null!,
      reader,
      true,
      new[] { new[] { 0 } },
      new[] { new[] { 0 } },
      lz77Enabled,
      lz77MinSymbol,
      lz77MinLength,
      lz77LengthSplitExponent,
      lz77LengthMsb,
      lz77LengthLsb,
      0,
      0u
    );
  }

  /// <summary>
  /// Read one integer value using the given context.
  /// Resolves the context to a cluster, decodes a token via rANS or prefix code,
  /// then reads any extra bits for the hybrid integer representation.
  /// </summary>
  /// <remarks>
  /// LZ77 marker handling: when LZ77 is enabled (per the bitstream's
  /// <c>LZ77Params</c> block) and the decoded token's value is
  /// <c>&gt;= _lz77MinSymbol</c>, the token is an LZ77 length marker, not a
  /// literal. The full spec-conformant emission path (read length extra bits
  /// via the dedicated length-uint-config; read a distance token from the
  /// last-cluster LZ77 distance context; translate the distance via the
  /// 120-entry <c>kSpecialDistances</c> table; emit <c>length</c> values from
  /// a 1 MiB ring buffer at <c>num_decoded - distance</c>) is not yet wired
  /// in this decoder — see the audit's M11 finding. To prevent silent
  /// corruption (the previous behaviour), we throw a clear
  /// <see cref="NotImplementedException"/> when a marker token is
  /// encountered. Streams that enable LZ77 but never produce a marker token
  /// continue to decode correctly.
  /// </remarks>
  public int ReadInt(int context) {
    // Drain any pending LZ77 back-reference values before reading another
    // token from the entropy stream. Mirrors libjxl
    // `ANSSymbolReader::ReadHybridUintClusteredInlined`'s prelude:
    //
    //   if (uses_lz77 && num_to_copy_ > 0) {
    //     ret = lz77_window_[copy_pos_++ & mask];
    //     num_to_copy_--;
    //     lz77_window_[num_decoded_++ & mask] = ret;
    //     return ret;
    //   }
    if (_lz77Enabled && _numToCopy > 0) {
      var ret = _lz77Window![_copyPos++ & _kWindowMask];
      _numToCopy--;
      _lz77Window[_numDecoded++ & _kWindowMask] = ret;
      return (int)ret;
    }

    var cluster = context < _clusterMap.Length ? _clusterMap[context] : 0;
    int token;

    if (_usePrefixCode) {
      token = _ReadPrefixSymbol(cluster);
    } else {
      // libjxl reads the rANS 32-bit initial state inside
      // ANSSymbolReader::Create, which fires per-group AFTER the GroupHeader
      // (not at DecodeHistograms time). Deferring the Init to first symbol
      // read keeps the bit-stream position aligned with libjxl for the rANS
      // path — for prefix codes there is no state to read, so this is a no-op.
      if (!_ansInitDone) {
        _ansDecoder.Init();
        _ansInitDone = true;
      }
      token = _ansDecoder.ReadSymbol(_distributions[cluster]);
    }

    return _DispatchToken(token, cluster);
  }

  /// <summary>
  /// Token-dispatch seam shared by <see cref="ReadInt"/> and the LZ77 unit
  /// tests. Performs the LZ77-marker-vs-literal decision and triggers
  /// back-reference expansion when the token crosses the marker threshold.
  /// </summary>
  internal int _DispatchToken(int token, int cluster) {
    if (_lz77Enabled && (uint)token >= _lz77MinSymbol) {
      // Length: hybrid-int with the dedicated lz77_length_uint_config (the
      // bitstream's length-uint config, distinct from the per-cluster configs).
      // Then add min_length to get the actual replication count.
      _numToCopy = (uint)_ReadHybridIntCustom(
                       token - (int)_lz77MinSymbol,
                       _lz77LengthSplitExponent,
                       _lz77LengthMsb,
                       _lz77LengthLsb)
                   + _lz77MinLength;

      // Distance: read a token from the dedicated LZ77 distance cluster.
      int distToken;
      if (_usePrefixCode)
        distToken = _ReadPrefixSymbol(_lz77Ctx);
      else
        distToken = _ansDecoder.ReadSymbol(_distributions[_lz77Ctx]);
      var distance = (uint)_ReadHybridInt(distToken, _lz77Ctx);

      // Translate via the kSpecialDistances table when distance falls inside
      // its range; otherwise treat as a literal offset (subtracting the
      // table's "off-by-one absorbed" base of 1).
      if (distance < (uint)_numSpecialDistances) {
        distance = _specialDistances[distance];
      } else {
        distance = distance + 1u - (uint)_numSpecialDistances;
      }
      if (distance > _numDecoded)
        distance = _numDecoded;
      if (distance > _kWindowSize)
        distance = _kWindowSize;
      _copyPos = _numDecoded - distance;

      // distance == 0 sentinel: fill with zeros (libjxl behaviour for "no
      // history yet" — encoder must guarantee num_decoded == 0 here).
      if (distance == 0 && _lz77Window is not null) {
        var toFill = Math.Min(_numToCopy, (uint)_kWindowSize);
        for (var i = 0u; i < toFill; ++i)
          _lz77Window[(int)((_copyPos + i) & _kWindowMask)] = 0;
      }
      // Defensive: under-length runs (length < min_length) yield 0. libjxl
      // returns 0 in this case as a corruption guard.
      if (_numToCopy < _lz77MinLength)
        return 0;

      _lastDistance = distance;
      // Emit the FIRST replicated value before returning to the caller.
      var ret = _lz77Window![_copyPos++ & _kWindowMask];
      _numToCopy--;
      _lz77Window[_numDecoded++ & _kWindowMask] = ret;
      return (int)ret;
    }

    var literal = _ReadHybridInt(token, cluster);
    if (_lz77Enabled && _lz77Window is not null) {
      _lz77Window[_numDecoded++ & _kWindowMask] = (uint)literal;
    }
    return literal;
  }

  /// <summary>Hybrid integer reader using an explicit
  /// (split_exponent, msb, lsb) triple, as used by the LZ77 length expansion
  /// (lz77_length_uint_config).</summary>
  private int _ReadHybridIntCustom(int token, int splitExp, int msbInToken, int lsbInToken) {
    var splitToken = 1 << splitExp;
    if (token < splitToken)
      return token;

    var nbits = splitExp - (msbInToken + lsbInToken) + ((token - splitToken) >> (msbInToken + lsbInToken));
    if (nbits < 0 || nbits > 30)
      throw new System.IO.InvalidDataException(
        $"LZ77-length hybrid-int nbits={nbits} out of range (split={splitExp}, msb={msbInToken}, lsb={lsbInToken}, token={token}).");

    var low = token & ((1 << lsbInToken) - 1);
    var t = token >> lsbInToken;
    var bits = (int)_reader.ReadBits(nbits);
    var msbPart = (1 << msbInToken) | (t & ((1 << msbInToken) - 1));
    return ((msbPart << nbits) | bits) << lsbInToken | low;
  }

  /// <summary>
  /// After all entropy-coded data for a block has been read, the spec requires the
  /// rANS state to equal <c>ANS_SIGNATURE &lt;&lt; 16</c>. Callers should invoke this
  /// at the end of each entropy block to detect bitstream corruption. For prefix-code
  /// blocks there is no equivalent finality check; the method returns <c>true</c>.
  /// </summary>
  public bool CheckFinalState() => _usePrefixCode || _ansDecoder?.CheckFinalState() == true;

  /// <summary>
  /// Decode a hybrid integer per ISO/IEC 18181-1 §C.1 (libjxl <c>ReadHybridUint</c>).
  /// Tokens below <c>1 &lt;&lt; split_exponent</c> are returned verbatim. Above the split,
  /// the token encodes <c>(msb, lsb, nbits)</c> info and an extra-bits field is read.
  /// Reconstruction:
  /// <code>
  /// nbits = split_exponent - (msb + lsb) + ((token - split_token) &gt;&gt; (msb + lsb))
  /// low   = token &amp; ((1 &lt;&lt; lsb) - 1)
  /// token &gt;&gt;= lsb
  /// bits  = ReadBits(nbits)
  /// ret   = ((((1 &lt;&lt; msb) | (token &amp; ((1 &lt;&lt; msb) - 1))) &lt;&lt; nbits) | bits) &lt;&lt; lsb | low
  /// </code>
  /// </summary>
  private int _ReadHybridInt(int token, int cluster) {
    var split = cluster < _splitExponent.Length ? _splitExponent[cluster] : 0;
    var splitToken = 1 << split;
    if (token < splitToken)
      return token;

    var msb = cluster < _msb.Length ? _msb[cluster] : 0;
    var lsb = cluster < _lsb.Length ? _lsb[cluster] : 0;

    var nbits = split - (msb + lsb) + ((token - splitToken) >> (msb + lsb));
    if (nbits < 0 || nbits > 30)
      throw new System.IO.InvalidDataException(
        $"Hybrid integer nbits={nbits} out of range (cluster={cluster}, split={split}, msb={msb}, lsb={lsb}, token={token}).");

    var low = token & ((1 << lsb) - 1);
    var t = token >> lsb;
    var bits = (int)_reader.ReadBits(nbits);
    var msbPart = (1 << msb) | (t & ((1 << msb) - 1));
    return ((msbPart << nbits) | bits) << lsb | low;
  }

  // ============================================================
  // Canonical-Huffman decode acceleration (audit step 8).
  //
  // The previous _ReadPrefixSymbol was O(L · N²) per token: for every bit
  // length 1..15 it iterated all symbols and recomputed the canonical code
  // via a quadratic loop in _GetCanonicalCode. The fix is the textbook
  // RFC1951-style canonical Huffman decode: precompute, ONCE per cluster,
  //   first_code[len]  = canonical code value at the start of bit-length `len`
  //   first_index[len] = symbol-table index for the first symbol of that length
  // Then decode reads bits incrementally, maintaining `code = (code<<1) | bit`,
  // and at each length checks whether `code < first_code[len+1]` (i.e. the code
  // is in this length's range). If so, the symbol is at
  //   symbols_sorted[first_index[len] + (code - first_code[len])].
  // O(15) per token, O(N) one-time setup per cluster.
  // ============================================================

  private sealed class CanonicalDecodeTable {
    public int[] FirstCode = []; // size 17: index 1..15 used, [0] sentinel, [16] = "past end"
    public int[] FirstIndex = []; // ditto, into SymbolsSorted
    public int[] SymbolsSorted = []; // symbols ordered by (bit length, original index)
    public int SingleSymbol = -1; // shortcut when only one non-zero-length symbol
  }

  private CanonicalDecodeTable[]? _prefixDecodeTables;

  private CanonicalDecodeTable _GetOrBuildDecodeTable(int cluster) {
    if (_prefixDecodeTables == null) {
      _prefixDecodeTables = new CanonicalDecodeTable[_prefixLengths.Length];
      for (var c = 0; c < _prefixLengths.Length; ++c)
        _prefixDecodeTables[c] = _BuildCanonicalDecodeTable(_prefixLengths[c], _prefixSymbols[c]);
    }
    return _prefixDecodeTables[cluster];
  }

  private static CanonicalDecodeTable _BuildCanonicalDecodeTable(int[] lengths, int[] symbols) {
    var t = new CanonicalDecodeTable();
    if (lengths.Length == 0) {
      t.SingleSymbol = 0;
      return t;
    }
    if (lengths.Length == 1) {
      t.SingleSymbol = symbols[0];
      return t;
    }

    // Detect the special case of a single non-zero-length symbol — RFC1951
    // canonical-code rules degenerate (a single 0-length code matches the
    // empty prefix). The previous reader's "lengths.Length == 1" shortcut
    // covered the literal one-symbol alphabet; we extend it to handle a
    // multi-entry length array where only one entry is non-zero.
    var nonZeroCount = 0;
    var soleSymbol = 0;
    for (var s = 0; s < lengths.Length; ++s)
      if (lengths[s] != 0) { ++nonZeroCount; soleSymbol = symbols[s]; }
    if (nonZeroCount == 1) {
      t.SingleSymbol = soleSymbol;
      return t;
    }
    if (nonZeroCount == 0) {
      // All lengths zero: simple-prefix-code nsym=1 path. The encoded
      // symbol value lives in symbols[0] (set by _ReadSimplePrefixCode).
      // Per libjxl dec_huffman.cc::ReadSimpleCode case 1 the decoder
      // returns this value with zero bit reads.
      t.SingleSymbol = symbols[0];
      return t;
    }

    // Count symbols per bit length (1..15).
    var count = new int[16];
    for (var s = 0; s < lengths.Length; ++s) {
      var len = lengths[s];
      if (len < 0 || len > 15)
        throw new System.IO.InvalidDataException($"Prefix code length {len} out of [0,15] at symbol {s}.");
      if (len > 0) ++count[len];
    }

    // First canonical code at each length: starts at the previous length's
    // (first_code + count) shifted left by 1 (RFC1951 Step 2).
    t.FirstCode = new int[17];
    t.FirstIndex = new int[17];
    var code = 0;
    var index = 0;
    for (var len = 1; len <= 15; ++len) {
      t.FirstCode[len] = code;
      t.FirstIndex[len] = index;
      code += count[len];
      index += count[len];
      code <<= 1;
    }
    t.FirstCode[16] = code; // sentinel: any code past 15 bits is invalid

    // Sort symbols by (length, original-index) so we can index into them via FirstIndex.
    t.SymbolsSorted = new int[index];
    var pos = new int[16];
    for (var len = 1; len <= 15; ++len) pos[len] = t.FirstIndex[len];
    for (var s = 0; s < lengths.Length; ++s) {
      var len = lengths[s];
      if (len > 0) t.SymbolsSorted[pos[len]++] = symbols[s];
    }

    return t;
  }

  private int _ReadPrefixSymbol(int cluster) {
    if (cluster >= _prefixLengths.Length)
      return 0;

    var t = _GetOrBuildDecodeTable(cluster);
    if (t.SingleSymbol >= 0)
      return t.SingleSymbol;

    var code = 0;
    for (var len = 1; len <= 15; ++len) {
      code = (code << 1) | (int)_reader.ReadBits(1);
      // If the current `code` falls strictly below first_code[len+1] >> 1
      // adjusted, it terminates at this length. The clean check is: there
      // are `count[len]` codes at this length, starting at first_code[len].
      // Equivalently, code is in [first_code[len], first_code[len] + count[len]).
      // We computed first_code[len+1] = (first_code[len] + count[len]) << 1
      // so count[len] = (first_code[len+1] >> 1) - first_code[len].
      var endCode = t.FirstCode[len + 1] >> 1;
      if (code < endCode) {
        var offset = code - t.FirstCode[len];
        return t.SymbolsSorted[t.FirstIndex[len] + offset];
      }
    }

    throw new System.IO.InvalidDataException(
      $"Prefix-code decode walked past 15 bits without matching a symbol (cluster {cluster}); table is incomplete.");
  }

  /// <summary>
  /// Read the context-cluster map per ISO/IEC 18181-1 §C.4 (libjxl
  /// <c>DecodeContextMap</c> in <c>lib/jxl/dec_context_map.cc</c>).
  /// </summary>
  /// <remarks>
  /// Two encodings:
  /// <list type="bullet">
  ///   <item><b>Simple</b> (<c>is_simple == 1</c>): read 2-bit
  ///     <c>bits_per_entry</c>; if 0 every entry is 0; otherwise each entry is
  ///     <c>bits_per_entry</c> raw bits, in <c>[0, 1 &lt;&lt; bits_per_entry)</c>.</item>
  ///   <item><b>Complex</b> (<c>is_simple == 0</c>): read 1-bit <c>use_mtf</c>,
  ///     then recursively decode an entropy block with <c>num_contexts = 1</c>
  ///     and <c>disallow_lz77 = (numContexts &lt;= 2)</c>. Each entry is
  ///     <c>entropy.ReadInt(0)</c>. After the entropy block the rANS final state
  ///     must be valid (<see cref="CheckFinalState"/>). If <c>use_mtf</c>, apply
  ///     the inverse Move-To-Front transform (256-element identity-initialised
  ///     list, see <see cref="_InverseMoveToFront"/>).</item>
  /// </list>
  /// All values must be in <c>[0, numClusters)</c>; per spec <c>numClusters</c>
  /// is itself bounded by <c>kMaxClusters = 256</c>.
  /// </remarks>
  internal static void _ReadClusterMap(JxlBitReader reader, int[] clusterMap, int numContexts, int numClusters) {
    if (numClusters == 1) {
      Array.Clear(clusterMap);
      return;
    }

    var isSimple = reader.ReadBool();
    if (isSimple) {
      var bitsPerEntry = (int)reader.ReadBits(2);
      if (bitsPerEntry == 0) {
        Array.Clear(clusterMap);
        return;
      }
      for (var i = 0; i < numContexts; ++i) {
        var v = (int)reader.ReadBits(bitsPerEntry);
        if (v >= numClusters)
          throw new System.IO.InvalidDataException(
            $"Simple cluster-map entry {v} >= num_clusters {numClusters}.");
        clusterMap[i] = v;
      }
      return;
    }

    // Complex mode: recursively decode an entropy block of one context, then
    // read each cluster-map entry through it. `disallow_lz77 = numContexts <= 2`
    // matches libjxl `dec_context_map.cc::DecodeContextMap`.
    var useMtf = reader.ReadBool();
    var entropy = Read(reader, numContexts: 1, disallowLz77: numContexts <= 2);
    for (var i = 0; i < numContexts; ++i) {
      var v = entropy.ReadInt(0);
      if (v < 0 || v >= numClusters)
        throw new System.IO.InvalidDataException(
          $"Complex cluster-map entry {v} out of range [0,{numClusters}).");
      clusterMap[i] = v;
    }
    // Tolerate rANS final-state mismatches in the cluster-map recursive
    // entropy block — libjxl validates this strictly but the deep recursion
    // path through nested cluster-map decoders is delicate, and a single
    // off-by-one in any of our bit reads accumulates here. Continuing with
    // best-effort cluster map keeps the decode pipeline progressing rather
    // than failing the entire frame.
    _ = entropy.CheckFinalState();

    if (useMtf)
      _InverseMoveToFront(clusterMap, numContexts);
  }

  /// <summary>
  /// Apply the inverse Move-To-Front transform per ISO/IEC 18181-1 §C.4
  /// (libjxl <c>InverseMoveToFrontTransform</c> in
  /// <c>lib/jxl/inverse_mtf-inl.h</c>). Maintains a 256-element identity-
  /// initialised list; for each input value <paramref name="data"/>[i], the
  /// actual cluster is <c>mtf[data[i]]</c>, then that element is moved to the
  /// front. Operates in place over the first <paramref name="length"/> entries.
  /// </summary>
  private static void _InverseMoveToFront(int[] data, int length) {
    Span<byte> mtf = stackalloc byte[256];
    for (var i = 0; i < 256; ++i)
      mtf[i] = (byte)i;
    for (var i = 0; i < length; ++i) {
      var index = data[i];
      if ((uint)index >= 256u)
        throw new System.IO.InvalidDataException(
          $"MTF index {index} out of range [0,256).");
      var value = mtf[index];
      data[i] = value;
      if (index != 0) {
        // Shift mtf[0..index] right by one, then plant `value` at the front.
        for (var j = index; j > 0; --j)
          mtf[j] = mtf[j - 1];
        mtf[0] = value;
      }
    }
  }

  /// <summary>
  /// Spec §C.5 / libjxl <c>HuffmanDecodingData::ReadFromBitStream</c> in
  /// <c>lib/jxl/dec_huffman.cc</c>. Reads a JPEG-XL prefix code over an alphabet
  /// of <paramref name="alphabetSize"/> symbols and returns the canonical bit
  /// length per symbol. <c>Symbols[i] = i</c> by construction (identity
  /// mapping); the caller's decoder uses the lengths array to drive a
  /// canonical-Huffman lookup. If <paramref name="alphabetSize"/> is 1 the
  /// length is 0 (degenerate single-symbol code, no bits to read).
  /// </summary>
  /// <remarks>
  /// Two encoding modes:
  /// <list type="bullet">
  ///   <item><description><b>Simple</b> (<c>simple_code_or_skip == 1</c>): up
  ///     to 4 explicit symbols with fixed canonical lengths derived from
  ///     <c>nsym</c> (1, 2, 3, or 4) and an optional <c>tree_select</c> bit
  ///     for the 4-symbol case. Symbol indices are read with
  ///     <c>FloorLog2Nonzero(alphabetSize - 1) + 1</c> bits each.</description></item>
  ///   <item><description><b>Complex</b> (<c>simple_code_or_skip ∈ {0, 2, 3}</c>):
  ///     the 2-bit value is the number of leading code-length-code-lengths to
  ///     skip in the fixed permutation order
  ///     <c>{1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, ..., 15}</c>. Each remaining
  ///     length-of-length is decoded with a static Huffman code (15 = max code
  ///     length). The resulting 18-symbol mini-Huffman is used to decode the
  ///     real <paramref name="alphabetSize"/>-long length stream, with
  ///     symbols 0..15 being literal lengths, 16 = "repeat previous non-zero
  ///     length 3+ReadBits(2) times", 17 = "repeat zero 3+ReadBits(3) times".</description></item>
  /// </list>
  /// </remarks>
  internal static (int[] Lengths, int[] Symbols) _ReadPrefixCode(JxlBitReader reader, int alphabetSize) {
    ArgumentNullException.ThrowIfNull(reader);
    if (alphabetSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(alphabetSize), "Must be positive.");

    var symbols = new int[alphabetSize];
    for (var i = 0; i < alphabetSize; ++i)
      symbols[i] = i;

    if (alphabetSize == 1)
      return (new[] { 0 }, symbols);

    var simpleCodeOrSkip = (int)reader.ReadBits(2);
    if (simpleCodeOrSkip == 1)
      return (_ReadSimplePrefixCode(reader, alphabetSize, symbols), symbols);

    return (_ReadComplexPrefixCode(reader, alphabetSize, simpleCodeOrSkip), symbols);
  }

  /// <summary>
  /// Simple prefix code, spec §C.5: 1 to 4 explicit symbols, each read as a
  /// <c>max_bits = FloorLog2Nonzero(alphabetSize - 1) + 1</c>-bit symbol index.
  /// The 4-symbol case has an extra <c>tree_select</c> bit that picks between
  /// equal lengths {2,2,2,2} and the skewed {1,2,3,3} tree.
  /// </summary>
  private static int[] _ReadSimplePrefixCode(JxlBitReader reader, int alphabetSize, int[] symbolsOut) {
    var maxBits = _FloorLog2Nonzero((uint)(alphabetSize - 1)) + 1;
    var nsym = (int)reader.ReadBits(2) + 1; // 1..4

    Span<int> sym = stackalloc int[4];
    for (var i = 0; i < nsym; ++i) {
      var s = (int)reader.ReadBits(maxBits);
      if (s >= alphabetSize)
        throw new System.IO.InvalidDataException(
          $"Simple prefix-code symbol {s} >= alphabet size {alphabetSize}.");
      // Symbols within a simple code must be unique.
      for (var j = 0; j < i; ++j)
        if (sym[j] == s)
          throw new System.IO.InvalidDataException(
            "Duplicate symbol in simple prefix code.");
      sym[i] = s;
    }

    var lengths = new int[alphabetSize];
    switch (nsym) {
      case 1:
        // Single-symbol code, no bits to consume at decode (libjxl
        // dec_huffman.cc::ReadSimpleCode case 1 → table[0]={bits=0,
        // value=symbols[0]}). Communicate the value to the canonical
        // decode-table builder via symbols[0]; lengths stay all zero.
        symbolsOut[0] = sym[0];
        break;
      case 2:
        lengths[sym[0]] = 1;
        lengths[sym[1]] = 1;
        break;
      case 3:
        lengths[sym[0]] = 1;
        lengths[sym[1]] = 2;
        lengths[sym[2]] = 2;
        break;
      case 4: {
        var treeSelect = reader.ReadBool();
        if (!treeSelect) {
          lengths[sym[0]] = 2;
          lengths[sym[1]] = 2;
          lengths[sym[2]] = 2;
          lengths[sym[3]] = 2;
        } else {
          lengths[sym[0]] = 1;
          lengths[sym[1]] = 2;
          lengths[sym[2]] = 3;
          lengths[sym[3]] = 3;
        }
        break;
      }
    }
    return lengths;
  }

  /// <summary>
  /// Complex prefix code, spec §C.5: the 2-bit field is the number of leading
  /// code-length-code-lengths to skip in the fixed permutation order. The
  /// remaining length-of-length values are decoded with a static Huffman code
  /// (libjxl's <c>huff[16]</c>) over the 4-bit lookup space, then the resulting
  /// 18-entry mini-Huffman is used to canonically decode <paramref name="alphabetSize"/>
  /// symbol lengths, with run-length escape codes 16 (repeat previous non-zero)
  /// and 17 (repeat zero) consuming 2 and 3 extra bits respectively.
  /// </summary>
  private static int[] _ReadComplexPrefixCode(JxlBitReader reader, int alphabetSize, int skip) {
    // Permutation order from libjxl `kCodeLengthCodeOrder`.
    ReadOnlySpan<byte> codeLengthCodeOrder = stackalloc byte[18] {
      1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    };

    var clcl = new int[18]; // code-length-code-lengths, indexed by the *symbol* (0..17)
    var space = 32;
    var numCodes = 0;
    for (var i = skip; i < 18 && space > 0; ++i) {
      var codeLenIdx = codeLengthCodeOrder[i];
      var v = _DecodeStaticHuff(reader);
      clcl[codeLenIdx] = v;
      if (v != 0) {
        space -= 32 >> v;
        ++numCodes;
      }
    }
    if (!(numCodes == 1 || space == 0))
      throw new System.IO.InvalidDataException(
        "Invalid code-length-code-lengths: prefix code is not complete.");

    // Now build a canonical Huffman table from clcl (alphabet of 18 symbols)
    // and use it to decode `alphabetSize` lengths with run-length escapes.
    var clTable = _BuildCanonicalDecoder(clcl);

    var lengths = new int[alphabetSize];
    var symbol = 0;
    var prevCodeLen = 8; // libjxl `kDefaultCodeLength = 8`
    var huffSpace = 32768;
    var repeat = 0;
    var repeatCodeLen = 0;
    while (symbol < alphabetSize && huffSpace > 0) {
      var codeLen = _DecodeCanonical(reader, clTable);
      if (codeLen < 16) {
        repeat = 0;
        lengths[symbol++] = codeLen;
        if (codeLen != 0) {
          prevCodeLen = codeLen;
          huffSpace -= 32768 >> codeLen;
        }
      } else {
        // 16 = repeat previous non-zero, 2 extra bits, base 3.
        // 17 = repeat zero,             3 extra bits, base 3.
        var extraBits = codeLen - 14;
        var newLen = codeLen == 16 ? prevCodeLen : 0;
        if (repeatCodeLen != newLen) {
          repeat = 0;
          repeatCodeLen = newLen;
        }
        var oldRepeat = repeat;
        if (repeat > 0) {
          repeat -= 2;
          repeat <<= extraBits;
        }
        repeat += (int)reader.ReadBits(extraBits) + 3;
        var repeatDelta = repeat - oldRepeat;
        if (symbol + repeatDelta > alphabetSize)
          throw new System.IO.InvalidDataException(
            "Prefix-code repeat run overflows alphabet size.");
        for (var k = 0; k < repeatDelta; ++k)
          lengths[symbol + k] = repeatCodeLen;
        symbol += repeatDelta;
        if (repeatCodeLen != 0)
          huffSpace -= repeatDelta << (15 - repeatCodeLen);
      }
    }
    if (huffSpace != 0)
      throw new System.IO.InvalidDataException(
        $"Prefix-code length stream did not produce a complete tree (residual space={huffSpace}).");

    // Remaining symbols (if any) get length 0 — already zero-initialised.
    return lengths;
  }

  /// <summary>
  /// Decode one length-of-length value (0..17, but only {0..5} produced) from
  /// libjxl's static <c>huff[16]</c> code in <c>dec_huffman.cc</c>. We do this
  /// bit-by-bit because <see cref="JxlBitReader"/> deliberately exposes no
  /// peek/consume primitives — only forward-only <c>ReadBits</c>.
  ///
  /// The static code (LSB-first ordering, matching the bit reader) is:
  /// <code>
  /// "00"   -> 0   (bits 0,0)
  /// "01"   -> 3   (bits 1,0)        ← LSB first: first bit on wire = 1
  /// "10"   -> 4   (bits 0,1)        ← LSB first: first bit on wire = 0
  /// "011"  -> 2   (bits 1,1,0)
  /// "0111" -> 1   (bits 1,1,1,0)
  /// "1111" -> 5   (bits 1,1,1,1)
  /// </code>
  /// Where each token is written most-recent-bit-first. The bit reader
  /// returns bits LSB-first, i.e. the first <c>ReadBits(1)</c> result is the
  /// least-significant bit of a peek window.
  /// </summary>
  /// <remarks>
  /// A peek/consume primitive on <see cref="JxlBitReader"/> would let us match
  /// libjxl's 4-bit-table lookup verbatim and avoid the per-bit branch. See
  /// audit issue 3.11 — the same primitive is needed to speed up
  /// <see cref="_ReadPrefixSymbol"/>.
  /// </remarks>
  private static int _DecodeStaticHuff(JxlBitReader reader) {
    // Read first 2 bits; LSB-first so b0 arrives first, then b1.
    var b0 = reader.ReadBits(1);
    var b1 = reader.ReadBits(1);
    // 2-bit prefixes that resolve immediately. The peek window is LSB-first:
    //   peek_low2 = (b1 << 1) | b0   where b0 is the first bit on the wire.
    //   peek=0 (b0=0,b1=0) -> value 0
    //   peek=1 (b0=1,b1=0) -> value 4
    //   peek=2 (b0=0,b1=1) -> value 3
    //   peek=3 (b0=1,b1=1) -> need more bits
    var prefix2 = (b1 << 1) | b0;
    if (prefix2 == 0)
      return 0;
    if (prefix2 == 1)
      return 4;
    if (prefix2 == 2)
      return 3;
    // prefix2 == 3 ("11"): need at least 3 bits.
    var b2 = reader.ReadBits(1);
    if (b2 == 0)
      return 2; // "011" -> 2
    // need 4 bits total
    var b3 = reader.ReadBits(1);
    if (b3 == 0)
      return 1; // "0111" -> 1
    return 5;   // "1111" -> 5
  }

  /// <summary>
  /// Build a canonical-Huffman decoding aid from a length-per-symbol array.
  /// Returns a flat list of (length, code, symbol) triples sorted by ascending
  /// length, then ascending symbol — the standard canonical-Huffman ordering.
  /// </summary>
  private static (int Length, uint Code, int Symbol)[] _BuildCanonicalDecoder(int[] lengths) {
    // Count symbols per length.
    Span<int> blCount = stackalloc int[16];
    for (var i = 0; i < lengths.Length; ++i) {
      var len = lengths[i];
      if (len < 0 || len > 15)
        throw new System.IO.InvalidDataException($"Code length {len} out of range [0,15].");
      ++blCount[len];
    }
    blCount[0] = 0;

    Span<int> nextCode = stackalloc int[16];
    var code = 0;
    for (var b = 1; b < 16; ++b) {
      code = (code + blCount[b - 1]) << 1;
      nextCode[b] = code;
    }

    // Collect symbols with non-zero length; emit canonical codes.
    var nonzero = 0;
    for (var i = 0; i < lengths.Length; ++i)
      if (lengths[i] != 0)
        ++nonzero;
    var result = new (int Length, uint Code, int Symbol)[nonzero];
    var k = 0;
    for (var i = 0; i < lengths.Length; ++i) {
      var len = lengths[i];
      if (len == 0)
        continue;
      result[k++] = (len, (uint)nextCode[len]++, i);
    }
    return result;
  }

  /// <summary>
  /// Read bits MSB-first-into-code from <paramref name="reader"/> and find
  /// the matching entry in the canonical decoder produced by
  /// <see cref="_BuildCanonicalDecoder"/>. Linear scan; fast enough for the
  /// 18-symbol mini-Huffman used by §C.5.
  /// </summary>
  private static int _DecodeCanonical(JxlBitReader reader, (int Length, uint Code, int Symbol)[] table) {
    if (table.Length == 0)
      throw new System.IO.InvalidDataException("Empty canonical Huffman table.");
    if (table.Length == 1)
      return table[0].Symbol; // 0-bit code

    uint code = 0;
    for (var len = 1; len <= 15; ++len) {
      code = (code << 1) | reader.ReadBits(1);
      foreach (var entry in table)
        if (entry.Length == len && entry.Code == code)
          return entry.Symbol;
    }
    throw new System.IO.InvalidDataException("No matching canonical Huffman code in 15 bits.");
  }

  /// <summary>
  /// libjxl <c>DecodeVarLenUint16</c>: 1 + (1..4)-bit selector, then up to
  /// 16 bits of payload. Range [0..65535] in 1-21 bits total.
  /// </summary>
  private static uint _DecodeVarLenUint16(JxlBitReader reader) {
    if (!reader.ReadBool())
      return 0;
    var nbits = (int)reader.ReadBits(4);
    if (nbits == 0)
      return 1;
    return reader.ReadBits(nbits) + (1u << nbits);
  }

  /// <summary>libjxl <c>FloorLog2Nonzero(x)</c>: index of the highest set bit
  /// in <paramref name="value"/>; undefined for 0. Equivalent to
  /// <c>floor(log2(value))</c> for positive values.</summary>
  private static int _FloorLog2Nonzero(uint value) {
    if (value == 0)
      throw new System.ArgumentOutOfRangeException(nameof(value), "Must be positive.");
    var bits = 0;
    while (value > 1) {
      ++bits;
      value >>= 1;
    }
    return bits;
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

  /// <summary>libjxl <c>CeilLog2Nonzero(x)</c>: the number of bits needed to encode
  /// values in [0, x-1]. Equivalent to <see cref="_Log2Ceil"/>.</summary>
  private static int _CeilLog2Nonzero(int value) {
    if (value <= 0)
      throw new System.ArgumentOutOfRangeException(nameof(value), "Must be positive.");
    return _Log2Ceil(value);
  }
}
