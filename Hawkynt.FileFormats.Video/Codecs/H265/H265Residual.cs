using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Reads the transform coefficients of one block — ITU-T H.265, clauses 7.3.8.11 and 9.3.4.2.
/// </summary>
/// <remarks>
/// The most intricate syntax in the standard, and the one where being one bin out of step is
/// indistinguishable from working. It reads a block in five passes over each sixteen-coefficient
/// sub-block, deliberately: every pass groups bins that share a context model, so a context sees a
/// run of decisions with the same statistics rather than one of each kind in turn.
/// <para/>
/// <b>It starts from the end.</b> The position of the last significant coefficient is coded first,
/// and everything after it in scan order is known to be zero without a bin being spent — which is
/// where most of the saving is, because a transform block's coefficients cluster at the low
/// frequencies and the tail is long.
/// <para/>
/// <b>One sign per sub-block may not be transmitted at all.</b> When the significant coefficients of
/// a sub-block are spread far enough apart, the encoder chooses levels whose sum has the parity of
/// the sign it wanted to send, and sends no sign. A decoder that read the sign anyway would be one
/// bypass bin ahead for the rest of the slice. That is <c>sign_data_hiding_enabled_flag</c>, and it
/// is on by default in every encoder anyone uses.
/// <para/>
/// <b>The scan is chosen from the intra prediction mode</b> for the two smallest block sizes, so this
/// has to be told what that mode was — a residual whose energy runs vertically is read down its
/// columns. Every other block is read along the diagonal.
/// </remarks>
internal static class H265Residual {

  /// <summary>Table 9-43: the sub-block context for the sixteen positions of a 4x4 block.</summary>
  private static readonly byte[] _SmallBlockContext = [
    0, 1, 4, 5,
    2, 3, 4, 5,
    6, 6, 8, 8,
    7, 7, 8, 8,
  ];

