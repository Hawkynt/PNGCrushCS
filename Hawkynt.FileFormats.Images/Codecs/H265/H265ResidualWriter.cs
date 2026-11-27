using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Writes the transform coefficients of one block — the encode direction of ITU-T H.265, clauses
/// 7.3.8.11 and 9.3.4.2.
/// </summary>
/// <remarks>
/// The mirror of <see cref="H265Residual"/>, pass for pass and context for context. It is written as
/// a mirror on purpose: the contexts this syntax uses are derived from what has already been coded,
/// so the two directions have to walk the block in the same order and reach the same context index at
/// every bin. A writer organised more conveniently — coefficients in raster order, say — would
/// produce a stream that decodes to different numbers, and the first sign of it would be a picture
/// that is merely wrong rather than an error.
/// <para/>
/// Three things the decoder allows are deliberately not produced here, and each is checked rather
/// than assumed: transform skip, transquant bypass and sign data hiding. The first two change what
/// the coefficients mean, and the third requires choosing levels for their parity, which is a
/// rate-distortion decision this encoder does not make. Where the picture parameter set enables one,
/// the flag is written with the value that turns it off.
/// </remarks>
internal static class H265ResidualWriter {

  /// <summary>Table 9-43: the sub-block context for the sixteen positions of a 4x4 block.</summary>
  private static readonly byte[] _SmallBlockContext = [
    0, 1, 4, 5,
    2, 3, 4, 5,
    6, 6, 8, 8,
    7, 7, 8, 8,
  ];

  /// <summary>
  /// Writes one transform block's coefficients.
  /// </summary>
  /// <param name="cabac">The arithmetic encoder, whose contexts this advances.</param>
  /// <param name="coefficients">The levels, row-major and <c>1 &lt;&lt; log2Size</c> across.</param>
  /// <param name="log2Size">The block's size as a base-two logarithm: 2, 3, 4 or 5.</param>
  /// <param name="cIdx">0 for luma, 1 for Cb, 2 for Cr.</param>
  /// <param name="intraPredMode">
  /// The prediction mode of the block, which chooses the scan for the two smallest sizes. Pass a
  /// negative value for an inter block, which always scans diagonally.
  /// </param>
  /// <param name="chromaArrayType">The sequence's chroma format.</param>
  /// <param name="transformSkipEnabled">
  /// Whether the picture parameter set allows a block to be sent untransformed. This writer never
  /// does, but where the tool is enabled the flag that says so still has to be written.
  /// </param>
  /// <param name="log2MaxTransformSkipBlockSize">The largest block that flag applies to.</param>
  internal static void Encode(
    H265CabacEncoder cabac,
    int[] coefficients,
    int log2Size,
    int cIdx,
    int intraPredMode,
    int chromaArrayType,
    bool transformSkipEnabled = false,
    int log2MaxTransformSkipBlockSize = 2) {
    if (transformSkipEnabled && log2Size <= log2MaxTransformSkipBlockSize)
      cabac.EncodeBin(
        cIdx == 0 ? H265CabacContexts.TRANSFORM_SKIP_FLAG_LUMA : H265CabacContexts.TRANSFORM_SKIP_FLAG_CHROMA, 0);

    var scanIdx = _ScanIndex(log2Size, cIdx, intraPredMode, chromaArrayType);
    var subBlockScan = H265ScanOrder.Positions(log2Size - 2, scanIdx);
    var positionScan = H265ScanOrder.Positions(2, scanIdx);
    var subBlocksAcross = 1 << (log2Size - 2);

    _FindLast(
      coefficients, log2Size, subBlockScan, positionScan, subBlocksAcross,
      out var lastSubBlock, out var lastScanPos, out var lastX, out var lastY);

    if (lastSubBlock < 0)
      throw new InvalidOperationException(
        "An H.265 transform block with no significant coefficient reached the residual writer. A block like that is "
        + "stated by its coded-block flag and has no residual syntax at all.");

    _WriteLastPosition(cabac, log2Size, cIdx, scanIdx, lastX, lastY);

    var codedSubBlock = new bool[subBlocksAcross * subBlocksAcross];
    var significant = new bool[16];
    var greaterThanOne = new bool[16];

    var greater1CtxSet = 0;
    var greater1Ctx = 1;
    var previousGreater1Flag = false;
    var anySubBlockWritten = false;

    for (var i = lastSubBlock; i >= 0; --i) {
      var subX = H265ScanOrder.X(subBlockScan, i);
      var subY = H265ScanOrder.Y(subBlockScan, i);
      var subIndex = subY * subBlocksAcross + subX;

      Array.Clear(significant);
      Array.Clear(greaterThanOne);

      var anyHere = false;
      for (var n = 0; n < 16; ++n) {
        var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
        var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);
        if (coefficients[(y << log2Size) + x] == 0)
          continue;

        significant[n] = true;
        anyHere = true;
      }

      // The first and the last sub-block are known to be coded without a flag: the last because the
      // last significant coefficient is in it, and the first because a block with no coefficients at
      // all would not have reached this syntax.
      var inferDirectCurrent = false;
      if (i < lastSubBlock && i > 0) {
        cabac.EncodeBin(
          H265CabacContexts.CODED_SUB_BLOCK_FLAG
          + _CodedSubBlockContext(codedSubBlock, subBlocksAcross, subX, subY, cIdx),
          anyHere ? 1 : 0);
        codedSubBlock[subIndex] = anyHere;
        inferDirectCurrent = true;
      } else
        codedSubBlock[subIndex] = true;

      if (!codedSubBlock[subIndex])
        continue;

      var start = i == lastSubBlock ? lastScanPos - 1 : 15;
      for (var n = start; n >= 0; --n) {
        var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
        var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);

        if (n == 0 && inferDirectCurrent) {
          // Nothing else in this sub-block was significant, so its direct current coefficient must
          // be — the decoder infers it rather than reading a bin, so none is written.
          var anyOther = false;
          for (var k = 1; k <= 15; ++k)
            anyOther |= significant[k];

          if (!anyOther)
            break;
        }

        cabac.EncodeBin(
          H265CabacContexts.SIG_COEFF_FLAG
          + _SignificanceContext(codedSubBlock, subBlocksAcross, log2Size, scanIdx, cIdx, x, y),
          significant[n] ? 1 : 0);
      }

