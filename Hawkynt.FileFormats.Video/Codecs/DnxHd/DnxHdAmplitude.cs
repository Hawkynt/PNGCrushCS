namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// What one amplitude codeword of SMPTE ST 2019-1:2016, Annex E says, packed and unpacked.
/// </summary>
/// <remarks>
/// Annex E prints four columns beside each amplitude codeword — the amplitude, the run flag, the
/// index flag, and whether the codeword is the end-of-block one. All four fit in ten bits, so
/// <see cref="DnxHdVlcTables.AmplitudeSymbols"/> holds them packed and this type is the single place
/// that translates between that representation and the individual fields.
/// <para/>
/// The two flags decide what follows the codeword in the bitstream: Annex A.2 puts the sign bit
/// immediately after the codeword, then the amplitude index <c>P</c> if the index flag is set, then
/// the zero-run codeword if the run flag is set.
/// </remarks>
internal static class DnxHdAmplitude {

  private const int _AMPLITUDE_MASK = 0x7F;
  private const int _RUN_FLAG = 1 << 7;
  private const int _INDEX_FLAG = 1 << 8;
  private const int _END_OF_BLOCK = 1 << 9;

  /// <summary>The packed symbol Annex E uses for the end of a block.</summary>
  internal const int EndOfBlockSymbol = _END_OF_BLOCK;

  /// <summary>The base amplitude, 1 to 64, before any index offset is added.</summary>
  internal static int Value(int symbol) => symbol & _AMPLITUDE_MASK;

  /// <summary>Whether a run of zero-valued coefficients precedes this one.</summary>
  internal static bool HasRun(int symbol) => (symbol & _RUN_FLAG) != 0;

  /// <summary>Whether an amplitude index follows the sign bit, putting the amplitude past 64.</summary>
  internal static bool HasIndex(int symbol) => (symbol & _INDEX_FLAG) != 0;

  /// <summary>Whether this codeword ends the block.</summary>
  internal static bool IsEndOfBlock(int symbol) => (symbol & _END_OF_BLOCK) != 0;

  /// <summary>Packs one non-EOB amplitude symbol exactly as <see cref="DnxHdVlcTables"/> stores it.</summary>
  internal static int Symbol(int amplitude, bool run, bool index)
    => amplitude | (run ? _RUN_FLAG : 0) | (index ? _INDEX_FLAG : 0);
}
