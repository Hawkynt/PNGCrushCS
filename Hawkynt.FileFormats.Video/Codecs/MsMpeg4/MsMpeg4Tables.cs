using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The code tables of <see cref="MsMpeg4Data"/>, built once into the form the decoder reads them in.
/// </summary>
/// <remarks>
/// Nothing here is a table of its own. Every member is either a straight construction from the arrays
/// in <see cref="MsMpeg4Data"/> or, in the case of the DC tables versions 1 and 2 use, a construction
/// from an ISO/IEC 14496-2 table by the rule Microsoft applied to it. That rule is worth stating,
/// because it is the whole of the difference: the standard's code is inverted bit for bit, and the
/// magnitude bits and the marker bit past eight of them follow inside the same codeword rather than
/// outside it. So the table is five hundred and twelve entries where the standard's is thirteen, and
/// it maps a codeword straight to a differential rather than to the number of bits one occupies.
/// </remarks>
internal static class MsMpeg4Tables {

  /// <summary>How many entries the version 1 and 2 DC tables hold: one per differential.</summary>
  private const int _V2_DC_ENTRIES = 512;

  /// <summary>Where nought sits in them, which is what a decoded entry has taken off it.</summary>
  internal const int V2DcBias = 256;

  /// <summary>The escape entry of a version 3 DC table: the differential follows in eight bits.</summary>
  internal const int V3DcEscape = 119;

  /// <summary>
  /// The six run-level tables, the first three for intra luminance and the last three for everything
  /// else.
  /// </summary>
  /// <remarks>
  /// The split is by index and not by table: a picture states one index for luminance and one for
  /// chrominance, and the chrominance one is offset by three. So an intra luminance block reads
  /// <c>RunLevel[index]</c> and every other block reads <c>RunLevel[3 + index]</c>, which is why the
  /// two halves are one array rather than two.
  /// </remarks>
  internal static readonly MsMpeg4RunLevelTable[] RunLevel = [
    new("run-level table 0 (intra, low motion)", 85,
      MsMpeg4Data.LowMotionIntraCodes, MsMpeg4Data.LowMotionIntraLengths,
      MsMpeg4Data.LowMotionIntraRuns, MsMpeg4Data.LowMotionIntraLevels),
    new("run-level table 1 (intra, high motion)", 119,
      MsMpeg4Data.HighMotionIntraCodes, MsMpeg4Data.HighMotionIntraLengths,
      MsMpeg4Data.HighMotionIntraRuns, MsMpeg4Data.HighMotionIntraLevels),
    new("run-level table 2 (intra, mid rate)", 67,
      MsMpeg4Data.MidRateIntraCodes, MsMpeg4Data.MidRateIntraLengths,
      MsMpeg4Data.MidRateIntraRuns, MsMpeg4Data.MidRateIntraLevels),
    new("run-level table 3 (inter, low motion)", 81,
      MsMpeg4Data.LowMotionInterCodes, MsMpeg4Data.LowMotionInterLengths,
      MsMpeg4Data.LowMotionInterRuns, MsMpeg4Data.LowMotionInterLevels),
    new("run-level table 4 (inter, high motion)", 99,
      MsMpeg4Data.HighMotionInterCodes, MsMpeg4Data.HighMotionInterLengths,
      MsMpeg4Data.HighMotionInterRuns, MsMpeg4Data.HighMotionInterLevels),
    new("run-level table 5 (inter, mid rate)", 58,
      MsMpeg4Data.MidRateInterCodes, MsMpeg4Data.MidRateInterLengths,
      MsMpeg4Data.MidRateInterRuns, MsMpeg4Data.MidRateInterLevels),
  ];

  /// <summary>Version 3's two motion vector tables, each a whole vector per codeword.</summary>
  internal static readonly MsMpeg4VlcTable[] MotionVector = [
    MsMpeg4VlcTable.FromLengths("motion vector table 0", MsMpeg4Data.MotionVector0Lengths, MsMpeg4Data.MotionVector0Symbols),
    MsMpeg4VlcTable.FromLengths("motion vector table 1", MsMpeg4Data.MotionVector1Lengths, MsMpeg4Data.MotionVector1Symbols),
  ];

  /// <summary>The same two tables turned round, so that an encoder can write a vector.</summary>
  internal static readonly MsMpeg4MotionVectorCodes[] MotionVectorCodes = [
    new(MsMpeg4Data.MotionVector0Lengths, MsMpeg4Data.MotionVector0Symbols),
    new(MsMpeg4Data.MotionVector1Lengths, MsMpeg4Data.MotionVector1Symbols),
  ];

  /// <summary>Version 3's two DC tables, luminance first and chrominance second in each.</summary>
  internal static readonly MsMpeg4VlcTable[][] Dc = [
    [
      MsMpeg4VlcTable.FromCodes("DC table 0 (luminance)", MsMpeg4Data.Dc0LuminanceCodes, MsMpeg4Data.Dc0LuminanceLengths),
      MsMpeg4VlcTable.FromCodes("DC table 0 (chrominance)", MsMpeg4Data.Dc0ChrominanceCodes, MsMpeg4Data.Dc0ChrominanceLengths),
    ],
    [
      MsMpeg4VlcTable.FromCodes("DC table 1 (luminance)", MsMpeg4Data.Dc1LuminanceCodes, MsMpeg4Data.Dc1LuminanceLengths),
      MsMpeg4VlcTable.FromCodes("DC table 1 (chrominance)", MsMpeg4Data.Dc1ChrominanceCodes, MsMpeg4Data.Dc1ChrominanceLengths),
    ],
  ];

