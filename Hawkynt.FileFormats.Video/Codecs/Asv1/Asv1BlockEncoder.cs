using System;
using FileFormat.Codecs.Asv;

namespace FileFormat.Codecs.Asv1;

/// <summary>
/// Writes one of ASV1's eight-by-eight blocks, clause 4.4 — the exact inverse of
/// <see cref="Asv1BlockDecoder"/>, reading the same two tables from the other side.
/// </summary>
/// <remarks>
/// <b>Only forty of a block's sixty-four positions can be coded at all.</b> Clause 3.3 states that
/// coefficient groups ten to fifteen "cannot be coded (they must be 0)", and the End-Of-Block code
/// that terminates a block is what makes that so: there is no way to reach a later group without
/// having written every earlier one. So the twenty-four highest-frequency positions are dropped
/// before a pattern is worked out rather than written and then refused — which is also the one place
/// this encoder is coarser than ASV2's, whose explicit group count reaches all sixteen.
/// <para/>
/// <b>An empty group before a coded one costs two bits; an empty group after the last coded one
/// costs nothing.</b> A group with no coefficients is stated by pattern zero, and the block ends on
/// End Of Block whenever it likes, so trailing empty groups are simply never written. Holding them
/// back until a coded group turns up is what makes a flat block eight bits and five rather than
/// eight bits and twenty-five.
/// </remarks>
internal static class Asv1BlockEncoder {

  /// <summary>The last coefficient group ASV1's End-Of-Block-terminated coding can reach.</summary>
  private const int _LAST_GROUP = 9;

  /// <summary>The value <see cref="Asv1VlcTables.Level"/> escapes with, as the encoder must ask for it.</summary>
  private const int _LEVEL_ESCAPE = short.MinValue;

  private static readonly AsvCodeTable _Pattern = new(Asv1VlcTables.CodedCoefficientPattern);
  private static readonly AsvCodeTable _Level = new(Asv1VlcTables.Level);

  /// <summary>Writes one block from its quantised direct-current field and levels.</summary>
  /// <param name="directCurrent">The eight-bit field of clause 3.5.</param>
  /// <param name="levels">Sixty-four quantised levels in raster order; position zero is ignored.</param>
  internal static void Write(AsvBitWriter writer, int directCurrent, scoped ReadOnlySpan<int> levels) {
    writer.Bits(directCurrent, 8);

    var deferredEmptyGroups = 0;
    for (var group = 0; group <= _LAST_GROUP; ++group) {
      var pattern = 0;
      for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
        var position = Asv1VlcTables.ScanPosition[group * 4 + withinGroup];

        // Position zero is the block's own direct current, already written above and never coded here.
        if (position != 0 && levels[position] != 0)
          pattern |= 1 << withinGroup;
      }

      if (pattern == 0) {
        ++deferredEmptyGroups;
        continue;
      }

      for (; deferredEmptyGroups > 0; --deferredEmptyGroups)
        writer.Code(_Pattern[0]);

      writer.Code(_Pattern[pattern]);

      for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
        if (((pattern >> withinGroup) & 1) == 0)
          continue;

        _WriteLevel(writer, levels[Asv1VlcTables.ScanPosition[group * 4 + withinGroup]]);
      }
    }

    writer.Code(_Pattern[Asv1VlcTables.EndOfBlock]);
  }

  /// <summary>Which raster positions ASV1's ten reachable coefficient groups cover.</summary>
  /// <remarks>
  /// Everything outside this is forced to nought before a block is written, because the coding cannot
  /// state it and a decoder reconstructs it as nought whatever the encoder wanted.
  /// </remarks>
  internal static bool[] CodeablePositions { get; } = _BuildCodeablePositions();

  private static bool[] _BuildCodeablePositions() {
    var codeable = new bool[64];
    for (var i = 0; i < (_LAST_GROUP + 1) * 4; ++i)
      codeable[Asv1VlcTables.ScanPosition[i]] = true;

    codeable[0] = false;
    return codeable;
  }

  private static void _WriteLevel(AsvBitWriter writer, int level) {
    if (_Level.Holds(level)) {
      writer.Code(_Level[level]);
      return;
    }

    writer.Code(_Level[_LEVEL_ESCAPE]);
    writer.Bits(level & 0xFF, 8);
  }
}
