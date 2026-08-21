using System;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Entropy decoding of one colour component of one slice into its scanned coefficient array.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5.3.2 for the syntax and 7.1.1 for the codebooks. The array holds every quantised
/// coefficient of every block of the component in the slice, ordered by frequency first and by block
/// second — see <see cref="ProResBlocks"/> — so a slice of eight macroblocks of luma is one array of
/// 32 blocks × 64 coefficients, coded as a single stream.
/// <para/>
/// <b>Every codebook here is adaptive, and every one adapts to the previous symbol of its own kind.</b>
/// There are three adaptations running at once — one for DC differences, one for runs and one for
/// levels — and each is reset at the start of each colour component of each slice, to the values
/// 7.1.1.3 and 7.1.1.4 state rather than to zero. Starting them at zero decodes the first few
/// symbols of every component with the wrong codebook, which does not fail: it produces a different
/// picture.
/// <para/>
/// <b>The DC differences carry their sign forward.</b> 7.1.1.3 makes the sign of a difference
/// relative to the sign of the one before it, so a run of decreasing DC values is coded with the
/// same short symbols as a run of increasing ones. The consequence is that the sign of
/// <c>previousDCDiff</c> is state, not just its magnitude, and losing it flips the polarity of
/// everything downstream of the first negative difference.
/// </remarks>
internal static class ProResCoefficients {

  /// <summary>The code the first DC coefficient of an array is written with, RDD 36:2022, 7.1.1.3.</summary>
  private static readonly ProResGolombCode _FirstDc = ProResGolombCode.ExpGolomb(5);

  /// <summary>
  /// Codebooks for <c>dc_coeff_difference</c>, indexed by the absolute value of the previous one.
  /// </summary>
  /// <remarks>RDD 36:2022, Table 9. The last entry serves every magnitude of 3 and above.</remarks>
  private static readonly ProResGolombCode[] _DcDifference = [
    ProResGolombCode.ExpGolomb(0),
    ProResGolombCode.ExpGolomb(1),
    new(1, 2, 3),
    ProResGolombCode.ExpGolomb(3),
  ];

  /// <summary>Codebooks for <c>run</c>, indexed by the previous run.</summary>
  /// <remarks>RDD 36:2022, Table 10. The last entry serves every run of 15 and above.</remarks>
  private static readonly ProResGolombCode[] _Run = [
    new(2, 0, 1),
    new(2, 0, 1),
    new(1, 0, 1),
    new(1, 0, 1),
    ProResGolombCode.ExpGolomb(0),
    new(1, 1, 2),
    new(1, 1, 2),
    new(1, 1, 2),
    new(1, 1, 2),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(2),
  ];

  /// <summary>Codebooks for <c>abs_level_minus_1</c>, indexed by the previous level symbol.</summary>
  /// <remarks>RDD 36:2022, Table 11. The last entry serves every symbol of 8 and above.</remarks>
  private static readonly ProResGolombCode[] _Level = [
    new(2, 0, 2),
    new(1, 0, 1),
    new(2, 0, 1),
    ProResGolombCode.ExpGolomb(0),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(1),
    ProResGolombCode.ExpGolomb(2),
  ];

  /// <summary>The magnitude the DC adaptation starts each array at, RDD 36:2022, 7.1.1.3.</summary>
  private const int _INITIAL_DC_DIFFERENCE = 3;

  /// <summary>The run the adaptation starts each array at, RDD 36:2022, 7.1.1.4.</summary>
  private const int _INITIAL_RUN = 4;

  /// <summary>
  /// The level symbol the adaptation starts each array at, RDD 36:2022, 7.1.1.4.
  /// </summary>
  /// <remarks>
  /// One, not two. The specification states the initial <i>level</i> as 2 and the symbol as
  /// <c>|2| − 1</c>, and it is the symbol that indexes Table 11.
  /// </remarks>
  private const int _INITIAL_LEVEL_SYMBOL = 1;