  /// <summary>
  /// The codewords and lengths of the DC table versions 1 and 2 read, luminance first and chrominance
  /// second, indexed by the differential plus <see cref="V2DcBias"/>.
  /// </summary>
  internal static readonly (int[] Codes, int[] Lengths)[] V2DcCodes = [
    _BuildV2Dc(MsMpeg4Data.Mpeg4DcLuminanceCodes, MsMpeg4Data.Mpeg4DcLuminanceLengths),
    _BuildV2Dc(MsMpeg4Data.Mpeg4DcChrominanceCodes, MsMpeg4Data.Mpeg4DcChrominanceLengths),
  ];

  /// <summary>The same table as the decoder reads it.</summary>
  internal static readonly MsMpeg4VlcTable[] V2Dc = [
    MsMpeg4VlcTable.FromCodes("version 1 and 2 DC (luminance)", V2DcCodes[0].Codes, V2DcCodes[0].Lengths),
    MsMpeg4VlcTable.FromCodes("version 1 and 2 DC (chrominance)", V2DcCodes[1].Codes, V2DcCodes[1].Lengths),
  ];

  /// <summary>Version 3, intra picture: the six coded block pattern bits before their prediction is undone.</summary>
  internal static readonly MsMpeg4VlcTable IntraMacroblockPattern =
    MsMpeg4VlcTable.FromCodes("the intra macroblock table", MsMpeg4Data.IntraMacroblockPatternCodes, MsMpeg4Data.IntraMacroblockPatternLengths);

  /// <summary>Version 3, predicted picture: whether the macroblock is intra, and its coded block pattern.</summary>
  internal static readonly MsMpeg4VlcTable MacroblockNonIntra =
    MsMpeg4VlcTable.FromCodes("the predicted macroblock table", MsMpeg4Data.MacroblockNonIntraCodes, MsMpeg4Data.MacroblockNonIntraLengths);

  /// <summary>Version 2, predicted picture: the same, in eight values instead of a hundred and twenty-eight.</summary>
  internal static readonly MsMpeg4VlcTable V2MacroblockType =
    MsMpeg4VlcTable.FromCodes("the version 2 macroblock type table", MsMpeg4Data.V2MacroblockTypeCodes, MsMpeg4Data.V2MacroblockTypeLengths);

  /// <summary>Version 2, intra picture: the two chrominance bits of the coded block pattern.</summary>
  internal static readonly MsMpeg4VlcTable V2IntraChromaPattern =
    MsMpeg4VlcTable.FromCodes("the version 2 intra chrominance table", MsMpeg4Data.V2IntraChromaPatternCodes, MsMpeg4Data.V2IntraChromaPatternLengths);

  /// <summary>Version 1, intra macroblock: H.263's own MCBPC table, which version 1 borrows whole.</summary>
  internal static readonly MsMpeg4VlcTable IntraMacroblock =
    MsMpeg4VlcTable.FromCodes("H.263 intra MCBPC", MsMpeg4Data.IntraMacroblockCodes, MsMpeg4Data.IntraMacroblockLengths);

  /// <summary>Version 1, macroblock of a predicted picture: H.263's inter MCBPC table.</summary>
  internal static readonly MsMpeg4VlcTable InterMacroblock =
    MsMpeg4VlcTable.FromCodes("H.263 inter MCBPC", MsMpeg4Data.InterMacroblockCodes, MsMpeg4Data.InterMacroblockLengths);

  /// <summary>The four luminance bits of a coded block pattern, which all three versions read from H.263.</summary>
  internal static readonly MsMpeg4VlcTable CodedBlockPatternY =
    MsMpeg4VlcTable.FromCodes("H.263 CBPY", MsMpeg4Data.CodedBlockPatternYCodes, MsMpeg4Data.CodedBlockPatternYLengths);

  /// <summary>The magnitude of a motion vector difference, which versions 1 and 2 read from H.263.</summary>
  internal static readonly MsMpeg4VlcTable MotionVectorMagnitude =
    MsMpeg4VlcTable.FromCodes("H.263 motion vector magnitudes", MsMpeg4Data.MotionVectorMagnitudeCodes, MsMpeg4Data.MotionVectorMagnitudeLengths);

  /// <summary>
  /// Builds one of the DC tables versions 1 and 2 read, out of the ISO/IEC 14496-2 table it is made
  /// from.
  /// </summary>
  /// <remarks>
  /// Three changes to the standard's table and each of them matters. The codeword is inverted bit for
  /// bit. The magnitude bits follow inside the same codeword, in the mapping JPEG uses, where a
  /// negative differential of <c>n</c> bits is written as the complement of its magnitude. And a
  /// differential of more than eight bits carries a marker bit at the end, which exists so that a long
  /// run of zeroes cannot come out of a DC code.
  /// <para/>
  /// Written out as five hundred and twelve codewords rather than as thirteen plus a rule, so that the
  /// reader is one table walk and there is nothing to get wrong per block.
  /// </remarks>
  private static (int[] Codes, int[] Lengths) _BuildV2Dc(int[] sizeCodes, int[] sizeLengths) {
    var codes = new int[_V2_DC_ENTRIES];
    var lengths = new int[_V2_DC_ENTRIES];

    for (var differential = -V2DcBias; differential < V2DcBias; ++differential) {
      var magnitude = Math.Abs(differential);
      var size = 0;
      for (var v = magnitude; v != 0; v >>= 1)
        ++size;

      var bits = differential < 0 ? magnitude ^ ((1 << size) - 1) : differential;
      var code = sizeCodes[size] ^ ((1 << sizeLengths[size]) - 1);
      var length = sizeLengths[size];

      if (size > 0) {
        code = (code << size) | bits;
        length += size;

        if (size > 8) {
          code = (code << 1) | 1;
          ++length;
        }
      }

      codes[differential + V2DcBias] = code;
      lengths[differential + V2DcBias] = length;
    }

    return (codes, lengths);
  }
}
