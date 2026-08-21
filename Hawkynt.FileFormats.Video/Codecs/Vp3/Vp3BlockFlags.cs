using System.IO;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Reads which blocks of an inter frame carry a residual, from the run-length coded bit strings of
/// Sections 7.2 and 7.3.
/// </summary>
/// <remarks>
/// The flags are stored three strings deep, and the reason is that most of a frame is usually one
/// answer or the other. The first string says, for every super block in the frame, whether it is
/// mixed. The second says, for each super block that is not mixed, which way it goes — all sixteen
/// blocks coded or none of them. Only the mixed super blocks spend a bit per block, in the third
/// string. A still passage costs one run of zeroes and one run of zeroes, whatever the frame size.
/// <para/>
/// The first two strings use the long-run coding, whose runs reach 4129; the third uses the short-run
/// coding, which stops at 30, because a mixed super block has at most sixteen blocks in it and the
/// run cannot cross into the next string. Both codings read a bit for the first run and then toggle,
/// which is why a run of the same value twice over cannot be written and does not need to be.
/// <para/>
/// Flags are read for every block in the coded frame, including blocks that lie entirely outside the
/// picture region a container crops to. Those blocks are reconstructed like any other, because a
/// later frame may predict from them.
/// </remarks>
internal static class Vp3BlockFlags {

  /// <summary>Marks every block coded, which is what an intra frame does without reading anything.</summary>
  internal static void All(bool[] coded, int count) {
    for (var i = 0; i < count; ++i)
      coded[i] = true;
  }

  /// <param name="partial">Scratch, one entry per super block.</param>
  /// <param name="whole">Scratch, one entry per super block.</param>
  /// <param name="inside">Scratch, one entry per block.</param>
  internal static void Read(
    Vp3BitReader reader, Vp3Geometry geometry, bool[] coded,
    bool[] partial, bool[] whole, bool[] inside) {
    var superBlocks = geometry.SuperBlockCount;
    var blocks = geometry.BlockCount;

    // Which super blocks are partly coded.
    _LongRuns(reader, partial, superBlocks);

    var plainCount = 0;
    for (var i = 0; i < superBlocks; ++i)
      if (!partial[i])
        ++plainCount;

    // Which of the rest are coded whole, in the order those super blocks come.
    _LongRuns(reader, whole, plainCount);

    // The blocks of the partly coded super blocks, one bit each.
    var insideCount = 0;
    for (var i = 0; i < blocks; ++i)
      if (partial[geometry.BlockSuperBlock[i]])
        ++insideCount;

    _ShortRuns(reader, inside, insideCount);

    // Fold the "coded whole" answers back onto the super blocks they belong to — they were stated
    // only for the super blocks that are not partly coded — and then expand to one flag per block.
    for (int i = superBlocks - 1, plainAt = plainCount; i >= 0; --i)
      whole[i] = !partial[i] && whole[--plainAt];

    for (int i = blocks - 1, insideAt = insideCount; i >= 0; --i) {
      var superBlock = geometry.BlockSuperBlock[i];
      coded[i] = partial[superBlock] ? inside[--insideAt] : whole[superBlock];
    }
  }

  /// <summary>
  /// Reads <paramref name="count"/> bits coded as long runs, Section 7.2.1.
  /// </summary>
  private static void _LongRuns(Vp3BitReader reader, bool[] bits, int count) {
    if (count == 0)
      return;

    var at = 0;
    var value = reader.ReadBit() != 0;
    while (true) {
      var code = Vp3Tables.LongRunLengths.Read(reader);
      var length = Vp3Tables.LongRunStarts[code] + reader.ReadBits(Vp3Tables.LongRunExtraBits[code]);

      if (at + length > count)
        throw new InvalidDataException(
          $"A VP3 run of {length} {(value ? "coded" : "uncoded")} flags overruns the {count} the frame has room "
          + $"for, {at} of them already read.");

      for (var i = 0; i < length; ++i)
        bits[at++] = value;

      if (at == count)
        return;

      // Theora reads a fresh value after a run this long so that longer runs can be stated; VP3 does
      // not, and the runs it needs never reach the limit at the frame sizes it was used at.
      value = length == Vp3Tables.LONG_RUN_LIMIT ? reader.ReadBit() != 0 : !value;
    }
  }

  /// <summary>
  /// Reads <paramref name="count"/> bits coded as short runs, Section 7.2.2.
  /// </summary>
  private static void _ShortRuns(Vp3BitReader reader, bool[] bits, int count) {
    if (count == 0)
      return;

    var at = 0;
    var value = reader.ReadBit() != 0;
    while (true) {
      var code = Vp3Tables.ShortRunLengths.Read(reader);
      var length = Vp3Tables.ShortRunStarts[code] + reader.ReadBits(Vp3Tables.ShortRunExtraBits[code]);

      if (at + length > count)
        throw new InvalidDataException(
          $"A VP3 run of {length} block flags overruns the {count} the frame's partly coded super blocks have "
          + $"room for, {at} of them already read.");

      for (var i = 0; i < length; ++i)
        bits[at++] = value;

      if (at == count)
        return;

      value = !value;
    }
  }
}
