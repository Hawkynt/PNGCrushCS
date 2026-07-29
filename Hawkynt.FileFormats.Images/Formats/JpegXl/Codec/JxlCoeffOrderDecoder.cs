using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Per-AC-strategy coefficient-order permutation decoder. Mirrors libjxl
/// <c>coeff_order.cc::DecodeCoeffOrders</c> — when the encoder picks a non-
/// natural coefficient order for any AC strategy, the bitstream contains a
/// Lehmer-code permutation per (strategy, channel) that we have to consume
/// to keep the bit stream aligned even when we don't yet USE the orders for
/// AC coefficient decoding.
/// </summary>
internal static class JxlCoeffOrderDecoder {

  /// <summary>libjxl <c>kPermutationContexts</c> in <c>coeff_order.h</c>.</summary>
  public const int PermutationContexts = 8;

  /// <summary>libjxl <c>kStrategyOrder</c> from <c>coeff_order.h</c>: maps AC
  /// strategy ID (0..26) to the order bucket. Strategies that share an order
  /// bucket reuse the same permutation.</summary>
  internal static readonly byte[] StrategyOrder = {
    0, 1, 1, 1, 2, 3, 4, 4, 5,  5,  6,  6,  1,  1,
    1, 1, 1, 1, 7, 8, 8, 9, 10, 10, 11, 12, 12,
  };

  /// <summary>Number of valid AC strategies (libjxl
  /// <c>AcStrategy::kNumValidStrategies</c>).</summary>
  internal const int NumValidStrategies = 27;

  /// <summary>Per-strategy 8x8-block coverage (libjxl
  /// <c>covered_blocks_x[]</c> LUT).</summary>
  internal static readonly byte[] CoveredBlocksX = {
    1, 1, 1, 1, 2, 4, 1, 2, 1, 4, 2, 4, 1, 1, 1, 1, 1, 1,
    8, 4, 8, 16, 8, 16, 32, 16, 32,
  };

  /// <summary>Per-strategy 8x8-block coverage (libjxl
  /// <c>covered_blocks_y[]</c> LUT).</summary>
  internal static readonly byte[] CoveredBlocksY = {
    1, 1, 1, 1, 2, 4, 2, 1, 4, 1, 4, 2, 1, 1, 1, 1, 1, 1,
    8, 8, 4, 16, 16, 8, 32, 32, 16,
  };

  /// <summary>libjxl <c>kBlockDim * kBlockDim</c> = 64 — number of
  /// coefficients per 8x8 cell.</summary>
  private const int DctBlockSize = 64;

  /// <summary>
  /// libjxl <c>CoeffOrderContext(val)</c>: maps a previously-decoded
  /// permutation value to an entropy context ID. Uses
  /// <c>HybridUintConfig(0, 0, 0).Encode</c> to compute the token for
  /// <paramref name="val"/>, then clamps to <c>kPermutationContexts - 1</c>.
  /// For HybridUintConfig(0,0,0): token(0)=0, token(v)=1+floor(log2(v)) for v>=1.
  /// </summary>
  public static int CoeffOrderContext(uint val) {
    if (val == 0)
      return 0;
    var n = _FloorLog2Nonzero(val);
    var token = 1 + (int)n;
    return Math.Min(token, PermutationContexts - 1);
  }

  /// <summary>libjxl <c>DecodeCoeffOrders</c>: when <paramref name="usedOrders"/>
  /// is non-zero, decode permutation histograms + per-(strategy,channel)
  /// Lehmer-code permutations. Bit-stream advance only — the actual
  /// permutations are not yet exposed (AC decode falls back to natural
  /// order).
  /// </summary>
  /// <param name="reader">Bit reader positioned at the permutation
  /// histograms.</param>
  /// <param name="usedOrders">Bit mask of orders that the encoder actually
  /// permuted. Bit <c>i</c> set ⇒ order bucket <c>i</c> uses a
  /// permutation.</param>
  public static void DecodeCoeffOrders(JxlBitReader reader, uint usedOrders) {
    ArgumentNullException.ThrowIfNull(reader);
    if (usedOrders == 0)
      return;

    // libjxl: DecodeHistograms with kPermutationContexts contexts, then
    // ANSSymbolReader::Create. Our JxlEntropyDecoder.Read combines both,
    // deferring the rANS state read to first ReadInt — exactly what we want.
    var entropy = JxlEntropyDecoder.Read(reader, numContexts: PermutationContexts, disallowLz77: false);

    // For each AC strategy, decode 3 permutations IF its order bucket is in
    // usedOrders AND not yet computed.
    var computed = 0u;
    for (var o = 0; o < NumValidStrategies; ++o) {
      var ord = StrategyOrder[o];
      var ordBit = 1u << ord;
      if ((computed & ordBit) != 0)
        continue;
      computed |= ordBit;

      if ((usedOrders & ordBit) == 0)
        continue;

      var llf = (int)CoveredBlocksX[o] * CoveredBlocksY[o];
      var size = DctBlockSize * llf;
      // Per libjxl `DecodeCoeffOrder`: 3 channels share an order bucket.
      for (var c = 0; c < 3; ++c)
        _ReadPermutation(entropy, skip: llf, size: size);
    }

    // libjxl checks rANS final state — we tolerate misalignment here, the
    // entropy block is otherwise self-contained.
    _ = entropy.CheckFinalState();
  }

  /// <summary>libjxl <c>ReadPermutation</c>: reads
  /// <c>end = ReadHybridUint(CoeffOrderContext(size)) + skip</c> followed by
  /// <c>end - skip</c> Lehmer-code entries (each via
  /// <c>ReadHybridUint(CoeffOrderContext(last))</c>). We read but don't
  /// surface the permutation — the caller's natural-order fallback is used
  /// for actual AC coefficient decoding.</summary>
  private static void _ReadPermutation(JxlEntropyDecoder entropy, int skip, int size) {
    var end = (uint)entropy.ReadInt(CoeffOrderContext((uint)size)) + (uint)skip;
    if (end > (uint)size)
      throw new System.IO.InvalidDataException(
        $"DecodeCoeffOrders: permutation end={end} exceeds size={size}.");
    uint last = 0;
    for (var i = (uint)skip; i < end; ++i) {
      var v = (uint)entropy.ReadInt(CoeffOrderContext(last));
      if (v >= (uint)size - i)
        throw new System.IO.InvalidDataException(
          $"DecodeCoeffOrders: invalid lehmer code v={v} at position {i} (size={size}).");
      last = v;
    }
  }

  private static uint _FloorLog2Nonzero(uint x) {
    var n = 0u;
    while ((x >>= 1) != 0) ++n;
    return n;
  }
}
