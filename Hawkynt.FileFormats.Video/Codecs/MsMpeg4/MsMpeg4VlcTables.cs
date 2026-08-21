using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The variable-length code tables Microsoft's MPEG-4 version 2 reads, which are ISO/IEC 14496-2's
/// own almost throughout.
/// </summary>
/// <remarks>
/// This is the finding that decides the shape of the whole decoder, and it is worth stating plainly
/// because the format's reputation says otherwise. Of the tables version 2 uses:
/// <list type="bullet">
/// <item>the coded block pattern for luminance is ISO/IEC 14496-2 Table B-8, unaltered;</item>
/// <item>the run-level codes are Table B-16 for an intra luminance block and Table B-17 for an intra
/// chrominance block and for every block of a predicted macroblock, both unaltered;</item>
/// <item>the intra DC size codes are Tables B-13 and B-14 with every bit inverted, which is what the
/// only published description of the format means by "0 and 1 are exchanged";</item>
/// <item>the motion vector difference is Table B-12, with the sign taken out of the code and read as
/// a bit of its own — which works because the standard's codes for a difference and its negation
/// differ in nothing but their last bit.</item>
/// </list>
/// Only two small tables are Microsoft's own, and they are the two below that are written out.
/// <para/>
/// <b>Where these come from.</b> Microsoft published no specification for this format: the Open
/// Specifications programme covers its protocols and container formats and not its codec bitstreams,
/// SMPTE ST 421 covers Windows Media Video 9 and says nothing of the earlier ones, and the one
/// Microsoft document that touches version 8 specifies motion compensation and deblocking while
/// leaving entropy coding to the host. The only public description of the bitstream is Michael
/// Niedermayer's <i>DIVX3 / MS-MPEG4v1-v3 / WMV7-8</i> (version 0.07, 2003, GNU Free Documentation
/// Licence), which gives the syntax in full and then refers the reader elsewhere for every large
/// table. So the syntax here follows that document and the tables were derived from the bitstream:
/// pictures were built with known content, encoded, and the codeword that had to stand for a known
/// (last, run, level) triple read out of what was left between two known ends. The two tables below
/// were settled by writing streams that use each codeword and asking a reference decoder what it made
/// of them, which is the only way to reach codewords no encoder emits.
/// <para/>
/// The derived tables are checked rather than trusted: <see cref="MacroblockType"/> is a complete
/// prefix code over its eight values, which the tests assert by Kraft's equality, and every table
/// built from an ISO one is built from that table rather than copied beside it, so the two cannot
/// drift apart.
/// </remarks>
internal static class MsMpeg4VlcTables {

  /// <summary>
  /// The two chrominance bits of the coded block pattern of an intra macroblock, in an intra picture.
  /// </summary>
  /// <remarks>
  /// Microsoft's own, and not ISO/IEC 14496-2 Table B-6: the standard's table states a macroblock type
  /// and a pattern together in one code, because an intra picture there may still change the
  /// quantiser. Version 2 states the quantiser once per picture and never again, so the type is known
  /// and only the two bits are left.
  /// <para/>
  /// Bit 1 is Cb and bit 0 is Cr, which is the order the blocks are coded in and the order the rest of
  /// the pattern uses. That was settled by building pictures with an alternating current coefficient
  /// in one chrominance block and not the other: an intra block carries its DC before its
  /// coefficients, so the two cases occupy different bits and can be told apart, which for a predicted
  /// macroblock they cannot.
  /// </remarks>
  internal static readonly Mpeg4VlcTable IntraChromaPattern = new(
    "v2_intra_cbpc (derived from the bitstream)",
    ("1", 0),
    ("01", 3),
    ("001", 2),
    ("000", 1));

  /// <summary>
  /// What a macroblock of a predicted picture is: bit 2 says it is intra coded, bits 1 and 0 are the
  /// chrominance half of its coded block pattern.
  /// </summary>
  /// <remarks>
  /// Microsoft's own. Half of it can be read off an encoder's output — the four values ffmpeg's
  /// encoder emits were found that way — and the other half cannot, because no encoder here produces a
  /// predicted macroblock that codes one chrominance block and not the other, nor an intra one that
  /// does. Those four were settled the other way about: a picture was written that uses the codeword,
  /// with three ordinary macroblocks behind it so that a codeword of the wrong length would put them
  /// out of step and ruin the picture, and a reference decoder was asked what came out. Only one value
  /// per codeword reproduces the picture that value predicts.
  /// </remarks>
  internal static readonly Mpeg4VlcTable MacroblockType = new(
    "v2_mb_type (derived from the bitstream)",
    ("1", 0),
    ("00", 1),
    ("011", 2),
    ("0100 1", 3),
    ("0101", 4),
    ("0100 001", 5),
    ("0100 000", 6),
    ("0100 01", 7));