  /// <summary>
  /// Decodes one colour component's coded data into scanned quantised coefficients.
  /// </summary>
  /// <param name="data">The component's coded bytes, whose length is the <c>dataSize</c> the
  /// end-of-data test is made against.</param>
  /// <param name="blockCount">The number of 8×8 blocks of this component in the slice.</param>
  /// <returns><paramref name="blockCount"/> × 64 coefficients in scanned order.</returns>
  internal static int[] Decode(ReadOnlyMemory<byte> data, int blockCount) {
    var coefficients = new int[blockCount * 64];
    var bits = new ProResBitReader(data);

    _DecodeDcCoefficients(bits, coefficients, blockCount);
    _DecodeAcCoefficients(bits, coefficients, blockCount);

    return coefficients;
  }

  /// <summary>
  /// The DC coefficient of every block, which occupy the first <paramref name="blockCount"/> entries.
  /// </summary>
  /// <remarks>
  /// They come first because slice scanning puts all coefficients of frequency index 0 at the start
  /// of the array (7.2.1), and they are coded as differences because the DC of neighbouring blocks
  /// is what a picture has most of.
  /// </remarks>
  private static void _DecodeDcCoefficients(ProResBitReader bits, int[] coefficients, int blockCount) {
    var previous = _ToSigned(_FirstDc.Read(bits));
    coefficients[0] = previous;

    var previousDifference = _INITIAL_DC_DIFFERENCE;

    for (var n = 1; n < blockCount; ++n) {
      var magnitude = previousDifference < 0 ? -previousDifference : previousDifference;
      var codebook = _DcDifference[magnitude < _DcDifference.Length ? magnitude : _DcDifference.Length - 1];

      var difference = _ToSigned(codebook.Read(bits));
      if (previousDifference < 0)
        difference = -difference;

      previous += difference;
      coefficients[n] = previous;
      previousDifference = difference;
    }
  }

  /// <summary>The run-length coded AC coefficients, which fill the array from where the DCs stopped.</summary>
  private static void _DecodeAcCoefficients(ProResBitReader bits, int[] coefficients, int blockCount) {
    var n = blockCount;
    var previousRun = _INITIAL_RUN;
    var previousLevelSymbol = _INITIAL_LEVEL_SYMBOL;

    while (!bits.EndOfData()) {
      var run = _Run[previousRun < _Run.Length ? previousRun : _Run.Length - 1].Read(bits);
      previousRun = run;

      // The run is the count of zeroes before the coefficient, and the coefficient itself takes one
      // more place, so a run that reaches the end of the array is a damaged component rather than a
      // long stretch of nothing — a final run of zeroes is never coded at all (7.1.1.4).
      n += run;
      if (n >= coefficients.Length)
        throw new InvalidDataException(
          $"A ProRes colour component coded a run reaching coefficient {n} of an array of {coefficients.Length}. Its coded data is damaged.");

      for (var m = n - run; m < n; ++m)
        coefficients[m] = 0;

      var levelSymbol = _Level[previousLevelSymbol < _Level.Length ? previousLevelSymbol : _Level.Length - 1].Read(bits);
      previousLevelSymbol = levelSymbol;

      var sign = bits.Bit();
      coefficients[n] = (levelSymbol + 1) * (1 - 2 * sign);
      ++n;
    }

    // Everything after the last coded level is zero. The array was allocated zeroed, so this is only
    // stated to say that the implicit final run is deliberate and not an omission.
  }

  /// <summary>
  /// The inverse of the signed integer-to-symbol mapping, RDD 36:2022, 7.1.1.2.
  /// </summary>
  /// <remarks>
  /// Even symbols are non-negative and odd ones negative, which puts small magnitudes of either sign
  /// close together at the front of the alphabet where the codes are short.
  /// </remarks>
  private static int _ToSigned(int symbol) => (symbol & 1) == 0 ? symbol >> 1 : -((symbol + 1) >> 1);
}
