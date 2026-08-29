using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Reads one block of transform coefficient levels — <c>residual_block_cavlc</c>, ITU-T H.264
/// clause 7.3.5.3.3, decoded by clauses 9.2.1 to 9.2.4.
/// </summary>
/// <remarks>
/// CAVLC codes a block backwards and in pieces, which is worth stating plainly because nothing about
/// the loop below reads as a scan of coefficients. First the count: how many non-zero levels there
/// are and how many of those are the plus or minus ones that a transform's tail is nearly always made
/// of. Then the levels themselves, from the highest frequency down, each read out of a code whose
/// shape adapts to how large the last one was. Then how many zeroes lie among them in total, and then
/// how the zeroes are distributed between them, again from the end. Only in
/// <see cref="_Place"/> does any of it become a coefficient at a position.
/// <para/>
/// The adaptation is the point. A block's levels grow towards the DC end, so a code that was efficient
/// for a level of one is wasteful for a level of forty; <c>suffixLength</c> tracks the size reached so
/// far and widens the code as it goes (clause 9.2.2, steps 9 and 10). That is a state machine over the
/// block, so a single misread level does not corrupt one coefficient — it changes the width of every
/// code after it.
/// </remarks>
internal static class H264Residual {

  /// <summary>
  /// Reads one residual block into <paramref name="coeffLevel"/>, which is indexed by scan position.
  /// </summary>
  /// <param name="reader">The slice data.</param>
  /// <param name="coeffLevel">
  /// The block's scan positions, already zeroed by the caller; its length is <c>maxNumCoeff</c>.
  /// </param>
  /// <param name="nC">The neighbour-derived table selector of clause 9.2.1.</param>
  /// <param name="chromaDc">Whether this is a chroma DC block, whose <c>total_zeros</c> has its own table.</param>
  /// <returns><c>TotalCoeff</c>, which the blocks decoded after this one need for their own <c>nC</c>.</returns>
  internal static int ReadBlock(ref H264BitReader reader, scoped Span<int> coeffLevel, int nC, bool chromaDc) {
    var maxNumCoeff = coeffLevel.Length;
    var token = H264CavlcTables.ReadCoeffToken(ref reader, nC);
    var totalCoeff = H264CavlcTables.TotalCoeff(token);
    var trailingOnes = H264CavlcTables.TrailingOnes(token);

    if (totalCoeff > maxNumCoeff)
      throw new InvalidDataException(
        $"An H.264 residual block of at most {maxNumCoeff} coefficients decoded a coeff_token stating "
        + $"{totalCoeff}. H.264, clause 9.2.1 requires TotalCoeff not to exceed maxNumCoeff.");

    if (totalCoeff == 0)
      return 0;

    Span<int> levels = stackalloc int[16];
    _ReadLevels(ref reader, levels, totalCoeff, trailingOnes);

    Span<int> runs = stackalloc int[16];
    _ReadRuns(ref reader, runs, totalCoeff, maxNumCoeff, chromaDc);

    _Place(coeffLevel, levels, runs, totalCoeff);
    return totalCoeff;
  }

  /// <summary>The level of each non-zero coefficient, highest frequency first — clause 9.2.2.</summary>
  private static void _ReadLevels(ref H264BitReader reader, scoped Span<int> levels, int totalCoeff, int trailingOnes) {
    for (var i = 0; i < trailingOnes; ++i)
      levels[i] = reader.ReadBit() == 0 ? 1 : -1;

    // A block with many coefficients cannot have them all be ones, so its first coded level is
    // already worth a wider code — which is what starting suffixLength at 1 buys (clause 9.2.2).
    var suffixLength = totalCoeff > 10 && trailingOnes < 3 ? 1 : 0;

    for (var i = trailingOnes; i < totalCoeff; ++i) {
      var levelPrefix = reader.ReadLeadingZeroBits();

      var levelSuffixSize = levelPrefix == 14 && suffixLength == 0 ? 4
        : levelPrefix >= 15 ? levelPrefix - 3
        : suffixLength;

      var levelSuffix = levelSuffixSize > 0 ? reader.ReadBits(levelSuffixSize) : 0;

      var levelCode = (Math.Min(15, levelPrefix) << suffixLength) + levelSuffix;
      if (levelPrefix >= 15 && suffixLength == 0)
        levelCode += 15;

      if (levelPrefix >= 16)
        levelCode += (1 << (levelPrefix - 3)) - 4096;

      // The first coded level cannot be a one when fewer than three trailing ones were counted —
      // if it were, it would have been one of them. So the two smallest magnitudes are skipped.
      if (i == trailingOnes && trailingOnes < 3)
        levelCode += 2;

      levels[i] = (levelCode & 1) == 0 ? (levelCode + 2) >> 1 : (-levelCode - 1) >> 1;

      if (suffixLength == 0)
        suffixLength = 1;

      if (Math.Abs(levels[i]) > 3 << (suffixLength - 1) && suffixLength < 6)
        ++suffixLength;
    }
  }

  /// <summary>How many zeroes precede each non-zero coefficient — clause 9.2.3.</summary>
  private static void _ReadRuns(
    ref H264BitReader reader, scoped Span<int> runs, int totalCoeff, int maxNumCoeff, bool chromaDc) {
    // A block whose every position is non-zero has no zeroes to place and no total_zeros is coded.
    var zerosLeft = totalCoeff < maxNumCoeff
      ? chromaDc
        ? H264CavlcTables.ReadTotalZerosChromaDc(ref reader, totalCoeff)
        : H264CavlcTables.ReadTotalZeros4x4(ref reader, totalCoeff)
      : 0;

    if (zerosLeft > maxNumCoeff - totalCoeff)
      throw new InvalidDataException(
        $"An H.264 residual block states {zerosLeft} total_zeros among {totalCoeff} coefficients of a block of "
        + $"{maxNumCoeff}, which is more positions than the block has.");

    for (var i = 0; i < totalCoeff - 1; ++i) {
      var run = zerosLeft > 0 ? H264CavlcTables.ReadRunBefore(ref reader, zerosLeft) : 0;
      if (run > zerosLeft)
        throw new InvalidDataException(
          $"An H.264 residual block states a run_before of {run} with only {zerosLeft} zero(es) left to place.");

      runs[i] = run;
      zerosLeft -= run;
    }

    // Whatever is left goes in front of the last coefficient read, which is the lowest frequency one.
    runs[totalCoeff - 1] = zerosLeft;
  }

  /// <summary>Turns the levels and runs into coefficients at scan positions — clause 9.2.4.</summary>
  private static void _Place(Span<int> coeffLevel, ReadOnlySpan<int> levels, ReadOnlySpan<int> runs, int totalCoeff) {
    var coeffNum = -1;
    for (var i = totalCoeff - 1; i >= 0; --i) {
      coeffNum += runs[i] + 1;
      if (coeffNum >= coeffLevel.Length)
        throw new InvalidDataException(
          $"An H.264 residual block places a coefficient at scan position {coeffNum} of a block that has "
          + $"{coeffLevel.Length}. The levels and runs do not agree with the coefficient count.");

      coeffLevel[coeffNum] = levels[i];
    }
  }
}
