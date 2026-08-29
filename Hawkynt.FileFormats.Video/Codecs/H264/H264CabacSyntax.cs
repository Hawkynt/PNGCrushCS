using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>Transform-block categories from H.264 Table 9-42 used by CABAC residual syntax.</summary>
internal enum H264CabacBlockType : byte {
  Luma16x16Dc = 0,
  Luma16x16Ac = 1,
  Luma4x4 = 2,
  ChromaDc = 3,
  ChromaAc = 4,
  Luma8x8 = 5,
}

/// <summary>Neighbour macroblock state needed by CABAC context derivation.</summary>
internal readonly record struct H264CabacMbNeighbour(
  bool Available,
  bool Skipped,
  bool IsPcm,
  bool IsINxN,
  bool IsBDirect,
  bool IsInter,
  int CbpLuma,
  int CbpChroma,
  int IntraChromaMode,
  bool Transform8x8
);

/// <summary>
/// H.264 CABAC syntax-element binarizations and context selection (clause 9.3.2 / 9.3.3.1).
/// </summary>
/// <remarks>
/// Adapted to C# from OxideAV/oxideav-h264 <c>src/cabac_ctx.rs</c>, Copyright (c) 2026 Karpeles Lab Inc.,
/// MIT License, and cross-checked against FFmpeg <c>libavcodec/h264_cabac.c</c>, Copyright (c) 2003
/// Michael Niedermayer, LGPL-2.1-or-later. Context numbers and binarizations are normative H.264 tables.
/// This implementation intentionally covers the decoder's progressive 8-bit 4:2:0 AVC scope.
/// </remarks>
internal static class H264CabacSyntax {
  private static readonly byte[] _SIG_8X8 = [
    0,1,2,3,4,5,5,4,4,3,3,4,4,4,5,5,
    4,4,4,4,3,3,6,7,7,7,8,9,10,9,8,7,
    7,6,11,12,13,11,6,7,8,9,14,10,9,8,6,11,
    12,13,11,6,9,14,10,9,11,12,13,11,14,10,12,
  ];

  private static readonly byte[] _LAST_8X8 = [
    0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
    2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,
    3,3,3,3,3,3,3,3,4,4,4,4,4,4,4,4,
    5,5,5,5,6,6,6,6,7,7,7,7,8,8,8,
  ];

  internal static bool DecodeSkipFlag(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    bool isB,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    var offset = isB ? 24 : 11;
    var increment = (left.Available && !left.Skipped ? 1 : 0)
      + (above.Available && !above.Skipped ? 1 : 0);
    return decoder.DecodeDecision(ref contexts[offset + increment]) != 0;
  }

  internal static int DecodeMbTypeI(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    var increment = _MbTypeCondI(left) + _MbTypeCondI(above);
    var first = decoder.DecodeDecision(ref contexts[3 + increment]);
    if (first == 0)
      return 0;
    return _DecodeITypeSuffix(ref decoder, contexts, 3, firstAlreadyRead: true);
  }

  internal static int DecodeMbTypeP(ref H264CabacDecoder decoder, H264CabacContexts contexts) {
    var b0 = decoder.DecodeDecision(ref contexts[14]);
    if (b0 != 0)
      return 5 + _DecodeITypeSuffix(ref decoder, contexts, 17, firstAlreadyRead: false);

    var b1 = decoder.DecodeDecision(ref contexts[15]);
    var b2 = decoder.DecodeDecision(ref contexts[14 + (b1 != 1 ? 2 : 3)]);
    return (b1, b2) switch {
      (0, 0) => 0,
      (0, 1) => 3,
      (1, 1) => 1,
      (1, 0) => 2,
      _ => throw new InvalidDataException("An H.264 CABAC P mb_type decoded an impossible bin string."),
    };
  }

