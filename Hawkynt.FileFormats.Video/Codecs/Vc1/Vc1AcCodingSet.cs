using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// One of the eight AC coding sets of SMPTE 421M 8.1.3.4: a code table and the tables that turn what
/// it decodes into a run, a level and a last flag.
/// </summary>
/// <remarks>
/// The standard calls the whole bundle a coding set because the pieces are useless apart. One code
/// table maps bits to an index; the run and level tables turn that index into a coefficient; two
/// constants say where in the index space the last-coefficient pairs begin and where the escape
/// lives; and four delta tables extend the set beyond what the index space covers, which is what the
/// first two escape modes are for.
/// <para/>
/// Four sets are nominally intra and four inter, and an I picture uses both: the luma blocks take an
/// intra set and the two colour-difference blocks an inter one (8.1.3.4). The names are the standard's
/// and describe the material each was trained on rather than the block type they are used for.
/// </remarks>
internal sealed class Vc1AcCodingSet {

  internal Vc1AcCodingSet(
    string name,
    ReadOnlySpan<int> codes,
    ReadOnlySpan<byte> runs,
    ReadOnlySpan<byte> levels,
    ReadOnlySpan<byte> notLastDeltaLevel,
    ReadOnlySpan<byte> lastDeltaLevel,
    ReadOnlySpan<byte> notLastDeltaRun,
    ReadOnlySpan<byte> lastDeltaRun,
    int startOfLast,
    int escapeIndex) {
    this.Name = name;
    this.Codes = new(name, codes);
    this.Runs = runs.ToArray();
    this.Levels = levels.ToArray();
    this.NotLastDeltaLevel = notLastDeltaLevel.ToArray();
    this.LastDeltaLevel = lastDeltaLevel.ToArray();
    this.NotLastDeltaRun = notLastDeltaRun.ToArray();
    this.LastDeltaRun = lastDeltaRun.ToArray();
    this.StartOfLast = startOfLast;
    this.EscapeIndex = escapeIndex;

    // The code table indexes the run and level tables directly and its escape is the one index past
    // their end, so the three sizes are one fact stated three times. A mismatch means a table lost a
    // row, which is exactly the failure that would otherwise show up as a picture that is subtly wrong.
    if (this.Runs.Length != escapeIndex || this.Levels.Length != escapeIndex)
      throw new ArgumentException(
        $"{name}: the code table escapes at index {escapeIndex} but the run and level tables hold "
        + $"{this.Runs.Length} and {this.Levels.Length} entries.", nameof(runs));

    if (this.Codes.Count != escapeIndex + 1)
      throw new ArgumentException(
        $"{name}: {this.Codes.Count} codewords for {escapeIndex} run/level pairs and an escape.", nameof(codes));
  }

  internal string Name { get; }

  internal Vc1VlcTable Codes { get; }

  internal byte[] Runs { get; }

  internal byte[] Levels { get; }

  /// <summary>Added to the level of an escaped not-last symbol, indexed by its run (escape mode 1).</summary>
  internal byte[] NotLastDeltaLevel { get; }

  /// <summary>Added to the level of an escaped last symbol, indexed by its run (escape mode 1).</summary>
  internal byte[] LastDeltaLevel { get; }

  /// <summary>Added to the run of an escaped not-last symbol, indexed by its level (escape mode 2).</summary>
  internal byte[] NotLastDeltaRun { get; }

  /// <summary>Added to the run of an escaped last symbol, indexed by its level (escape mode 2).</summary>
  internal byte[] LastDeltaRun { get; }

  /// <summary>The first index whose run and level pair is the last one in its block.</summary>
  internal int StartOfLast { get; }

  /// <summary>The index that says the symbol did not fit the table and is coded some other way.</summary>
  internal int EscapeIndex { get; }

  // ------------------------------------------------------------------------------------------
  // The eight sets
  // ------------------------------------------------------------------------------------------

  internal static readonly Vc1AcCodingSet HighMotionIntra = new(
    "High Motion Intra", Vc1Tables.HighMotionIntraCodes, Vc1Tables.HighMotionIntraRuns, Vc1Tables.HighMotionIntraLevels,
    Vc1Tables.HighMotionIntraNotLastDeltaLevel, Vc1Tables.HighMotionIntraLastDeltaLevel,
    Vc1Tables.HighMotionIntraNotLastDeltaRun, Vc1Tables.HighMotionIntraLastDeltaRun,
    Vc1Tables.HighMotionIntraStartOfLast, Vc1Tables.HighMotionIntraEscapeIndex);