  /// <summary>
  /// Reads one transform block's coefficients into <paramref name="coefficients"/>.
  /// </summary>
  /// <param name="log2Size">The block's size as a base-two logarithm: 2, 3, 4 or 5.</param>
  /// <param name="cIdx">0 for luma, 1 for Cb, 2 for Cr.</param>
  /// <param name="intraPredMode">
  /// The prediction mode of the block, which chooses the scan for the two smallest sizes. Pass a
  /// negative value for an inter block, which always scans diagonally.
  /// </param>
  /// <returns>Whether the residual was sent untransformed.</returns>
  internal static bool Decode(
    ref H265CabacEngine cabac,
    int[] coefficients,
    int log2Size,
    int cIdx,
    int intraPredMode,
    H265PictureParameterSet pps,
    bool transquantBypass) {
    var size = 1 << log2Size;
    Array.Clear(coefficients, 0, size * size);

    var transformSkip = false;
    if (pps.TransformSkipEnabled && !transquantBypass && log2Size <= pps.Log2MaxTransformSkipBlockSize)
      transformSkip = cabac.DecodeBin(
        cIdx == 0 ? H265CabacContexts.TRANSFORM_SKIP_FLAG_LUMA : H265CabacContexts.TRANSFORM_SKIP_FLAG_CHROMA) != 0;

    var scanIdx = _ScanIndex(log2Size, cIdx, intraPredMode);

    _ReadLastPosition(ref cabac, log2Size, cIdx, out var lastX, out var lastY);
    if (scanIdx == H265ScanOrder.VERTICAL)
      (lastX, lastY) = (lastY, lastX);

    var subBlockScan = H265ScanOrder.Positions(log2Size - 2, scanIdx);
    var positionScan = H265ScanOrder.Positions(2, scanIdx);

    // Walk backwards from the end of the block to find which sub-block the last significant
    // coefficient is in and where inside it, which is where reading starts.
    var subBlocksAcross = 1 << (log2Size - 2);
    var lastSubBlock = subBlocksAcross * subBlocksAcross - 1;
    var lastScanPos = 16;

    while (true) {
      if (lastScanPos == 0) {
        lastScanPos = 16;
        --lastSubBlock;
      }

      --lastScanPos;

      var subX = H265ScanOrder.X(subBlockScan, lastSubBlock);
      var subY = H265ScanOrder.Y(subBlockScan, lastSubBlock);
      var x = (subX << 2) + H265ScanOrder.X(positionScan, lastScanPos);
      var y = (subY << 2) + H265ScanOrder.Y(positionScan, lastScanPos);

      if (x == lastX && y == lastY)
        break;

      if (lastSubBlock < 0)
        throw new System.IO.InvalidDataException(
          $"An H.265 transform block states its last significant coefficient at ({lastX}, {lastY}), which is not on "
          + "the scan this block uses. The entropy decoder is out of step with the bitstream.");
    }

    var codedSubBlock = new bool[subBlocksAcross * subBlocksAcross];
    var significant = new bool[16];
    var greaterThanOne = new bool[16];

    var greater1CtxSet = 0;
    var greater1Ctx = 1;
    var previousGreater1Flag = false;
    var anySubBlockRead = false;

    for (var i = lastSubBlock; i >= 0; --i) {
      var subX = H265ScanOrder.X(subBlockScan, i);
      var subY = H265ScanOrder.Y(subBlockScan, i);
      var subIndex = subY * subBlocksAcross + subX;

      // The first and the last sub-block are known to be coded without a flag: the last because the
      // last significant coefficient is in it, and the first because a block with no coefficients at
      // all would not have reached this syntax.
      var inferDirectCurrent = false;
      if (i < lastSubBlock && i > 0) {
        codedSubBlock[subIndex] = cabac.DecodeBin(
          H265CabacContexts.CODED_SUB_BLOCK_FLAG
          + _CodedSubBlockContext(codedSubBlock, subBlocksAcross, subX, subY, cIdx)) != 0;
        inferDirectCurrent = true;
      } else
        codedSubBlock[subIndex] = true;

      Array.Clear(significant);
      Array.Clear(greaterThanOne);

      var start = i == lastSubBlock ? lastScanPos - 1 : 15;
      if (i == lastSubBlock)
        significant[lastScanPos] = true;

      if (codedSubBlock[subIndex])
        for (var n = start; n >= 0; --n) {
          var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
          var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);

          if (n == 0 && inferDirectCurrent) {
            // Nothing else in this sub-block was significant, so its direct current coefficient must
            // be — otherwise the sub-block would have been flagged as not coded at all.
            var anyOther = false;
            for (var k = 1; k <= 15; ++k)
              anyOther |= significant[k];

            if (!anyOther) {
              significant[0] = true;
              break;
            }
          }

          significant[n] = cabac.DecodeBin(
            H265CabacContexts.SIG_COEFF_FLAG
            + _SignificanceContext(codedSubBlock, subBlocksAcross, log2Size, scanIdx, cIdx, x, y)) != 0;
        }

      var firstSignificant = 16;
      var lastSignificant = -1;
      var greater1Count = 0;
      var lastGreater1Position = -1;

      // The first pass over the sub-block that reads levels: up to eight coefficients are asked
      // whether they exceed one, and the context walks along a chain whose state says how many of
      // the ones before it did.
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

          var carried = anySubBlockRead ? greater1Ctx : 1;
          if (anySubBlockRead && carried > 0)
            carried = previousGreater1Flag ? 0 : carried + 1;

          if (carried == 0)
            ++greater1CtxSet;

          greater1Ctx = 1;
          anySubBlockRead = true;
        } else if (greater1Ctx > 0)
          greater1Ctx = previousGreater1Flag ? 0 : greater1Ctx + 1;

        var context = greater1CtxSet * 4 + Math.Min(3, greater1Ctx) + (cIdx > 0 ? 16 : 0);
        greaterThanOne[n] = cabac.DecodeBin(H265CabacContexts.COEFF_ABS_LEVEL_GREATER1_FLAG + context) != 0;
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
      if (lastGreater1Position >= 0)
        greaterThanTwo = cabac.DecodeBin(
          H265CabacContexts.COEFF_ABS_LEVEL_GREATER2_FLAG + greater1CtxSet + (cIdx > 0 ? 4 : 0)) != 0;

