using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Block context map for VarDCT AC entropy coding (ISO/IEC 18181-1 §G.8 /
/// libjxl <c>BlockCtxMap</c> in <c>lib/jxl/ac_context.h</c> +
/// <c>DecodeBlockCtxMap</c> in <c>lib/jxl/entropy_coder.cc</c>).
///
/// <para>The BCM clusters a 3-dimensional context space — (channel, AC
/// strategy "order index", quant-field threshold bucket, DC bucket) — down to
/// a small (≤16) set of distinct entropy contexts that drive the AC histogram
/// selection in <see cref="JxlEntropyDecoder"/>. Layout per libjxl:</para>
///
/// <code>
/// raw_index = ((((c &lt; 2 ? c ^ 1 : 2) * kNumOrders) + ord) * (qf_thresholds.size() + 1) + qf_idx)
///             * num_dc_ctxs + dc_idx
/// </code>
///
/// <para>so the table has <c>3 * kNumOrders * num_dc_ctxs * (num_qf_thresholds + 1)</c>
/// entries. The default map (when the bitstream sets <c>is_default = 1</c>)
/// is a 39-entry table from libjxl's <c>kDefaultCtxMap</c> with
/// <c>num_ctxs = 15, num_dc_ctxs = 1, qf_thresholds = []</c>.</para>
///
/// <para>The 13 "orders" follow libjxl <c>kStrategyOrder</c>: each AC-strategy
/// type is mapped to one of 13 size classes (DCT8 = 0, DCT8x16/16x8 = 4, etc.).
/// See <see cref="_StrategyOrder"/>.</para>
/// </summary>
internal sealed class JxlBlockContextMap {

  /// <summary>libjxl <c>kNumOrders</c> from <c>coeff_order_fwd.h</c>: number of
  /// AC-strategy "size classes" (DCT8, DCT8x16/16x8, DCT16x16, …).</summary>
  internal const int NumOrders = 13;

  /// <summary>libjxl <c>kDefaultCtxMap</c> from <c>ac_context.h</c>. Layout is
  /// <c>[channel_bucket][order]</c> where channel_bucket is
  /// <c>(c &lt; 2 ? c ^ 1 : 2)</c> — i.e. Y=0, X=1, B=2 — and order is one of
  /// the 13 size classes. 39 entries total.</summary>
  internal static readonly byte[] DefaultCtxMap = new byte[] {
    // Y channel (channel_bucket 0 = Y because Y is c=1 -> 1^1=0; libjxl swaps X/Y).
    0, 1, 2, 2, 3,  3,  4,  5,  6,  6,  6,  6,  6,
    // X channel (channel_bucket 1 = X)
    7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
    // B channel (channel_bucket 2 = B)
    7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14,
  };

  /// <summary>libjxl <c>kStrategyOrder</c> mapping AcStrategyType (0..26) to
  /// one of the 13 "orders" (size classes). Derived from
  /// <c>lib/jxl/coeff_order.cc</c>:
  /// <code>
  /// constexpr uint8_t kStrategyOrder[] = {
  ///   0, 0, 1, 1, 2, 3, 4, 4, 5, 5, 6, 6, 1, 1,
  ///   1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
  /// };
  /// </code>
  /// </summary>
  internal static readonly byte[] StrategyOrder = new byte[] {
    0, 0, 1, 1, 2, 3, 4, 4, 5, 5, 6, 6, 1, 1,
    1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
  };

  private readonly byte[] _ctxMap;
  private readonly int[][] _dcThresholds; // [channel][threshold_index]
  private readonly uint[] _qfThresholds;
  private readonly int _numDcCtxs;

  /// <summary>Number of distinct entropy contexts (max of ctx_map + 1).
  /// Mirrors libjxl <c>BlockCtxMap::num_ctxs</c>.</summary>
  public int NumContexts { get; }

  /// <summary>libjxl <c>kNonZeroBuckets</c> from <c>ac_context.h</c>.</summary>
  internal const int NonZeroBuckets = 37;

  /// <summary>libjxl <c>kZeroDensityContextCount</c> from <c>ac_context.h</c>.</summary>
  internal const int ZeroDensityContextCount = 458;

  /// <summary>Total number of AC entropy contexts per histogram set, mirroring
  /// libjxl <c>BlockCtxMap::NumACContexts() = num_ctxs * (kNonZeroBuckets +
  /// kZeroDensityContextCount)</c>. For the default block context map
  /// (num_ctxs = 15) this is 15 × 495 = 7425.</summary>
  public int NumACContexts => this.NumContexts * (NonZeroBuckets + ZeroDensityContextCount);

  private JxlBlockContextMap(
    byte[] ctxMap,
    int[][] dcThresholds,
    uint[] qfThresholds,
    int numDcCtxs,
    int numContexts
  ) {
    this._ctxMap = ctxMap;
    this._dcThresholds = dcThresholds;
    this._qfThresholds = qfThresholds;
    this._numDcCtxs = numDcCtxs;
    this.NumContexts = numContexts;
  }

  /// <summary>
  /// Construct the default block context map (the one sent when the bitstream's
  /// <c>is_default</c> flag is 1). Equivalent to libjxl's
  /// <c>BlockCtxMap()</c> default constructor: <c>kDefaultCtxMap</c> with
  /// <c>num_dc_ctxs = 1</c> and no QF thresholds, giving
  /// <c>num_ctxs = 15</c>.
  /// </summary>
  public static JxlBlockContextMap CreateDefault() {
    var ctxMap = (byte[])DefaultCtxMap.Clone();
    var maxCtx = 0;
    for (var i = 0; i < ctxMap.Length; ++i)
      if (ctxMap[i] > maxCtx)
        maxCtx = ctxMap[i];
    return new JxlBlockContextMap(
      ctxMap,
      dcThresholds: new[] { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() },
      qfThresholds: Array.Empty<uint>(),
      numDcCtxs: 1,
      numContexts: maxCtx + 1
    );
  }

  /// <summary>
  /// Decode the block context map from the bitstream
  /// (libjxl <c>DecodeBlockCtxMap</c>). Read order:
  /// <list type="number">
  ///   <item>1-bit <c>is_default</c>: if 1, return <see cref="CreateDefault"/>.</item>
  ///   <item>For each channel c ∈ {0,1,2}: 4-bit count, then count entries of
  ///     <c>UnpackSigned(U32(Bits(4), BitsOffset(8,16), BitsOffset(16,272),
  ///     BitsOffset(32,65808)))</c> — DC thresholds.
  ///     <c>num_dc_ctxs *= dc_thresholds[c].size() + 1</c>.</item>
  ///   <item>4-bit count, then count entries of
  ///     <c>U32(Bits(2), BitsOffset(3,4), BitsOffset(5,12), BitsOffset(8,44)) + 1</c>
  ///     — QF thresholds.</item>
  ///   <item>If <c>num_dc_ctxs * (qf_count + 1) &gt; 64</c>: invalid.</item>
  ///   <item>Decode a context map of <c>3 * kNumOrders * num_dc_ctxs * (qf_count + 1)</c>
  ///     entries via the standard cluster-map decoder
  ///     (<see cref="JxlEntropyDecoder._ReadClusterMap"/> wrapped in 8-bit values).</item>
  ///   <item>If <c>num_ctxs &gt; 16</c>: invalid.</item>
  /// </list>
  /// </summary>
  public static JxlBlockContextMap Decode(JxlBitReader reader, JxlEntropyDecoder? entropy = null) {
    ArgumentNullException.ThrowIfNull(reader);
    // `entropy` is part of the public API contract for symmetry with
    // JxlAcStrategyDecoder, but DecodeBlockCtxMap in libjxl creates its own
    // sub-decoder for the cluster-map step (DecodeContextMap recursively
    // calls DecodeHistograms with num_contexts=1). The parameter is now
    // optional — pre-reading a JxlEntropyDecoder before calling this method
    // would CONSUME extra bits and misalign the bitstream.
    _ = entropy;

    var isDefault = reader.ReadBool();
    if (isDefault)
      return CreateDefault();

    var dcThresholds = new int[3][];
    var numDcCtxs = 1;
    for (var c = 0; c < 3; ++c) {
      var count = (int)reader.ReadBits(4);
      dcThresholds[c] = new int[count];
      for (var i = 0; i < count; ++i) {
        var packed = reader.ReadU32(0, 4, 16, 8, 272, 16, 65808, 32);
        dcThresholds[c][i] = _UnpackSigned(packed);
      }
      numDcCtxs *= count + 1;
    }

    var qfCount = (int)reader.ReadBits(4);
    var qfThresholds = new uint[qfCount];
    for (var i = 0; i < qfCount; ++i)
      qfThresholds[i] = reader.ReadU32(0, 2, 4, 3, 12, 5, 44, 8) + 1u;

    if ((long)numDcCtxs * (qfCount + 1) > 64)
      throw new System.IO.InvalidDataException(
        "BlockCtxMap: num_dc_ctxs * (qf_count + 1) > 64.");

    var mapSize = 3 * NumOrders * numDcCtxs * (qfCount + 1);
    // The cluster map is decoded as a context map of `mapSize` entries
    // (libjxl: DecodeContextMap with num_htrees output). We reuse our existing
    // _ReadClusterMap helper, which mirrors libjxl's logic exactly. Since the
    // upper bound on num_htrees here is 16 (per the post-decode validation),
    // we pass numClusters = mapSize to allow any value, then re-derive
    // num_ctxs from the actual maximum.
    var clusterMapInt = new int[mapSize];
    JxlEntropyDecoder._ReadClusterMap(
      reader,
      clusterMapInt,
      numContexts: mapSize,
      numClusters: Math.Min(mapSize, 256)  // libjxl kMaxClusters = 256
    );

    // Convert to byte[] and compute num_ctxs.
    var ctxMap = new byte[mapSize];
    var maxCtx = 0;
    for (var i = 0; i < mapSize; ++i) {
      var v = clusterMapInt[i];
      if (v < 0 || v > 255)
        throw new System.IO.InvalidDataException(
          $"BlockCtxMap: cluster value {v} out of byte range.");
      ctxMap[i] = (byte)v;
      if (v > maxCtx)
        maxCtx = v;
    }

    var numContexts = maxCtx + 1;
    if (numContexts > 16)
      throw new System.IO.InvalidDataException(
        $"BlockCtxMap: num_ctxs {numContexts} > 16.");

    return new JxlBlockContextMap(ctxMap, dcThresholds, qfThresholds, numDcCtxs, numContexts);
  }

  /// <summary>
  /// Look up the entropy context for one (channel, ac_strategy, qf_index) combination.
  /// DC bucket is fixed to 0 (caller does not yet supply DC prediction in the
  /// integration boundary). The libjxl formula is:
  /// <code>
  /// idx = c &lt; 2 ? c ^ 1 : 2
  /// idx = idx * kNumOrders + ord
  /// idx = idx * (qf_thresholds.size() + 1) + qf_idx
  /// idx = idx * num_dc_ctxs + dc_idx
  /// return ctx_map[idx]
  /// </code>
  /// </summary>
  /// <param name="channel">XYB channel (0=X, 1=Y, 2=B).</param>
  /// <param name="strategy">AC strategy type, used to look up the size-class
  /// "order" in <see cref="StrategyOrder"/>.</param>
  /// <param name="qfIndex">Pre-computed quant-field bucket
  /// <c>0..qf_thresholds.size()</c>.</param>
  public int GetContext(int channel, JxlAcStrategyType strategy, int qfIndex) {
    if ((uint)channel >= 3u)
      throw new ArgumentOutOfRangeException(nameof(channel), "Must be 0..2.");
    var stratIdx = (int)strategy;
    if ((uint)stratIdx >= (uint)StrategyOrder.Length)
      throw new ArgumentOutOfRangeException(nameof(strategy));
    var qfBuckets = this._qfThresholds.Length + 1;
    if ((uint)qfIndex >= (uint)qfBuckets)
      throw new ArgumentOutOfRangeException(nameof(qfIndex), $"Must be 0..{qfBuckets - 1}.");

    var ord = StrategyOrder[stratIdx];
    var channelBucket = channel < 2 ? channel ^ 1 : 2;

    var idx = channelBucket * NumOrders + ord;
    idx = idx * qfBuckets + qfIndex;
    idx = idx * this._numDcCtxs + 0; // dc_idx = 0 (no DC prediction yet)
    return this._ctxMap[idx];
  }

  /// <summary>
  /// libjxl <c>UnpackSigned</c>: <c>(value &gt;&gt; 1) ^ (-(value &amp; 1))</c>.
  /// Maps 0,1,2,3,4,5,… → 0,-1,1,-2,2,-3,…
  /// </summary>
  private static int _UnpackSigned(uint value) {
    var u = (int)(value >> 1);
    var sign = -(int)(value & 1u);
    return u ^ sign;
  }
}
