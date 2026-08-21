using System.IO;

namespace FileFormat.Codecs.Theora;

/// <summary>
/// The two run-length codes Theora carries its bit strings in.
/// </summary>
/// <remarks>
/// Theora specification section 7.2. Both encode an alternating sequence of runs: the value of the
/// first run is written out, and every run after it toggles, so a string of flags costs one Huffman
/// code a run rather than a bit a flag. Which of the two codes is used depends on whether the run
/// length is bounded.
/// <para/>
/// The long code has no practical limit and is used for the super block flags and the block-level
/// quantisation indices. It has one wrinkle: a run of exactly 4129 — the longest it can state — is
/// followed by a fresh value bit rather than a toggle, so that runs longer than the code can express
/// are written as several. VP3 does not do this, which is where its 4129-flag ceiling comes from.
/// <para/>
/// The short code stops at 30, which is the longest run needed when writing one bit for each of the
/// sixteen blocks of a super block given that not all of them are alike.
/// <para/>
/// Both are written in the specification as tables of Huffman codes, and both turn out to be a unary
/// prefix followed by a fixed number of extra bits, so both are decoded by counting leading ones.
/// </remarks>
internal static class TheoraRunLength {

  /// <summary>
  /// Reads a run-length coded string of the given length into a caller's buffer.
  /// </summary>
  /// <param name="reader">The packet.</param>
  /// <param name="bits">The buffer to fill; exactly <paramref name="count"/> entries are written.</param>
  /// <param name="count">How many flags to decode. Nothing at all is read when this is zero.</param>
  /// <param name="what">What is being decoded, for a refusal that names it.</param>
  internal static void ReadLong(TheoraBitReader reader, bool[] bits, int count, string what) {
    if (count == 0)
      return;

    var written = 0;
    var value = reader.ReadBit();

    while (true) {
      // The unary prefix: up to six ones, the sixth of which is the code rather than a lead-in.
      var code = 0;
      while (code < 6 && reader.ReadBit() == 1)
        ++code;

      var run = TheoraTables.LongRunStart[code] + (int)reader.ReadBits(TheoraTables.LongRunBits[code]);
      if (written + run > count)
        throw new InvalidDataException(
          $"A run of {run} in {what} overruns the {count} flags there are room for, {written} of them already decoded.");

      for (var i = 0; i < run; ++i)
        bits[written + i] = value != 0;

      written += run;
      if (written == count)
        return;

      // A run of the maximum length is not the end of the alternation — it is a run that did not
      // fit, so the next value is read rather than toggled.
      value = run == TheoraTables.LONG_RUN_MAXIMUM ? reader.ReadBit() : 1 - value;
    }
  }

  /// <summary>Reads a run-length coded string whose runs are known not to exceed thirty.</summary>
  internal static void ReadShort(TheoraBitReader reader, bool[] bits, int count, string what) {
    if (count == 0)
      return;

    var written = 0;
    var value = reader.ReadBit();

    while (true) {
      var code = 0;
      while (code < 5 && reader.ReadBit() == 1)
        ++code;

      var run = TheoraTables.ShortRunStart[code] + (int)reader.ReadBits(TheoraTables.ShortRunBits[code]);
      if (written + run > count)
        throw new InvalidDataException(
          $"A run of {run} in {what} overruns the {count} flags there are room for, {written} of them already decoded.");

      for (var i = 0; i < run; ++i)
        bits[written + i] = value != 0;

      written += run;
      if (written == count)
        return;

      value = 1 - value;
    }
  }
}
