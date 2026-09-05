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

  /// <summary>Number of order buckets the strategies map onto.</summary>
  internal const int NumOrders = 13;

  /// <summary>
  /// The scan order each transform states for itself, per order bucket and
  /// channel (libjxl <c>coeff_order.cc::DecodeCoeffOrders</c>).
  /// </summary>
  /// <remarks>
  /// A frame may state, for any bucket, an order of its own instead of the one
  /// the shape implies. It states it as a permutation of the shape's natural
  /// order rather than as positions, so the two are composed: the k-th
  /// coefficient goes where the natural order's <c>permutation[k]</c>-th one
  /// would have gone.
  ///
  /// <para>Buckets are shared. Several shapes map onto one bucket and all of
  /// them use the natural order of whichever shape comes first, which is not
  /// the same as each computing its own — a shape and its transpose share a
  /// bucket, and the first of the pair decides for both.</para>
  /// </remarks>
  /// <param name="reader">Bit reader positioned at the permutation histograms.</param>
  /// <param name="usedOrders">Bit <c>i</c> set means bucket <c>i</c> states an
  /// order of its own.</param>
  /// <returns>Indexed by bucket, then by channel.</returns>
  public static int[][][] DecodeCoeffOrders(JxlBitReader reader, uint usedOrders) {
    ArgumentNullException.ThrowIfNull(reader);

    // Indexed by strategy rather than by bucket. The bitstream states one
    // permutation per bucket, but a bucket holds a shape and its transpose, and
    // what the permutation is a permutation *of* is the natural order — which
    // is not the same array for the two of them. libjxl can share one because
    // it keeps every transform's coefficients in one normalised layout; this
    // decoder keeps them in the shape's own, so the permutation has to be
    // composed onto each shape's own order.
    var orders = new int[NumValidStrategies][][];

    // libjxl: no histograms at all when nothing states an order of its own.
    JxlEntropyDecoder? entropy = null;
    if (usedOrders != 0)
      entropy = JxlEntropyDecoder.Read(reader, numContexts: PermutationContexts, disallowLz77: false);

    var computed = 0u;
    for (var o = 0; o < NumValidStrategies; ++o) {
      var ord = StrategyOrder[o];
      var ordBit = 1u << ord;
      if ((computed & ordBit) != 0)
        continue;
      computed |= ordBit;

      var llf = CoveredBlocksX[o] * CoveredBlocksY[o];
      var size = DctBlockSize * llf;

      // Every shape this bucket covers. They share the bucket's permutation and
      // each keeps its own natural order.
      var shapes = new System.Collections.Generic.List<int>();
      for (var other = 0; other < NumValidStrategies; ++other)
        if (StrategyOrder[other] == ord)
          shapes.Add(other);

      foreach (var shape in shapes) {
        orders[shape] = new int[3][];
        var natural = JxlNaturalCoeffOrder.For((JxlAcStrategyType)shape);
        if (natural.Length != size)
          throw new System.IO.InvalidDataException(
            $"The natural order of strategy {shape} has {natural.Length} entries where its shape needs {size}.");
      }

      if ((usedOrders & ordBit) == 0) {
        foreach (var shape in shapes) {
          var natural = JxlNaturalCoeffOrder.For((JxlAcStrategyType)shape);
          for (var c = 0; c < 3; ++c)
            orders[shape][c] = natural;
        }

        continue;
      }

      for (var c = 0; c < 3; ++c) {
        var permutation = _ReadPermutation(entropy!, skip: llf, size: size);
        foreach (var shape in shapes) {
          var natural = JxlNaturalCoeffOrder.For((JxlAcStrategyType)shape);
          var composed = new int[size];
          for (var k = 0; k < size; ++k)
            composed[k] = natural[permutation[k]];
          orders[shape][c] = composed;
        }
      }
    }

    // The permutations were read exactly as written only when the arithmetic
    // decoder is back where it started.
    if (entropy is not null && !entropy.CheckFinalState())
      throw new System.IO.InvalidDataException(
        "The coefficient orders did not end in the arithmetic decoder's initial state.");

    return orders;
  }

  /// <summary>The order for a transform's shape, in the channel's own version.</summary>
  public static int[] For(int[][][] orders, JxlAcStrategyType strategy, int channel) {
    ArgumentNullException.ThrowIfNull(orders);
    return orders[(int)strategy][channel];
  }

  /// <summary>libjxl <c>ReadPermutation</c>. The composition onto a natural
  /// order that <c>DecodeCoeffOrder</c> follows it with is done by the caller,
  /// once per shape sharing this bucket.</summary>
  private static int[] _ReadPermutation(JxlEntropyDecoder entropy, int skip, int size) {
    var lehmer = new int[size];
    var end = entropy.ReadInt(CoeffOrderContext((uint)size)) + skip;
    if (end < skip || end > size)
      throw new System.IO.InvalidDataException(
        $"This frame states a permutation running to {end} of {size}.");

    uint last = 0;
    for (var i = skip; i < end; ++i) {
      var value = entropy.ReadInt(CoeffOrderContext(last));
      if (value < 0 || value >= size - i)
        throw new System.IO.InvalidDataException(
          $"This frame states {value} at position {i} of a permutation of {size}, which names no value still unused.");

      lehmer[i] = value;
      last = (uint)value;
    }

    return JxlLehmerCode.Decode(lehmer, size);
  }

  private static uint _FloorLog2Nonzero(uint x) {
    var n = 0u;
    while ((x >>= 1) != 0) ++n;
    return n;
  }
}
