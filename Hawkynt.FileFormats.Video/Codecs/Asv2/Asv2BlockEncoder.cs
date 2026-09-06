using System;
using FileFormat.Codecs.Asv;

namespace FileFormat.Codecs.Asv2;

/// <summary>
/// Writes one of ASV2's eight-by-eight blocks, clause 4.5 — the exact inverse of
/// <see cref="Asv2BlockDecoder"/>, reading the same three tables from the other side.
/// </summary>
/// <remarks>
/// ASV2 states up front how many coefficient groups follow instead of ending on an End Of Block code,
/// so the block's shape is decided before a single pattern is written: the count is the serial number
/// of the last group holding anything, and every group up to it is written whether it holds a
/// coefficient or not. There is nothing to defer and nothing to hold back — an empty group in the
/// middle costs its pattern code and that is the whole of the choice.
/// <para/>
/// That count is also why ASV2 reaches all sixteen of the coefficient groups clause 3.3's diagram
/// names where ASV1 reaches only the first ten: nothing has to be written to get past a group.
/// <para/>
/// Group zero is coded from its own eight-value table rather than the sixteen-value one every later
/// group uses, because the position its first pattern bit would name is the block's own direct
/// current — carried by the separate eight-bit field and never a coefficient.
/// </remarks>
internal static class Asv2BlockEncoder {

  /// <summary>The value <see cref="Asv2VlcTables.Level"/> escapes with, as the encoder must ask for it.</summary>
  private const int _LEVEL_ESCAPE = short.MinValue;

  private static readonly AsvCodeTable _FirstPattern = new(Asv2VlcTables.FirstCoefficientPattern);
  private static readonly AsvCodeTable _Pattern = new(Asv2VlcTables.CodedCoefficientPattern);
  private static readonly AsvCodeTable _Level = new(Asv2VlcTables.Level);

  /// <summary>Writes one block from its quantised direct-current field and levels.</summary>
  /// <param name="directCurrent">The eight-bit field of clause 3.5.</param>
  /// <param name="levels">Sixty-four quantised levels in raster order; position zero is ignored.</param>
  internal static void Write(AsvBitWriter writer, int directCurrent, scoped ReadOnlySpan<int> levels) {
    var count = 0;
    for (var group = 15; group > 0; --group)
      if (_PatternOf(levels, group) != 0) {
        count = group;
        break;
      }

    writer.ReversedBits(count, 4);
    writer.ReversedBits(directCurrent, 8);

    for (var group = 0; group <= count; ++group) {
      var pattern = _PatternOf(levels, group);
      writer.Code(group == 0 ? _FirstPattern[pattern] : _Pattern[pattern]);

      for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
        if (((pattern >> (3 - withinGroup)) & 1) == 0)
          continue;

        _WriteLevel(writer, levels[Asv2VlcTables.ScanPosition[group * 4 + withinGroup]]);
      }
    }
  }

  /// <summary>
  /// Which of a coefficient group's four positions carry a level, most significant bit first — the
  /// order ASV2 reads them in, where ASV1 reads the same shape of pattern the other way round.
  /// </summary>
  private static int _PatternOf(scoped ReadOnlySpan<int> levels, int group) {
    var pattern = 0;
    for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
      var position = Asv2VlcTables.ScanPosition[group * 4 + withinGroup];

      // Position zero is the block's own direct current, carried by its own field and never coded here.
      if (position != 0 && levels[position] != 0)
        pattern |= 1 << (3 - withinGroup);
    }

    return pattern;
  }

  private static void _WriteLevel(AsvBitWriter writer, int level) {
    if (_Level.Holds(level)) {
      writer.Code(_Level[level]);
      return;
    }

    writer.Code(_Level[_LEVEL_ESCAPE]);
    writer.ReversedBits(level & 0xFF, 8);
  }
}