  internal static int DecodeMbTypeB(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    var increment = _MbTypeCondB(left) + _MbTypeCondB(above);
    var b0 = decoder.DecodeDecision(ref contexts[27 + increment]);
    if (b0 == 0)
      return 0;

    var b1 = decoder.DecodeDecision(ref contexts[30]);
    var b2 = decoder.DecodeDecision(ref contexts[27 + (b1 != 0 ? 4 : 5)]);
    if (b1 == 0)
      return b2 == 0 ? 1 : 2;

    var b3 = decoder.DecodeDecision(ref contexts[32]);
    var b4 = decoder.DecodeDecision(ref contexts[32]);
    var b5 = decoder.DecodeDecision(ref contexts[32]);
    if (b2 == 0)
      return 3 + (b3 << 2) + (b4 << 1) + b5;

    if (b3 == 1 && b4 == 1 && b5 == 1)
      return 22;
    if (b3 == 1 && b4 == 1 && b5 == 0)
      return 11;
    if (b3 == 1 && b4 == 0 && b5 == 1)
      return 23 + _DecodeITypeSuffix(ref decoder, contexts, 32, firstAlreadyRead: false);

    var b6 = decoder.DecodeDecision(ref contexts[32]);
    return 12 + (b3 << 3) + (b4 << 2) + (b5 << 1) + b6;
  }

  internal static int DecodeSubMbTypeP(ref H264CabacDecoder decoder, H264CabacContexts contexts) {
    if (decoder.DecodeDecision(ref contexts[21]) != 0)
      return 0;
    if (decoder.DecodeDecision(ref contexts[22]) == 0)
      return 1;
    return decoder.DecodeDecision(ref contexts[23]) != 0 ? 2 : 3;
  }

  internal static int DecodeSubMbTypeB(ref H264CabacDecoder decoder, H264CabacContexts contexts) {
    if (decoder.DecodeDecision(ref contexts[36]) == 0)
      return 0;
    var b1 = decoder.DecodeDecision(ref contexts[37]);
    var b2 = decoder.DecodeDecision(ref contexts[36 + (b1 != 0 ? 2 : 3)]);
    if (b1 == 0)
      return b2 == 0 ? 1 : 2;

    var b3 = decoder.DecodeDecision(ref contexts[39]);
    var b4 = decoder.DecodeDecision(ref contexts[39]);
    if (b2 == 0)
      return 3 + (b3 << 1) + b4;
    if (b3 != 0)
      return b4 == 0 ? 11 : 12;
    var b5 = decoder.DecodeDecision(ref contexts[39]);
    return 7 + (b4 << 1) + b5;
  }

  internal static int DecodeReferenceIndex(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    bool leftGreaterThanZero,
    bool aboveGreaterThanZero,
    int activeCount) {
    if (activeCount <= 1)
      return 0;

    var increment = (leftGreaterThanZero ? 1 : 0) + (aboveGreaterThanZero ? 2 : 0);
    if (decoder.DecodeDecision(ref contexts[54 + increment]) == 0)
      return 0;
    if (decoder.DecodeDecision(ref contexts[58]) == 0)
      return 1;

    var value = 2;
    while (decoder.DecodeDecision(ref contexts[59]) != 0) {
      ++value;
      if (value >= activeCount)
        throw new InvalidDataException(
          $"An H.264 CABAC ref_idx decoded {value}, beyond the {activeCount} active reference pictures.");
    }
    if (value >= activeCount)
      throw new InvalidDataException(
        $"An H.264 CABAC ref_idx decoded {value}, beyond the {activeCount} active reference pictures.");
    return value;
  }

  internal static int DecodeMotionVectorDifference(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    bool vertical,
    int neighbourAbsoluteSum) {
    var offset = vertical ? 47 : 40;
    var firstIncrement = neighbourAbsoluteSum > 32 ? 2 : neighbourAbsoluteSum > 2 ? 1 : 0;
    ReadOnlySpan<int> increments = [firstIncrement, 3, 4, 5, 6, 6, 6, 6, 6];
    var magnitude = 0;
    for (var i = 0; i < increments.Length; ++i) {
      if (decoder.DecodeDecision(ref contexts[offset + increments[i]]) == 0) {
        magnitude = i;
        break;
      }
      magnitude = i + 1;
    }

    if (magnitude >= 9) {
      var k = 3;
      long suffix = 0;
      while (decoder.DecodeBypass() != 0) {
        if (k >= 30)
          throw new InvalidDataException("An H.264 CABAC mvd_lX UEG3 escape exceeds the supported integer range.");
        suffix += 1L << k;
        ++k;
      }
      long tail = 0;
      for (var i = 0; i < k; ++i)
        tail = (tail << 1) | (uint)decoder.DecodeBypass();
      var extended = 9L + suffix + tail;
      if (extended > short.MaxValue * 2L)
        throw new InvalidDataException($"An H.264 CABAC motion-vector difference magnitude {extended} is not representable.");
      magnitude = (int)extended;
    }

    return magnitude == 0 ? 0 : decoder.DecodeBypassSigned(magnitude);
  }

