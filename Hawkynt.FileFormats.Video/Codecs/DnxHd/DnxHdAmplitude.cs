namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// What one amplitude codeword of SMPTE ST 2019-1:2016, Annex E says, unpacked.
/// </summary>
/// <remarks>
/// Annex E prints four columns beside each amplitude codeword — the amplitude, the run flag, the
/// index flag, and whether the codeword is the end-of-block one. All four fit in ten bits, so
/// <see cref="DnxHdVlcTables.AmplitudeSymbols"/> holds them packed and this takes them apart again.
/// <para/>
/// The two flags decide what follows the codeword in the bitstream, which is why they have to be
/// read before anything else can be: Annex A.2 puts the sign bit immediately after the codeword,
/// then the amplitude index <c>P</c> if the index flag is set, then the zero-run codeword if the run
/// flag is set. Reading them in any other order decodes a different picture.
/// </remarks>
internal static class DnxHdAmplitude {

  private const int _AMPLITUDE_MASK = 0x7F;
  private const int _RUN_FLAG = 1 << 7;
  private const int _INDEX_FLAG = 1 << 8;
  private const int _END_OF_BLOCK = 1 << 9;

  /// <summary>The base amplitude, 1 to 64, before any index offset is added.</summary>
  internal static int Value(int symbol) => symbol & _AMPLITUDE_MASK;

  /// <summary>Whether a run of zero-valued coefficients precedes this one.</summary>
  internal static bool HasRun(int symbol) => (symbol & _RUN_FLAG) != 0;

  /// <summary>Whether an amplitude index follows the sign bit, putting the amplitude past 64.</summary>
  internal static bool HasIndex(int symbol) => (symbol & _INDEX_FLAG) != 0;

  /// <summary>Whether this codeword ends the block.</summary>
  internal static bool IsEndOfBlock(int symbol) => (symbol & _END_OF_BLOCK) != 0;
}
