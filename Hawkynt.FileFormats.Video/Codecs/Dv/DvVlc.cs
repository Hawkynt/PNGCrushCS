using System;

namespace FileFormat.Codecs.Dv;

/// <summary>
/// The run-level code book, in both directions: a flat table to decode with and a run-by-level table
/// to encode from.
/// </summary>
/// <remarks>
/// DV assigns its codes canonically in the order <see cref="DvTables.CodeLengths"/> lists them, so
/// the code book is entirely determined by that one table and nothing here is a further constant.
/// The sign is not part of the code: a code naming a non-zero magnitude is followed by one bit, which
/// is folded into the decoding table so that a coefficient comes out of a single lookup.
/// <para/>
/// The two directions do not share a table because the mapping is not one to one. Several run-level
/// pairs have two codes — <c>(1, 0)</c> is both <c>0x7cf</c> and <c>0x1f82</c> — so inverting the
/// decoder's table would pick whichever came last rather than the shorter one, and a run or level
/// with no code of its own has to be written as two codes in a row, which the decoder's table cannot
/// express at all.
/// </remarks>
internal static class DvVlc {

  /// <summary>One decoded run-level pair, with the sign bit already accounted for.</summary>
  /// <param name="Length">The bits the code and its sign together occupy.</param>
  /// <param name="Run">The zero coefficients before this one; 127 is the end-of-block stamp.</param>
  /// <param name="Level">The signed magnitude, or zero where the code states only a run.</param>
  internal readonly record struct Code(byte Length, byte Run, short Level);

  /// <summary>The longest code, sign bit included.</summary>
  private const int _LONGEST_CODE = 16;

  private const int _TABLE_SIZE = 1 << _LONGEST_CODE;

  /// <summary>The run size of the encoding table: every run a coefficient position can name.</summary>
  private const int _ENCODE_RUNS = 64;

  /// <summary>The level size of the encoding table; the upper half holds the negative magnitudes.</summary>
  private const int _ENCODE_LEVELS = 512;

  private static readonly Code[] _decode = _BuildDecodeTable();
  private static readonly uint[] _encodeBits = new uint[_ENCODE_RUNS * _ENCODE_LEVELS];
  private static readonly byte[] _encodeSizes = new byte[_ENCODE_RUNS * _ENCODE_LEVELS];

  static DvVlc() => _BuildEncodeTable();

  /// <summary>
  /// Decodes the code at the head of a sixteen-bit window.
  /// </summary>
  /// <remarks>
  /// One lookup, no loop: the table is indexed by sixteen bits because that is the longest a code and
  /// its sign can be, and the book is complete, so every window decodes to something. Whether the
  /// code actually fitted inside the block's budget is the caller's question, not this one's.
  /// </remarks>
  internal static Code Decode(uint window) => _decode[window >> (32 - _LONGEST_CODE)];

  /// <summary>The bits that write a run and a magnitude, sign bit included but left clear.</summary>
  internal static uint EncodeBits(int run, int level) => _encodeBits[run * _ENCODE_LEVELS + (level & (_ENCODE_LEVELS - 1))];

  /// <summary>How many bits <see cref="EncodeBits"/> hands back for the same pair.</summary>
  internal static int EncodeSize(int run, int level) => _encodeSizes[run * _ENCODE_LEVELS + (level & (_ENCODE_LEVELS - 1))];

  /// <summary>
  /// Lays the whole code book out flat, one entry per sixteen-bit window.
  /// </summary>
  /// <remarks>
  /// Canonical assignment: each code is the running total of <c>2^(32-length)</c> taken in table
  /// order, which is the same rule an encoder uses, so the two agree by construction rather than by
  /// two people having transcribed the same list.
  /// </remarks>
  private static Code[] _BuildDecodeTable() {
    var table = new Code[_TABLE_SIZE];
    var code = 0u;

    for (var i = 0; i < DvTables.CodeCount; ++i) {
      int length = DvTables.CodeLengths[i];
      var value = code >> (32 - length);
      code += 1u << (32 - length);

      var run = DvTables.CodeRuns[i];
      int level = DvTables.CodeLevels[i];

      if (level == 0) {
        _Fill(table, value, length, new((byte)length, run, 0));
        continue;
      }

      // A magnitude is followed by one sign bit, so the code covers two windows: the same prefix with
      // a clear bit for the positive value and a set one for the negative.
      _Fill(table, (value << 1) | 0, length + 1, new((byte)(length + 1), run, (short)level));
      _Fill(table, (value << 1) | 1, length + 1, new((byte)(length + 1), run, (short)-level));
    }

    return table;
  }

  /// <summary>Writes one entry into every window that begins with a code.</summary>
  private static void _Fill(Code[] table, uint prefix, int length, Code entry) {
    var span = 1 << (_LONGEST_CODE - length);
    var start = (int)(prefix << (_LONGEST_CODE - length));
    for (var i = 0; i < span; ++i)
      table[start + i] = entry;
  }

  /// <summary>
  /// Builds the run-by-level table an encoder writes from.
  /// </summary>
  /// <remarks>
  /// Two steps, and the second is what makes the table usable. The first takes every pair the code
  /// book names directly, keeping the first code for a pair that has two — which is the shorter one,
  /// because the book is in ascending length order. The second fills every pair the book does not
  /// name at all by concatenating the code for the run with the code for the magnitude, which is
  /// exactly how such a pair is written: two codes in a row, and a decoder walking them adds the runs
  /// up on its own.
  /// </remarks>
  private static void _BuildEncodeTable() {
    var code = 0u;

    for (var i = 0; i < DvTables.CodeCount; ++i) {
      int length = DvTables.CodeLengths[i];
      var value = code >> (32 - length);
      code += 1u << (32 - length);

      int run = DvTables.CodeRuns[i];
      int level = DvTables.CodeLevels[i];
      if (run >= _ENCODE_RUNS)
        continue;

      var slot = run * _ENCODE_LEVELS + level;
      if (_encodeSizes[slot] != 0)
        continue;

      // The sign bit is part of the written code for a non-zero magnitude; it is left clear here and
      // set by the caller, which is why the code is shifted up by one.
      _encodeBits[slot] = level != 0 ? value << 1 : value;
      _encodeSizes[slot] = (byte)(length + (level != 0 ? 1 : 0));
    }

    for (var run = 0; run < _ENCODE_RUNS; ++run)
      for (var level = 1; level < _ENCODE_LEVELS / 2; ++level) {
        var slot = run * _ENCODE_LEVELS + level;
        if (_encodeSizes[slot] == 0) {
          // A run of no zeros is named directly for every magnitude the book covers, so the two-code
          // form is only ever needed from a run of one upwards; a gap at run zero would mean the code
          // table itself had been transcribed wrongly.
          if (run == 0)
            throw new InvalidOperationException($"The DV code table names no code for a magnitude of {level} with no run.");

          var runOnly = (run - 1) * _ENCODE_LEVELS;
          _encodeBits[slot] = _encodeBits[level] | (_encodeBits[runOnly] << _encodeSizes[level]);
          _encodeSizes[slot] = (byte)(_encodeSizes[runOnly] + _encodeSizes[level]);
        }

        // The negative magnitudes live in the upper half, at the two's-complement index, and differ
        // from their positive twins only in the sign bit.
        var negative = run * _ENCODE_LEVELS + ((-level) & (_ENCODE_LEVELS - 1));
        _encodeBits[negative] = _encodeBits[slot] | 1;
        _encodeSizes[negative] = _encodeSizes[slot];
      }
  }
}