  internal static int DecodeIntraChromaPredMode(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    var increment = _IntraChromaCond(left) + _IntraChromaCond(above);
    if (decoder.DecodeDecision(ref contexts[64 + increment]) == 0)
      return 0;
    if (decoder.DecodeDecision(ref contexts[67]) == 0)
      return 1;
    return decoder.DecodeDecision(ref contexts[67]) == 0 ? 2 : 3;
  }

  internal static int DecodeIntraPredictionMode(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int predictedMode) {
    if (decoder.DecodeDecision(ref contexts[68]) != 0)
      return predictedMode;
    var remaining = 0;
    // rem_intra4x4_pred_mode is FL cMax=7 and its first bin is the least significant bit.
    for (var bit = 0; bit < 3; ++bit)
      remaining |= decoder.DecodeDecision(ref contexts[69]) << bit;
    return remaining < predictedMode ? remaining : remaining + 1;
  }

  internal static bool DecodeTransform8x8Flag(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    var increment = (left.Available && left.Transform8x8 ? 1 : 0)
      + (above.Available && above.Transform8x8 ? 1 : 0);
    return decoder.DecodeDecision(ref contexts[399 + increment]) != 0;
  }

  internal static int DecodeCodedBlockPattern(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacMbNeighbour left,
    H264CabacMbNeighbour above) {
    Span<int> bins = stackalloc int[4];
    var luma = 0;
    for (var index = 0; index < 4; ++index) {
      var condLeft = index switch {
        0 => _CbpLumaExternal(left, 1),
        1 => bins[0] == 0 ? 1 : 0,
        2 => _CbpLumaExternal(left, 3),
        3 => bins[2] == 0 ? 1 : 0,
        _ => 0,
      };
      var condAbove = index switch {
        0 => _CbpLumaExternal(above, 2),
        1 => _CbpLumaExternal(above, 3),
        2 => bins[0] == 0 ? 1 : 0,
        3 => bins[1] == 0 ? 1 : 0,
        _ => 0,
      };
      bins[index] = decoder.DecodeDecision(ref contexts[73 + condLeft + 2 * condAbove]);
      luma |= bins[index] << index;
    }

    var condA0 = _CbpChromaExternal(left, 0);
    var condB0 = _CbpChromaExternal(above, 0);
    if (decoder.DecodeDecision(ref contexts[77 + condA0 + 2 * condB0]) == 0)
      return luma;

    var condA1 = _CbpChromaExternal(left, 1);
    var condB1 = _CbpChromaExternal(above, 1);
    var second = decoder.DecodeDecision(ref contexts[81 + condA1 + 2 * condB1]);
    return luma | ((second == 0 ? 1 : 2) << 4);
  }

  internal static int DecodeMbQpDelta(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    bool previousDeltaNonZero) {
    if (decoder.DecodeDecision(ref contexts[60 + (previousDeltaNonZero ? 1 : 0)]) == 0)
      return 0;

    var code = 1;
    var context = 62;
    while (decoder.DecodeDecision(ref contexts[context]) != 0) {
      ++code;
      context = 63;
      if (code > 103)
        throw new InvalidDataException("An H.264 CABAC mb_qp_delta unary code exceeds the 8-bit QP syntax range.");
    }
    var magnitude = (code + 1) >> 1;
    return (code & 1) != 0 ? magnitude : -magnitude;
  }

