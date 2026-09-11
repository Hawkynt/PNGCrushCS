using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// Writes which blocks of an inter frame carry coefficients, in the run-coded form
/// <see cref="Vp3BlockFlags"/> reads (Sections 7.2.1 and 7.2.2 of the Theora specification).
/// </summary>
/// <remarks>
/// The three passes are not three ways of saying the same thing: the first names the super blocks
/// that are partly coded, the second answers "all or nothing" for the rest <em>in their own order</em>
/// -- the partly coded ones are simply not asked -- and the third spends one flag per block, but only
/// on the blocks inside a partly coded super block. A writer that emitted a flag per block everywhere
/// would be read back correctly by nothing, because the reader counts its way through each pass by
/// how many answers the previous one implied.
/// <para/>
/// The runs themselves alternate: a run states a length, and the value flips for the next one, so only
/// the first value is written. The one exception is a run that reaches the table's limit, which is how
/// a run longer than the table can be stated at all -- there the value is written again rather than
/// flipped, and a writer that flipped anyway would invert the rest of the frame.
/// </remarks>
internal static class Vp3BlockFlagWriter {

  internal static void Write(
    Vp3BitWriter writer, Vp3Geometry geometry, bool[] coded,
    bool[] partial, bool[] whole, bool[] inside) {
    var superBlocks = geometry.SuperBlockCount;
    var blocks = geometry.BlockCount;

    // A super block is partly coded when its blocks disagree with each other.
    for (var superBlock = 0; superBlock < superBlocks; ++superBlock) {
      partial[superBlock] = false;
      whole[superBlock] = false;
    }

    var seen = new bool[superBlocks];
    var first = new bool[superBlocks];
    for (var block = 0; block < blocks; ++block) {
      var superBlock = geometry.BlockSuperBlock[block];
      if (!seen[superBlock]) {
        seen[superBlock] = true;
        first[superBlock] = coded[block];
        continue;
      }

      if (coded[block] != first[superBlock])
        partial[superBlock] = true;
    }

    for (var superBlock = 0; superBlock < superBlocks; ++superBlock)
      whole[superBlock] = !partial[superBlock] && first[superBlock];

    _LongRuns(writer, partial, superBlocks);

    // The "all or nothing" answers, stated only for the super blocks that are not partly coded and
    // in the order those come.
    var plainCount = 0;
    for (var superBlock = 0; superBlock < superBlocks; ++superBlock)
      if (!partial[superBlock])
        inside[plainCount++] = whole[superBlock];

    _LongRuns(writer, inside, plainCount);

    // One flag per block, for the blocks of the partly coded super blocks only.
    var insideCount = 0;
    for (var block = 0; block < blocks; ++block)
      if (partial[geometry.BlockSuperBlock[block]])
        inside[insideCount++] = coded[block];

    _ShortRuns(writer, inside, insideCount);
  }

  private static void _LongRuns(Vp3BitWriter writer, bool[] bits, int count) {
    if (count == 0)
      return;

    writer.WriteBit(bits[0] ? 1 : 0);

    var at = 0;
    var value = bits[0];
    while (at < count) {
      var length = 1;
      while (at + length < count && bits[at + length] == value && length < Vp3Tables.LONG_RUN_LIMIT)
        ++length;

      _WriteRun(writer, Vp3Tables.LongRunLengths, Vp3Tables.LongRunStarts, Vp3Tables.LongRunExtraBits, length);
      at += length;
      if (at == count)
        return;

      // The reader flips the value after every run but one: a run at the table's limit is followed
      // by a fresh value, because a longer run has to be spelled as two.
      if (length == Vp3Tables.LONG_RUN_LIMIT)
        writer.WriteBit(bits[at] ? 1 : 0);

      value = bits[at];
    }
  }

  private static void _ShortRuns(Vp3BitWriter writer, bool[] bits, int count) {
    if (count == 0)
      return;

    writer.WriteBit(bits[0] ? 1 : 0);

    // Short runs alternate with no escape, so a run longer than the table can state cannot be split
    // into two of the same value -- the reader would flip between them. The run is therefore not
    // capped here: an over-long one reaches _WriteRun and is refused, loudly, rather than written as
    // a stream that reads back inverted from the first over-long run onwards. The encoder avoids the
    // situation by deciding codedness a whole super block at a time, which leaves this pass empty.
    var at = 0;
    while (at < count) {
      var value = bits[at];
      var length = 1;
      while (at + length < count && bits[at + length] == value)
        ++length;

      _WriteRun(writer, Vp3Tables.ShortRunLengths, Vp3Tables.ShortRunStarts, Vp3Tables.ShortRunExtraBits, length);
      at += length;
    }
  }

  /// <summary>
  /// Writes one run length as the code whose range covers it plus the extra bits that place it
  /// inside that range.
  /// </summary>
  private static void _WriteRun(
    Vp3BitWriter writer, Vp3VlcTable lengths, int[] starts, int[] extraBits, int length) {
    for (var code = starts.Length - 1; code >= 0; --code) {
      if (length < starts[code])
        continue;

      var span = 1 << extraBits[code];
      if (code < starts.Length - 1 && length >= starts[code] + span)
        continue;

      lengths.Write(writer, code);
      if (extraBits[code] > 0)
        writer.WriteBits((uint)(length - starts[code]), extraBits[code]);

      return;
    }

    throw new ArgumentOutOfRangeException(
      nameof(length), length, "No VP3 run-length code covers this run, which means the run was built too long.");
  }
}
