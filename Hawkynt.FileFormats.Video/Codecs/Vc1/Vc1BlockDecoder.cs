using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// The state escape mode 3 carries across a whole picture (7.1.4.10, 7.1.4.11).
/// </summary>
/// <remarks>
/// A class rather than a value because it genuinely is picture-wide state. The first block of a
/// picture that escapes into mode 3 states how wide the fixed-length run and level fields are, and
/// every mode 3 escape after it in that picture uses the same two widths without restating them — so a
/// decoder that reset this per block would read the second escape at the wrong width and everything
/// after it out of step.
/// </remarks>
internal sealed class Vc1EscapeState {

  internal bool First { get; set; } = true;

  internal int LevelCodeSize { get; set; }

  internal int RunCodeSize { get; set; }

  internal void Reset() {
    this.First = true;
    this.LevelCodeSize = 0;
    this.RunCodeSize = 0;
  }
}

/// <summary>
/// Decodes the block layer of an intra-coded VC-1 block: the DC differential, then the run-level
/// coded AC coefficients (8.1.3.1, 8.1.3.4, 8.1.3.5).
/// </summary>
internal static class Vc1BlockDecoder {

  private static readonly Vc1VlcTable _LowMotionLumaDc = new("Low-motion Luma DC Differential", Vc1Tables.LowMotionLumaDc);
  private static readonly Vc1VlcTable _LowMotionChromaDc = new("Low-motion Colour-difference DC Differential", Vc1Tables.LowMotionChromaDc);
  private static readonly Vc1VlcTable _HighMotionLumaDc = new("High-motion Luma DC Differential", Vc1Tables.HighMotionLumaDc);
  private static readonly Vc1VlcTable _HighMotionChromaDc = new("High-motion Colour-difference DC Differential", Vc1Tables.HighMotionChromaDc);

  /// <summary>The index the DC tables use to say the differential did not fit them.</summary>
  /// <remarks>
  /// One past the hundred and nineteen values each table names, which is how every one of the four is
  /// built; the escape row is the last of the printed table and carries no differential of its own.
  /// </remarks>
  private const int _DC_ESCAPE_INDEX = 119;

  /// <summary>Picks the DC differential table for a block (8.1.1.2).</summary>
  internal static Vc1VlcTable DcTable(bool highMotion, bool luma) => highMotion
    ? luma ? _HighMotionLumaDc : _HighMotionChromaDc
    : luma ? _LowMotionLumaDc : _LowMotionChromaDc;

  /// <summary>
  /// Reads the DC differential of an intra block (Figure 37).
  /// </summary>
  /// <remarks>
  /// The coarser the quantiser the fewer bits the escape needs, because the differential it has to
  /// carry is smaller — which is why the escape width and the extra bits below it both depend on the
  /// quantiser rather than being fixed.
  /// </remarks>
  internal static int ReadDcDifferential(ref Vc1BitReader reader, Vc1VlcTable table, int quantiser) {
    var differential = table.Read(ref reader);
    if (differential == 0)
      return 0;

    if (differential == _DC_ESCAPE_INDEX)
      differential = reader.ReadBits(quantiser switch { 1 => 10, 2 => 9, _ => 8 });
    else
      differential = quantiser switch {
        1 => (differential * 4) + reader.ReadBits(2) - 3,
        2 => (differential * 2) + reader.ReadBit() - 1,
        _ => differential,
      };

    return reader.ReadBit() == 1 ? -differential : differential;
  }

  /// <summary>
  /// Fills a block's sixty-four coefficients from the run-level coded AC symbols (Figures 41 and 42).
  /// </summary>
  /// <param name="coefficients">The block, in scan order; entry nought is left for the DC.</param>
  internal static void ReadAcCoefficients(
    ref Vc1BitReader reader, Vc1AcCodingSet set, Vc1EscapeState escape, int pictureQuantiser, bool conservativeEscape,
    scoped Span<int> coefficients) {
    var position = 1;

    while (true) {
      var (run, level, last) = _ReadSymbol(ref reader, set, escape, pictureQuantiser, conservativeEscape);

      position += run;
      if ((uint)position >= 64u)
        throw new InvalidDataException(
          $"A run of {run} puts a coefficient at position {position} of a block that holds sixty-four.");

      coefficients[position] = level;
      ++position;

      if (last)
        return;

      if (position >= 64)
        throw new InvalidDataException("A block ran past its last coefficient without the last flag being set.");
    }
  }