  internal static bool DecodeCodedBlockFlag(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacBlockType blockType,
    bool leftCondition,
    bool aboveCondition) {
    if (blockType == H264CabacBlockType.Luma8x8)
      throw new InvalidOperationException(
        "Progressive 4:2:0 luma 8x8 residuals infer coded-block presence from CBP and do not code coded_block_flag.");
    var baseContext = blockType switch {
      H264CabacBlockType.Luma16x16Dc => 85,
      H264CabacBlockType.Luma16x16Ac => 89,
      H264CabacBlockType.Luma4x4 => 93,
      H264CabacBlockType.ChromaDc => 97,
      H264CabacBlockType.ChromaAc => 101,
      _ => throw new ArgumentOutOfRangeException(nameof(blockType)),
    };
    var increment = (leftCondition ? 1 : 0) + (aboveCondition ? 2 : 0);
    return decoder.DecodeDecision(ref contexts[baseContext + increment]) != 0;
  }

  /// <summary>
  /// Decodes significant/last flags, coefficient magnitudes, and signs for one block whose
  /// coded_block_flag (or 8x8 CBP inference) is already known to be one.
  /// </summary>
  /// <returns>The number of non-zero coefficients.</returns>
  internal static int DecodeResidualBlock(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacBlockType blockType,
    Span<int> coefficients,
    int startIndex,
    int endIndex) {
    if (startIndex < 0 || endIndex < startIndex || endIndex >= coefficients.Length)
      throw new ArgumentOutOfRangeException(nameof(startIndex));

    coefficients[startIndex..(endIndex + 1)].Clear();
    Span<byte> significantPositions = stackalloc byte[64];
    var count = 0;
    var terminated = false;
    for (var index = startIndex; index < endIndex; ++index) {
      var sigContext = _SignificantContext(blockType, index);
      if (decoder.DecodeDecision(ref contexts[sigContext]) == 0)
        continue;

      significantPositions[count++] = (byte)index;
      var lastContext = _LastContext(blockType, index);
      if (decoder.DecodeDecision(ref contexts[lastContext]) != 0) {
        terminated = true;
        break;
      }
    }

    // When no last_significant_coeff_flag terminated the scan, the final position is inferred to be significant.
    if (!terminated)
      significantPositions[count++] = (byte)endIndex;
    if (count == 0)
      throw new InvalidDataException("An H.264 CABAC coded residual block contained no significant coefficient.");

    var equalOne = 0;
    var greaterThanOne = 0;
    for (var n = count - 1; n >= 0; --n) {
      var absoluteMinusOne = _DecodeAbsLevelMinusOne(
        ref decoder, contexts, blockType, equalOne, greaterThanOne);
      var absolute = absoluteMinusOne + 1;
      if (absolute == 1)
        ++equalOne;
      else
        ++greaterThanOne;
      coefficients[significantPositions[n]] = decoder.DecodeBypassSigned(absolute);
    }
    return count;
  }

  private static int _DecodeITypeSuffix(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    int offset,
    bool firstAlreadyRead) {
    var b0 = firstAlreadyRead ? 1 : decoder.DecodeDecision(ref contexts[offset]);
    if (b0 == 0)
      return 0;
    if (decoder.DecodeTerminate() != 0)
      return 25;

    int inc2, inc3, inc4WhenOne, inc4WhenZero, inc5WhenOne, inc5WhenZero, inc6;
    if (offset == 3) {
      (inc2, inc3, inc4WhenOne, inc4WhenZero, inc5WhenOne, inc5WhenZero, inc6) = (3, 4, 5, 6, 6, 7, 7);
    } else {
      (inc2, inc3, inc4WhenOne, inc4WhenZero, inc5WhenOne, inc5WhenZero, inc6) = (1, 2, 2, 3, 3, 3, 3);
    }

    var b2 = decoder.DecodeDecision(ref contexts[offset + inc2]);
    var b3 = decoder.DecodeDecision(ref contexts[offset + inc3]);
    var b4 = decoder.DecodeDecision(ref contexts[offset + (b3 != 0 ? inc4WhenOne : inc4WhenZero)]);
    var b5 = decoder.DecodeDecision(ref contexts[offset + (b3 != 0 ? inc5WhenOne : inc5WhenZero)]);
    var b6 = b3 != 0 ? decoder.DecodeDecision(ref contexts[offset + inc6]) : 0;

    var baseValue = 12 * b2;
    return b3 == 0
      ? baseValue + 1 + (b4 << 1) + b5
      : baseValue + 5 + 4 * b4 + (b5 << 1) + b6;
  }