      var signHidden = lastSignificant - firstSignificant > 3 && !transquantBypass;
      var hideSign = pps.SignDataHidingEnabled && signHidden;

      var signs = 0;
      var signCount = 0;
      for (var n = 15; n >= 0; --n) {
        if (!significant[n] || (hideSign && n == firstSignificant))
          continue;

        signs = (signs << 1) | cabac.DecodeBypass();
        ++signCount;
      }

      var levelCount = 0;
      var levelSum = 0;
      var riceParam = 0;
      var lastAbsoluteLevel = 0;
      var signBit = signCount - 1;

      for (var n = 15; n >= 0; --n) {
        if (!significant[n])
          continue;

        var baseLevel = 1 + (greaterThanOne[n] ? 1 : 0) + (n == lastGreater1Position && greaterThanTwo ? 1 : 0);

        // Whether an escape follows depends on whether this coefficient's level could have been
        // stated by the flags alone. Past the eighth significant coefficient no flags were sent at
        // all, so any level above one is an escape.
        var threshold = levelCount < 8 ? n == lastGreater1Position ? 3 : 2 : 1;

        var level = baseLevel;
        if (baseLevel == threshold) {
          // The Rice parameter adapts to what this sub-block has held so far, so a block of large
          // coefficients stops paying a unary prefix for each of them. It adapts from the levels the
          // escape coding itself has carried and not from every level in the sub-block: a run of
          // coefficients that the flags alone could state says nothing about how wide the ones that
          // could not will be.
          riceParam = Math.Min(riceParam + (lastAbsoluteLevel > 3 * (1 << riceParam) ? 1 : 0), 4);
          level += _DecodeRemaining(ref cabac, riceParam);
          lastAbsoluteLevel = level;
        }

        var negative = false;
        if (hideSign && n == firstSignificant) {
          // The sign that was never sent: the encoder chose levels whose sum has its parity.
          levelSum += level;
          negative = (levelSum & 1) != 0;
        } else {
          negative = ((signs >> signBit) & 1) != 0;
          --signBit;
          levelSum += level;
        }

        var x = (subX << 2) + H265ScanOrder.X(positionScan, n);
        var y = (subY << 2) + H265ScanOrder.Y(positionScan, n);
        coefficients[(y << log2Size) + x] = negative ? -level : level;
        ++levelCount;
      }
    }

    return transformSkip;
  }

  /// <summary>
  /// Which scan a block is read in — the semantics of <c>scanIdx</c> in clause 7.3.8.11.
  /// </summary>
  /// <remarks>
  /// Only the two smallest luma blocks and the smallest chroma ones choose; everything larger reads
  /// diagonally. A mode near horizontal predicts along rows, so what the residual is left with runs
  /// down the columns and is read vertically — and the other way about for a mode near vertical.
  /// </remarks>
  private static int _ScanIndex(int log2Size, int cIdx, int intraPredMode) {
    if (intraPredMode < 0)
      return H265ScanOrder.DIAGONAL;

    if (log2Size != 2 && !(log2Size == 3 && cIdx == 0))
      return H265ScanOrder.DIAGONAL;

    return intraPredMode switch {
      >= 6 and <= 14 => H265ScanOrder.VERTICAL,
      >= 22 and <= 30 => H265ScanOrder.HORIZONTAL,
      _ => H265ScanOrder.DIAGONAL,
    };
  }

  /// <summary>
  /// Reads where the last significant coefficient is — clauses 7.3.8.11 and 9.3.3.
  /// </summary>
  /// <remarks>
  /// Each coordinate is a truncated unary prefix and, past the fourth value, a fixed-length suffix.
  /// The prefix names a bucket whose width doubles, so a position early in the block is stated
  /// exactly and one late in it approximately — which is the right way round, because the last
  /// significant coefficient of a real block is nearly always early.
  /// </remarks>
  private static void _ReadLastPosition(ref H265CabacEngine cabac, int log2Size, int cIdx, out int x, out int y) {
    var maximum = (log2Size << 1) - 1;

    var offset = cIdx == 0 ? 3 * (log2Size - 2) + ((log2Size - 1) >> 2) : 15;
    var shift = cIdx == 0 ? (log2Size + 1) >> 2 : log2Size - 2;

    var prefixX = _ReadLastPrefix(ref cabac, H265CabacContexts.LAST_SIG_COEFF_X_PREFIX, maximum, offset, shift);
    var prefixY = _ReadLastPrefix(ref cabac, H265CabacContexts.LAST_SIG_COEFF_Y_PREFIX, maximum, offset, shift);

    x = _LastPosition(ref cabac, prefixX);
    y = _LastPosition(ref cabac, prefixY);
  }

  private static int _ReadLastPrefix(
    ref H265CabacEngine cabac, int contextBase, int maximum, int offset, int shift) {
    var prefix = 0;
    while (prefix < maximum && cabac.DecodeBin(contextBase + (prefix >> shift) + offset) != 0)
      ++prefix;

    return prefix;
  }

  private static int _LastPosition(ref H265CabacEngine cabac, int prefix) {
    if (prefix <= 3)
      return prefix;

    var suffixLength = (prefix >> 1) - 1;
    var suffix = cabac.DecodeBypassBits(suffixLength);
    return ((1 << suffixLength) * (2 + (prefix & 1))) + suffix;
  }

  /// <summary>
  /// The escape coding for a level the flags could not state — clause 9.3.3.11.
  /// </summary>
  /// <remarks>
  /// A Rice code with an exponential-Golomb tail: up to four ones for the quotient, then the
  /// remainder at the Rice parameter's width, and if all four ones arrived the value carries on into
  /// a code whose own prefix each add a doubling. Every bin of it is bypassed, so the whole thing
  /// costs exactly its own length.
  /// </remarks>
  private static int _DecodeRemaining(ref H265CabacEngine cabac, int riceParam) {
    var prefix = 0;
    while (prefix < 32 && cabac.DecodeBypass() != 0)
      ++prefix;

    if (prefix >= 32)
      throw new System.IO.InvalidDataException(
        "An H.265 coefficient level escape code began with 32 one bits, which no conforming stream contains. The "
        + "entropy decoder is out of step with the bitstream.");

    if (prefix < 4)
      return (prefix << riceParam) + cabac.DecodeBypassBits(riceParam);

    var escapeLength = prefix - 3;
    return (((1 << escapeLength) + 2) << riceParam) + cabac.DecodeBypassBits(escapeLength + riceParam);
  }

  /// <summary>
  /// The context for <c>coded_sub_block_flag</c> — clause 9.3.4.2.4: whether the neighbours were coded.
  /// </summary>
  private static int _CodedSubBlockContext(bool[] coded, int across, int subX, int subY, int cIdx) {
    var neighbours = 0;

    if (subX < across - 1 && coded[subY * across + subX + 1])
      ++neighbours;

    if (subY < across - 1 && coded[(subY + 1) * across + subX])
      ++neighbours;

    return Math.Min(neighbours, 1) + (cIdx > 0 ? 2 : 0);
  }

  /// <summary>
  /// The context for <c>sig_coeff_flag</c> — clause 9.3.4.2.5.
  /// </summary>
  /// <remarks>
  /// Where in the sub-block the coefficient is, and which of the two neighbouring sub-blocks held
  /// anything. Those two together say a great deal: a coefficient at the top left of a sub-block
  /// whose right-hand neighbour was coded is far more likely to be significant than one at the
  /// bottom right of an isolated sub-block, and the twenty-seven luma contexts are that judgement
  /// crossed with the block size and the scan.
  /// </remarks>
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
