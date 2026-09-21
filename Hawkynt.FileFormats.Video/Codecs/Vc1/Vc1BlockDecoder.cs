using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>The state escape mode 3 carries across a whole picture (7.1.4.10, 7.1.4.11).</summary>
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

/// <summary>Decodes intra and inter run-level coefficient syntax.</summary>
internal static class Vc1BlockDecoder {

  private static readonly Vc1VlcTable _LowMotionLumaDc = new("Low-motion Luma DC Differential", Vc1Tables.LowMotionLumaDc);
  private static readonly Vc1VlcTable _LowMotionChromaDc = new("Low-motion Colour-difference DC Differential", Vc1Tables.LowMotionChromaDc);
  private static readonly Vc1VlcTable _HighMotionLumaDc = new("High-motion Luma DC Differential", Vc1Tables.HighMotionLumaDc);
  private static readonly Vc1VlcTable _HighMotionChromaDc = new("High-motion Colour-difference DC Differential", Vc1Tables.HighMotionChromaDc);

  private const int _DC_ESCAPE_INDEX = 119;

  internal static Vc1VlcTable DcTable(bool highMotion, bool luma) => highMotion
    ? luma ? _HighMotionLumaDc : _HighMotionChromaDc
    : luma ? _LowMotionLumaDc : _LowMotionChromaDc;

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

  /// <summary>Reads the AC part of an intra block; scan position zero belongs to its separately coded DC.</summary>
  internal static void ReadAcCoefficients(
    ref Vc1BitReader reader, Vc1AcCodingSet set, Vc1EscapeState escape, int pictureQuantiser, bool conservativeEscape,
    scoped Span<int> coefficients)
    => _ReadCoefficients(ref reader, set, escape, pictureQuantiser, conservativeEscape, coefficients, 1);

  /// <summary>Reads an inter block, where run-level coding includes coefficient position zero.</summary>
  internal static void ReadInterCoefficients(
    ref Vc1BitReader reader, Vc1AcCodingSet set, Vc1EscapeState escape, int pictureQuantiser, bool conservativeEscape,
    scoped Span<int> coefficients)
    => _ReadCoefficients(ref reader, set, escape, pictureQuantiser, conservativeEscape, coefficients, 0);

  private static void _ReadCoefficients(
    ref Vc1BitReader reader,
    Vc1AcCodingSet set,
    Vc1EscapeState escape,
    int pictureQuantiser,
    bool conservativeEscape,
    scoped Span<int> coefficients,
    int position) {
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

    var mode = reader.ReadBit() == 1 ? 1 : reader.ReadBit() == 1 ? 2 : 3;

    if (mode == 1) {
      var second = set.Codes.Read(ref reader);
      if (second == set.EscapeIndex)
        throw new InvalidDataException($"{set.Name}: an escaped symbol escaped again, which the standard does not define.");

      var run = set.Runs[second];
      var last = second >= set.StartOfLast;
      var level = set.Levels[second] + (last ? set.LastDeltaLevel[run] : set.NotLastDeltaLevel[run]);
      return (run, reader.ReadBit() == 1 ? -level : level, last);
    }

    if (mode == 2) {
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

  private static int _ReadLevelCodeSize(ref Vc1BitReader reader, bool conservative) {
    if (!conservative) {
      for (var length = 0; length < 5; ++length)
        if (reader.ReadBit() == 1)
          return length + 2;

      return reader.ReadBit() == 1 ? 7 : 8;
    }

    var code = reader.ReadBits(3);
    return code != 0 ? code : 8 + reader.ReadBits(2);
  }

  internal static void InverseScan(ReadOnlySpan<int> ordered, ReadOnlySpan<byte> scan, Span<int> block) {
    block.Clear();
    for (var i = 0; i < 64; ++i)
      block[scan[i]] = ordered[i];
  }

  internal static ReadOnlySpan<byte> ScanFor(bool acPrediction, bool fromTop) => !acPrediction
    ? Vc1Tables.NormalScan
    : fromTop
      ? Vc1Tables.HorizontalScan
      : Vc1Tables.VerticalScan;
}