      var firstSignificant = 16;
      var lastSignificant = -1;
      var greater1Count = 0;
      var lastGreater1Position = -1;

      for (var n = 15; n >= 0; --n) {
        if (!significant[n])
          continue;

        if (lastSignificant < 0)
          lastSignificant = n;

        firstSignificant = n;

        if (greater1Count >= 8)
          continue;

        if (greater1Count == 0) {
          greater1CtxSet = i == 0 || cIdx > 0 ? 0 : 2;

          var carried = anySubBlockWritten ? greater1Ctx : 1;
          if (anySubBlockWritten && carried > 0)
            carried = previousGreater1Flag ? 0 : carried + 1;

          if (carried == 0)
            ++greater1CtxSet;

          greater1Ctx = 1;
          anySubBlockWritten = true;
        } else if (greater1Ctx > 0)
          greater1Ctx = previousGreater1Flag ? 0 : greater1Ctx + 1;

        var magnitude = Math.Abs(_At(coefficients, log2Size, positionScan, subX, subY, n));
        greaterThanOne[n] = magnitude > 1;

        var context = greater1CtxSet * 4 + Math.Min(3, greater1Ctx) + (cIdx > 0 ? 16 : 0);
        cabac.EncodeBin(H265CabacContexts.COEFF_ABS_LEVEL_GREATER1_FLAG + context, greaterThanOne[n] ? 1 : 0);
        previousGreater1Flag = greaterThanOne[n];
        ++greater1Count;

        if (greaterThanOne[n] && lastGreater1Position < 0)
          lastGreater1Position = n;
      }

      if (lastSignificant < 0)
        continue;

      // Only one coefficient per sub-block is ever asked whether it exceeds two: the first that
      // exceeded one. Everything else that exceeded one goes straight to the escape coding.
      var greaterThanTwo = false;
      if (lastGreater1Position >= 0) {
        greaterThanTwo = Math.Abs(_At(coefficients, log2Size, positionScan, subX, subY, lastGreater1Position)) > 2;
        cabac.EncodeBin(
          H265CabacContexts.COEFF_ABS_LEVEL_GREATER2_FLAG + greater1CtxSet + (cIdx > 0 ? 4 : 0),
          greaterThanTwo ? 1 : 0);
      }

      for (var n = 15; n >= 0; --n) {
        if (!significant[n])
          continue;

        cabac.EncodeBypass(_At(coefficients, log2Size, positionScan, subX, subY, n) < 0 ? 1 : 0);
      }

      var levelCount = 0;
      var riceParam = 0;
      var lastAbsoluteLevel = 0;