  private static (int Run, int Level, bool Last) _ReadSymbol(
    ref Vc1BitReader reader, Vc1AcCodingSet set, Vc1EscapeState escape, int pictureQuantiser, bool conservativeEscape) {
    var index = set.Codes.Read(ref reader);

    if (index != set.EscapeIndex) {
      var run = set.Runs[index];
      var level = set.Levels[index];
      var last = index >= set.StartOfLast;
      return (run, reader.ReadBit() == 1 ? -level : level, last);
    }

    // Table 58: one bit for the first mode, two for the second, two for the third.
    var mode = reader.ReadBit() == 1 ? 1 : reader.ReadBit() == 1 ? 2 : 3;

    if (mode == 1) {
      // The symbol is in the table but its level is larger than the table's, by the amount the delta
      // table attaches to its run.
      var second = set.Codes.Read(ref reader);
      if (second == set.EscapeIndex)
        throw new InvalidDataException($"{set.Name}: an escaped symbol escaped again, which the standard does not define.");

      var run = set.Runs[second];
      var last = second >= set.StartOfLast;
      var level = set.Levels[second] + (last ? set.LastDeltaLevel[run] : set.NotLastDeltaLevel[run]);
      return (run, reader.ReadBit() == 1 ? -level : level, last);
    }

    if (mode == 2) {
      // The mirror of mode 1: the level is in the table and the run is larger, by the amount the delta
      // table attaches to its level, plus one.
      var second = set.Codes.Read(ref reader);
      if (second == set.EscapeIndex)
        throw new InvalidDataException($"{set.Name}: an escaped symbol escaped again, which the standard does not define.");

      var level = set.Levels[second];
      var last = second >= set.StartOfLast;
      var deltas = last ? set.LastDeltaRun : set.NotLastDeltaRun;
      if ((uint)level >= (uint)deltas.Length)
        throw new InvalidDataException($"{set.Name}: a level of {level} is past the end of its delta run table.");

      var run = set.Runs[second] + deltas[level] + 1;
      return (run, reader.ReadBit() == 1 ? -level : level, last);
    }

    // Mode 3: the run and the level as plain fixed-length fields, at widths the first escape of the
    // picture states and every later one reuses.
    var lastFlag = reader.ReadBit() == 1;
    if (escape.First) {
      escape.First = false;
      escape.LevelCodeSize = _ReadLevelCodeSize(ref reader, conservativeEscape);
      escape.RunCodeSize = 3 + reader.ReadBits(2);
    }

    var escapedRun = reader.ReadBits(escape.RunCodeSize);
    var sign = reader.ReadBit();
    var escapedLevel = reader.ReadBits(escape.LevelCodeSize);
    return (escapedRun, sign == 1 ? -escapedLevel : escapedLevel, lastFlag);
  }

  /// <summary>
  /// Reads how many bits a mode 3 level occupies, from whichever of Tables 59 and 60 applies.
  /// </summary>
  /// <remarks>
  /// Two tables for the same field, chosen by how finely the picture is quantised. A finely quantised
  /// picture has larger levels to carry, so it uses the table that can reach eleven bits; a coarsely
  /// quantised one uses the shorter table, whose codes are one bit long where the other's are three.
  /// </remarks>
  private static int _ReadLevelCodeSize(ref Vc1BitReader reader, bool conservative) {
    if (!conservative) {
      // Table 60: a run of zeroes counts, 1b through 000001b, with 000000b sharing the last length.
      for (var length = 0; length < 5; ++length)
        if (reader.ReadBit() == 1)
          return length + 2;

      return reader.ReadBit() == 1 ? 7 : 8;
    }

    // Table 59: three bits, where 000b means the size is stated by two more.
    var code = reader.ReadBits(3);
    return code != 0 ? code : 8 + reader.ReadBits(2);
  }

  /// <summary>
  /// Scatters a block's coefficients from scan order into the 8x8 array (Figure 43, Table 73).
  /// </summary>
  internal static void InverseScan(ReadOnlySpan<int> ordered, ReadOnlySpan<byte> scan, Span<int> block) {
    block.Clear();
    for (var i = 0; i < 64; ++i)
      block[scan[i]] = ordered[i];
  }

  /// <summary>The scan a block takes, from whether it carried AC prediction and where from (Table 73).</summary>
  internal static ReadOnlySpan<byte> ScanFor(bool acPrediction, bool fromTop) => !acPrediction
    ? Vc1Tables.NormalScan
    : fromTop
      ? Vc1Tables.HorizontalScan
      : Vc1Tables.VerticalScan;
}