  private static int _DecodeAbsLevelMinusOne(
    ref H264CabacDecoder decoder,
    H264CabacContexts contexts,
    H264CabacBlockType blockType,
    int equalOne,
    int greaterThanOne) {
    var baseContext = blockType switch {
      H264CabacBlockType.Luma16x16Dc => 227,
      H264CabacBlockType.Luma16x16Ac => 237,
      H264CabacBlockType.Luma4x4 => 247,
      H264CabacBlockType.ChromaDc => 257,
      H264CabacBlockType.ChromaAc => 266,
      H264CabacBlockType.Luma8x8 => 426,
      _ => throw new ArgumentOutOfRangeException(nameof(blockType)),
    };
    var firstIncrement = greaterThanOne != 0 ? 0 : Math.Min(4, 1 + equalOne);
    if (decoder.DecodeDecision(ref contexts[baseContext + firstIncrement]) == 0)
      return 0;

    var maxRestIncrement = blockType == H264CabacBlockType.ChromaDc ? 3 : 4;
    var restIncrement = 5 + Math.Min(maxRestIncrement, greaterThanOne);
    var prefix = 1;
    while (prefix < 14) {
      if (decoder.DecodeDecision(ref contexts[baseContext + restIncrement]) == 0)
        return prefix;
      ++prefix;
    }

    return 14 + decoder.DecodeBypassExpGolomb(0);
  }

  private static int _SignificantContext(H264CabacBlockType blockType, int index) {
    var baseContext = blockType switch {
      H264CabacBlockType.Luma16x16Dc => 105,
      H264CabacBlockType.Luma16x16Ac => 120,
      H264CabacBlockType.Luma4x4 => 134,
      H264CabacBlockType.ChromaDc => 149,
      H264CabacBlockType.ChromaAc => 152,
      H264CabacBlockType.Luma8x8 => 402,
      _ => throw new ArgumentOutOfRangeException(nameof(blockType)),
    };
    var increment = blockType switch {
      H264CabacBlockType.ChromaDc => Math.Min(index, 2),
      H264CabacBlockType.Luma8x8 => _SIG_8X8[index],
      _ => index,
    };
    return baseContext + increment;
  }

  private static int _LastContext(H264CabacBlockType blockType, int index) {
    var baseContext = blockType switch {
      H264CabacBlockType.Luma16x16Dc => 166,
      H264CabacBlockType.Luma16x16Ac => 181,
      H264CabacBlockType.Luma4x4 => 195,
      H264CabacBlockType.ChromaDc => 210,
      H264CabacBlockType.ChromaAc => 213,
      H264CabacBlockType.Luma8x8 => 417,
      _ => throw new ArgumentOutOfRangeException(nameof(blockType)),
    };
    var increment = blockType switch {
      H264CabacBlockType.ChromaDc => Math.Min(index, 2),
      H264CabacBlockType.Luma8x8 => _LAST_8X8[index],
      _ => index,
    };
    return baseContext + increment;
  }

  private static int _MbTypeCondI(H264CabacMbNeighbour neighbour)
    => neighbour.Available && !neighbour.IsINxN ? 1 : 0;

  private static int _MbTypeCondB(H264CabacMbNeighbour neighbour)
    => neighbour.Available && !(neighbour.Skipped || neighbour.IsBDirect) ? 1 : 0;

  private static int _IntraChromaCond(H264CabacMbNeighbour neighbour)
    => neighbour.Available && !neighbour.IsInter && !neighbour.IsPcm && neighbour.IntraChromaMode != 0 ? 1 : 0;

  private static int _CbpLumaExternal(H264CabacMbNeighbour neighbour, int bitIndex) {
    if (!neighbour.Available || neighbour.IsPcm)
      return 0;
    if (neighbour.Skipped)
      return 1;
    return (neighbour.CbpLuma & (1 << bitIndex)) != 0 ? 0 : 1;
  }

  private static int _CbpChromaExternal(H264CabacMbNeighbour neighbour, int binIndex) {
    if (!neighbour.Available)
      return 0;
    if (neighbour.IsPcm)
      return 1;
    if (neighbour.Skipped)
      return 0;
    return binIndex == 0
      ? neighbour.CbpChroma == 0 ? 0 : 1
      : neighbour.CbpChroma != 2 ? 0 : 1;
  }
}