  /// <summary>Whether a macroblock type states an intra coded macroblock.</summary>
  internal static bool IsIntra(int macroblockType) => (macroblockType & 4) != 0;

  /// <summary>The two chrominance bits of the coded block pattern a macroblock type states.</summary>
  internal static int ChromaPatternOf(int macroblockType) => macroblockType & 3;

  /// <summary>
  /// The size of an intra luminance DC differential: ISO/IEC 14496-2 Table B-13 with every bit
  /// inverted.
  /// </summary>
  /// <remarks>
  /// Inverted, not reordered. The values keep the meaning the standard gives them and the codewords
  /// are the standard's complemented, so a differential of nought is <c>100</c> here where the
  /// standard writes <c>011</c>. Building it from the standard's table rather than writing thirteen
  /// codes out again means a correction to one is a correction to both.
  /// </remarks>
  internal static readonly Mpeg4VlcTable LuminanceDcSize =
    _Invert(Mpeg4VlcTables.LuminanceDcSize, "Table B-13 with every bit inverted");

  /// <summary>The same for chrominance: ISO/IEC 14496-2 Table B-14 with every bit inverted.</summary>
  internal static readonly Mpeg4VlcTable ChrominanceDcSize =
    _Invert(Mpeg4VlcTables.ChrominanceDcSize, "Table B-14 with every bit inverted");

  /// <summary>
  /// The magnitude of a motion vector difference: ISO/IEC 14496-2 Table B-12 without its sign bit.
  /// </summary>
  /// <remarks>
  /// The standard's table maps a code to a signed difference, and Microsoft's format reads a magnitude
  /// and then a sign bit — which sounds like a different table and is the same one. Every pair of the
  /// standard's codes for a difference and its negation is identical but for the last bit, which is
  /// nought for the positive and one for the negative; strike that bit and what is left is a code for
  /// the magnitude. A difference of nought has no sign bit and keeps its whole code.
  /// <para/>
  /// So this is derived from Table B-12 here, and the property it relies on is asserted while deriving
  /// rather than assumed: a pair that did not agree would throw at start-up instead of decoding
  /// vectors that point the wrong way.
  /// </remarks>
  internal static readonly Mpeg4VlcTable MotionVectorMagnitude = _BuildMotionVectorMagnitudes();

  private static Mpeg4VlcTable _Invert(Mpeg4VlcTable source, string name) {
    var entries = new List<(string, int)>();
    foreach (var (code, value) in source.Entries) {
      Span<char> inverted = stackalloc char[code.Length];
      var length = 0;
      foreach (var character in code)
        switch (character) {
          case '0': inverted[length++] = '1'; break;
          case '1': inverted[length++] = '0'; break;
          default: break;
        }

      entries.Add((new(inverted[..length]), value));
    }

    return new(name, entries.ToArray());
  }

  private static Mpeg4VlcTable _BuildMotionVectorMagnitudes() {
    var codes = new Dictionary<int, string>();
    foreach (var (code, value) in Mpeg4VlcTables.MotionVectorDifference.Entries)
      codes[value] = code.Replace(" ", string.Empty);

    var entries = new List<(string, int)> { (codes[0], 0) };
    for (var magnitude = 1; magnitude <= 32; ++magnitude) {
      var positive = codes[magnitude];
      var negative = codes[-magnitude];

      if (positive.Length != negative.Length
          || positive[^1] != '0' || negative[^1] != '1'
          || !positive.AsSpan(0, positive.Length - 1).SequenceEqual(negative.AsSpan(0, negative.Length - 1)))
        throw new InvalidOperationException(
          $"ISO/IEC 14496-2 Table B-12 states '{positive}' for a motion vector difference of {magnitude} and "
          + $"'{negative}' for {-magnitude}. Microsoft's MPEG-4 version 2 reads a magnitude and then a sign bit, "
          + "which is the same table only while every such pair differs in nothing but its last bit.");

      entries.Add((positive[..^1], magnitude));
    }

    return new("Table B-12 without its sign bit", entries.ToArray());
  }
}