      for (var n = 15; n >= 0; --n) {
        if (!significant[n])
          continue;

        var level = Math.Abs(_At(coefficients, log2Size, positionScan, subX, subY, n));
        var baseLevel = 1 + (greaterThanOne[n] ? 1 : 0) + (n == lastGreater1Position && greaterThanTwo ? 1 : 0);

        // Whether an escape follows depends on whether this coefficient's level could have been
        // stated by the flags alone. Past the eighth significant coefficient no flags were sent at
        // all, so any level above one is an escape.
        var threshold = levelCount < 8 ? n == lastGreater1Position ? 3 : 2 : 1;

        if (baseLevel == threshold) {
          riceParam = Math.Min(riceParam + (lastAbsoluteLevel > 3 * (1 << riceParam) ? 1 : 0), 4);
          _EncodeRemaining(cabac, level - baseLevel, riceParam);
          lastAbsoluteLevel = level;
        }

        ++levelCount;
      }
    }
  }

  /// <summary>The coefficient at scan position <paramref name="n"/> of one sub-block.</summary>
  private static int _At(int[] coefficients, int log2Size, byte[] positionScan, int subX, int subY, int n) {
    var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
    var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);
    return coefficients[(y << log2Size) + x];
  }

  /// <summary>
  /// Finds the significant coefficient that comes last in scan order, which is where the syntax
  /// starts.
  /// </summary>
  private static void _FindLast(
    int[] coefficients, int log2Size, byte[] subBlockScan, byte[] positionScan, int subBlocksAcross,
    out int lastSubBlock, out int lastScanPos, out int lastX, out int lastY) {
    lastSubBlock = -1;
    lastScanPos = -1;
    lastX = 0;
    lastY = 0;

    for (var i = subBlocksAcross * subBlocksAcross - 1; i >= 0; --i) {
      var subX = H265ScanOrder.X(subBlockScan, i);
      var subY = H265ScanOrder.Y(subBlockScan, i);

      for (var n = 15; n >= 0; --n) {
        var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
        var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);
        if (coefficients[(y << log2Size) + x] == 0)
          continue;

        lastSubBlock = i;
        lastScanPos = n;
        lastX = x;
        lastY = y;
        return;
      }
    }
  }

  /// <summary>
  /// Which scan a block is written in — the semantics of <c>scanIdx</c> in clause 7.3.8.11.
  /// </summary>
  private static int _ScanIndex(int log2Size, int cIdx, int intraPredMode, int chromaArrayType) {
    if (intraPredMode < 0)
      return H265ScanOrder.DIAGONAL;

    if (log2Size != 2 && !(log2Size == 3 && (cIdx == 0 || chromaArrayType == 3)))
      return H265ScanOrder.DIAGONAL;

    return intraPredMode switch {
      >= 6 and <= 14 => H265ScanOrder.VERTICAL,
      >= 22 and <= 30 => H265ScanOrder.HORIZONTAL,
      _ => H265ScanOrder.DIAGONAL,
    };
  }

  /// <summary>
  /// Writes where the last significant coefficient is — clauses 7.3.8.11 and 9.3.3.
  /// </summary>
  /// <remarks>
  /// Each coordinate is a truncated unary prefix and, past the fourth value, a fixed-length suffix.
  /// A vertical scan states the transposed position, because the decoder swaps the pair back.
  /// </remarks>
  private static void _WriteLastPosition(
    H265CabacEncoder cabac, int log2Size, int cIdx, int scanIdx, int x, int y) {
    if (scanIdx == H265ScanOrder.VERTICAL)
      (x, y) = (y, x);

    var maximum = (log2Size << 1) - 1;
    var offset = cIdx == 0 ? 3 * (log2Size - 2) + ((log2Size - 1) >> 2) : 15;
    var shift = cIdx == 0 ? (log2Size + 1) >> 2 : log2Size - 2;

    var prefixX = _LastPrefix(x);
    var prefixY = _LastPrefix(y);

    _WriteLastPrefix(cabac, H265CabacContexts.LAST_SIG_COEFF_X_PREFIX, prefixX, maximum, offset, shift);
    _WriteLastPrefix(cabac, H265CabacContexts.LAST_SIG_COEFF_Y_PREFIX, prefixY, maximum, offset, shift);

    _WriteLastSuffix(cabac, prefixX, x);
    _WriteLastSuffix(cabac, prefixY, y);
  }

  /// <summary>The bucket a coordinate falls in: exact below four, then doubling in width.</summary>
  private static int _LastPrefix(int value) {
    if (value < 4)
      return value;

    var prefix = 4;
    while (true) {
      var suffixLength = (prefix >> 1) - 1;
      var start = (1 << suffixLength) * (2 + (prefix & 1));
      if (value < start + (1 << suffixLength))
        return prefix;

      ++prefix;
    }
  }

  private static void _WriteLastPrefix(
    H265CabacEncoder cabac, int contextBase, int prefix, int maximum, int offset, int shift) {
    for (var i = 0; i < prefix; ++i)
      cabac.EncodeBin(contextBase + (i >> shift) + offset, 1);

    if (prefix < maximum)
      cabac.EncodeBin(contextBase + (prefix >> shift) + offset, 0);
  }

  private static void _WriteLastSuffix(H265CabacEncoder cabac, int prefix, int value) {
    if (prefix <= 3)
      return;

    var suffixLength = (prefix >> 1) - 1;
    var start = (1 << suffixLength) * (2 + (prefix & 1));
    cabac.EncodeBypassBits(value - start, suffixLength);
  }

  /// <summary>
  /// The escape coding for a level the flags could not state — clause 9.3.3.11.
  /// </summary>
  /// <remarks>
  /// A Rice code with an exponential-Golomb tail: up to four ones for the quotient, then the
  /// remainder at the Rice parameter's width, and past that a code whose prefix each add a doubling.
  /// Every bin of it is bypassed, so the whole thing costs exactly its own length.
  /// </remarks>
  private static void _EncodeRemaining(H265CabacEncoder cabac, int value, int riceParam) {
    var quotient = value >> riceParam;

    if (quotient < 4) {
      for (var i = 0; i < quotient; ++i)
        cabac.EncodeBypass(1);

      cabac.EncodeBypass(0);
      cabac.EncodeBypassBits(value & ((1 << riceParam) - 1), riceParam);
      return;
    }

    // Past four the prefix counts doublings rather than multiples, and the remainder widens with it.
    var escapeLength = 1;
    while (value >= (((1 << (escapeLength + 1)) + 2) << riceParam))
      ++escapeLength;

    var prefix = escapeLength + 3;
    for (var i = 0; i < prefix; ++i)
      cabac.EncodeBypass(1);

    // The prefix is unary and unbounded, so it ends with a zero however long it ran.
    cabac.EncodeBypass(0);

    var start = ((1 << escapeLength) + 2) << riceParam;
    cabac.EncodeBypassBits(value - start, escapeLength + riceParam);
  }

  /// <summary>The context for <c>coded_sub_block_flag</c> — clause 9.3.4.2.4.</summary>
  private static int _CodedSubBlockContext(bool[] coded, int across, int subX, int subY, int cIdx) {
    var neighbours = 0;

    if (subX < across - 1 && coded[subY * across + subX + 1])
      ++neighbours;

    if (subY < across - 1 && coded[(subY + 1) * across + subX])
      ++neighbours;

    return Math.Min(neighbours, 1) + (cIdx > 0 ? 2 : 0);
  }

  /// <summary>The context for <c>sig_coeff_flag</c> — clause 9.3.4.2.5.</summary>
  private static int _SignificanceContext(
    bool[] coded, int across, int log2Size, int scanIdx, int cIdx, int x, int y) {
    if (log2Size == 2)
      return _SmallBlockContext[(y << 2) + x] + (cIdx > 0 ? 27 : 0);

    if (x + y == 0)
      return cIdx > 0 ? 27 : 0;

    var subX = x >> 2;
    var subY = y >> 2;

    var neighbours = 0;
    if (subX < across - 1 && coded[subY * across + subX + 1])
      neighbours += 1;

    if (subY < across - 1 && coded[(subY + 1) * across + subX])
      neighbours += 2;

    var withinX = x & 3;
    var withinY = y & 3;

    var context = neighbours switch {
      0 => withinX + withinY == 0 ? 2 : withinX + withinY < 3 ? 1 : 0,
      1 => withinY == 0 ? 2 : withinY == 1 ? 1 : 0,
      2 => withinX == 0 ? 2 : withinX == 1 ? 1 : 0,
      _ => 2,
    };

    if (cIdx == 0) {
      if (subX + subY > 0)
        context += 3;

      context += log2Size == 3 ? scanIdx == H265ScanOrder.DIAGONAL ? 9 : 15 : 21;
      return context;
    }

    return 27 + context + (log2Size == 3 ? 9 : 12);
  }
}