  internal static readonly Vc1AcCodingSet HighMotionInter = new(
    "High Motion Inter", Vc1Tables.HighMotionInterCodes, Vc1Tables.HighMotionInterRuns, Vc1Tables.HighMotionInterLevels,
    Vc1Tables.HighMotionInterNotLastDeltaLevel, Vc1Tables.HighMotionInterLastDeltaLevel,
    Vc1Tables.HighMotionInterNotLastDeltaRun, Vc1Tables.HighMotionInterLastDeltaRun,
    Vc1Tables.HighMotionInterStartOfLast, Vc1Tables.HighMotionInterEscapeIndex);

  internal static readonly Vc1AcCodingSet LowMotionIntra = new(
    "Low Motion Intra", Vc1Tables.LowMotionIntraCodes, Vc1Tables.LowMotionIntraRuns, Vc1Tables.LowMotionIntraLevels,
    Vc1Tables.LowMotionIntraNotLastDeltaLevel, Vc1Tables.LowMotionIntraLastDeltaLevel,
    Vc1Tables.LowMotionIntraNotLastDeltaRun, Vc1Tables.LowMotionIntraLastDeltaRun,
    Vc1Tables.LowMotionIntraStartOfLast, Vc1Tables.LowMotionIntraEscapeIndex);

  internal static readonly Vc1AcCodingSet LowMotionInter = new(
    "Low Motion Inter", Vc1Tables.LowMotionInterCodes, Vc1Tables.LowMotionInterRuns, Vc1Tables.LowMotionInterLevels,
    Vc1Tables.LowMotionInterNotLastDeltaLevel, Vc1Tables.LowMotionInterLastDeltaLevel,
    Vc1Tables.LowMotionInterNotLastDeltaRun, Vc1Tables.LowMotionInterLastDeltaRun,
    Vc1Tables.LowMotionInterStartOfLast, Vc1Tables.LowMotionInterEscapeIndex);

  internal static readonly Vc1AcCodingSet MidRateIntra = new(
    "Mid Rate Intra", Vc1Tables.MidRateIntraCodes, Vc1Tables.MidRateIntraRuns, Vc1Tables.MidRateIntraLevels,
    Vc1Tables.MidRateIntraNotLastDeltaLevel, Vc1Tables.MidRateIntraLastDeltaLevel,
    Vc1Tables.MidRateIntraNotLastDeltaRun, Vc1Tables.MidRateIntraLastDeltaRun,
    Vc1Tables.MidRateIntraStartOfLast, Vc1Tables.MidRateIntraEscapeIndex);

  internal static readonly Vc1AcCodingSet MidRateInter = new(
    "Mid Rate Inter", Vc1Tables.MidRateInterCodes, Vc1Tables.MidRateInterRuns, Vc1Tables.MidRateInterLevels,
    Vc1Tables.MidRateInterNotLastDeltaLevel, Vc1Tables.MidRateInterLastDeltaLevel,
    Vc1Tables.MidRateInterNotLastDeltaRun, Vc1Tables.MidRateInterLastDeltaRun,
    Vc1Tables.MidRateInterStartOfLast, Vc1Tables.MidRateInterEscapeIndex);

  internal static readonly Vc1AcCodingSet HighRateIntra = new(
    "High Rate Intra", Vc1Tables.HighRateIntraCodes, Vc1Tables.HighRateIntraRuns, Vc1Tables.HighRateIntraLevels,
    Vc1Tables.HighRateIntraNotLastDeltaLevel, Vc1Tables.HighRateIntraLastDeltaLevel,
    Vc1Tables.HighRateIntraNotLastDeltaRun, Vc1Tables.HighRateIntraLastDeltaRun,
    Vc1Tables.HighRateIntraStartOfLast, Vc1Tables.HighRateIntraEscapeIndex);

  internal static readonly Vc1AcCodingSet HighRateInter = new(
    "High Rate Inter", Vc1Tables.HighRateInterCodes, Vc1Tables.HighRateInterRuns, Vc1Tables.HighRateInterLevels,
    Vc1Tables.HighRateInterNotLastDeltaLevel, Vc1Tables.HighRateInterLastDeltaLevel,
    Vc1Tables.HighRateInterNotLastDeltaRun, Vc1Tables.HighRateInterLastDeltaRun,
    Vc1Tables.HighRateInterStartOfLast, Vc1Tables.HighRateInterEscapeIndex);

  /// <summary>
  /// The set a block takes, from the index in the picture header and the picture's quantiser
  /// (Tables 71 and 72).
  /// </summary>
  /// <remarks>
  /// Index nought means two different things depending on how coarsely the picture is quantised, which
  /// is the only place in the format where a table index is read against something other than itself.
  /// </remarks>
  internal static Vc1AcCodingSet For(int index, bool luma, int pictureQuantiserIndex) => index switch {
    0 => pictureQuantiserIndex <= 8
      ? luma ? HighRateIntra : HighRateInter
      : luma ? LowMotionIntra : LowMotionInter,
    1 => luma ? HighMotionIntra : HighMotionInter,
    _ => luma ? MidRateIntra : MidRateInter,
  };
}
