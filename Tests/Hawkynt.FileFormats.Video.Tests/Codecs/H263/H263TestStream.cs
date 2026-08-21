using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>
/// Writes H.263 and Sorenson Spark bitstreams a bit at a time, so a test can state exactly which
/// syntax it is exercising.
/// </summary>
/// <remarks>
/// Every stream in this library's tests is built rather than checked in, and for a codec that matters
/// more than usual: the paths worth testing are the ones a real encoder never produces. ffmpeg's
/// H.263 encoder emits no group of blocks headers unless asked, no DQUANT that needs clipping, no
/// stuffing codewords and none of the optional modes at all, so comparing against it — which is how
/// the decoder's arithmetic was checked — cannot reach any of those. These can, and so can the
/// refusals, which by definition no valid stream produces.
/// <para/>
/// Codes are given as the strings ITU-T H.263 prints them as, for the same reason the decoder's
/// tables are: a test that encoded with numbers would be checking the decoder against a second
/// transcription of the same table, and two transcriptions of one table go wrong together. Where a
/// test needs a particular code it writes the code, and the assertion states separately what it is
/// supposed to mean.
/// </remarks>
internal sealed class H263TestStream {

  /// <summary>MCBPC for an intra macroblock with no coded chrominance (Table 7, index 0).</summary>
  internal const string IntraMacroblock = "1";

  /// <summary>MCBPC for an intra macroblock with both chrominance blocks coded (Table 7, index 3).</summary>
  internal const string IntraMacroblockWithChrominance = "011";

  /// <summary>MCBPC for an intra macroblock carrying DQUANT (Table 7, index 4).</summary>
  internal const string IntraMacroblockWithQuantiser = "0001";

  /// <summary>MCBPC for a predicted macroblock with no coded chrominance (Table 8, index 0).</summary>
  internal const string InterMacroblock = "1";

  /// <summary>MCBPC for a predicted macroblock carrying DQUANT (Table 8, index 4).</summary>
  internal const string InterMacroblockWithQuantiser = "011";

  /// <summary>MCBPC for a predicted macroblock with four motion vectors (Table 8, index 8).</summary>
  internal const string InterMacroblockWithFourVectors = "010";

  /// <summary>MCBPC for an intra macroblock inside a predicted picture (Table 8, index 12).</summary>
  internal const string IntraMacroblockInPredictedPicture = "0001 1";

  /// <summary>The stuffing codeword both MCBPC tables carry, which stands for no macroblock at all.</summary>
  internal const string MacroblockStuffing = "0000 0000 1";

  /// <summary>CBPY for a pattern of none of the four luminance blocks, read as an intra macroblock.</summary>
  internal const string NoLuminanceCoded = "0011";

  /// <summary>CBPY for a pattern of all four luminance blocks, read as an intra macroblock.</summary>
  internal const string AllLuminanceCoded = "11";

  /// <summary>CBPY whose intra reading is 1000: the first luminance block only (Table 12, index 8).</summary>
  internal const string FirstLuminanceCoded = "0001 0";

  /// <summary>TCOEF for the last coefficient of a block, run nought, level one (Table 16, index 58).</summary>
  internal const string LastCoefficientLevelOne = "0111";

  /// <summary>The TCOEF escape (Table 16, index 102).</summary>
  internal const string CoefficientEscape = "0000 011";

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal H263TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written the way the Recommendation prints it; spaces are grouping.</summary>
  internal H263TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Pads with zeroes to the next byte boundary, which every picture start code follows.</summary>
  internal H263TestStream AlignToByte() {
    while (this._partialBits != 0)
      this._Bit(0);

    return this;
  }

  internal byte[] ToArray() {
    this.AlignToByte();
    return this._bytes.ToArray();
  }

  // --------------------------------------------------------------------------------------------
  // Picture layer — ITU-T H.263, 5.1
  // --------------------------------------------------------------------------------------------

  /// <summary>
  /// A baseline H.263 picture header.
  /// </summary>
  /// <param name="sourceFormat">Table 5's code: 1 sub-QCIF, 2 QCIF, 3 CIF, 4 4CIF, 5 16CIF.</param>
  internal H263TestStream PictureHeader(
    int sourceFormat = 1, bool isIntra = true, int quantiser = 1, int temporalReference = 0,
    bool unrestrictedMotionVectors = false, bool arithmeticCoding = false, bool advancedPrediction = false,
    bool pbFrames = false, bool continuousPresenceMultipoint = false,
    int firstPtypeBit = 1, int secondPtypeBit = 0) {
    this.AlignToByte();
    this.Bits(1, 17);                     // PSC, and the GN of zero that completes it
    this.Bits(0, 5);
    this.Bits(temporalReference, 8);

    this.Bits(firstPtypeBit, 1);
    this.Bits(secondPtypeBit, 1);
    this.Bits(0, 3);                      // split screen, document camera, freeze release
    this.Bits(sourceFormat, 3);
    this.Bits(isIntra ? 0 : 1, 1);
    this.Bits(unrestrictedMotionVectors ? 1 : 0, 1);
    this.Bits(arithmeticCoding ? 1 : 0, 1);
    this.Bits(advancedPrediction ? 1 : 0, 1);
    this.Bits(pbFrames ? 1 : 0, 1);

    this.Bits(quantiser, 5);
    this.Bits(continuousPresenceMultipoint ? 1 : 0, 1);
    this.Bits(0, 1);                      // PEI
    return this;
  }

  /// <summary>
  /// A Sorenson Spark picture header.
  /// </summary>
  /// <param name="sizeCode">
  /// 0 and 1 carry the size in the bitstream, in one byte or two per dimension; 2 to 6 name a size.
  /// </param>
  internal H263TestStream SorensonPictureHeader(
    int version = 0, int sizeCode = 0, int width = 16, int height = 16, int pictureType = 0,
    bool deblocking = false, int quantiser = 1, int temporalReference = 0) {
    this.AlignToByte();
    this.Bits(1, 17);                     // PSC, without the group number H.263 puts after it
    this.Bits(version, 5);
    this.Bits(temporalReference, 8);
    this.Bits(sizeCode, 3);

    switch (sizeCode) {
      case 0:
        this.Bits(width, 8);
        this.Bits(height, 8);
        break;

      case 1:
        this.Bits(width, 16);
        this.Bits(height, 16);
        break;
    }

    this.Bits(pictureType, 2);
    this.Bits(deblocking ? 1 : 0, 1);
    this.Bits(quantiser, 5);
    this.Bits(0, 1);                      // extra information flag
    return this;
  }

  /// <summary>A group of blocks header (ITU-T H.263, 5.2).</summary>
  internal H263TestStream GroupHeader(int groupNumber, int quantiser) {
    this.AlignToByte();
    this.Bits(1, 17);                     // GBSC
    this.Bits(groupNumber, 5);
    this.Bits(0, 2);                      // GFID
    this.Bits(quantiser, 5);
    return this;
  }

  // --------------------------------------------------------------------------------------------
  // Macroblock and block layers — ITU-T H.263, 5.3 and 5.4
  // --------------------------------------------------------------------------------------------

  /// <summary>The COD bit, which is present in a predicted picture for every macroblock.</summary>
  internal H263TestStream Coded(bool coded = true) => this.Bits(coded ? 0 : 1, 1);

  /// <summary>An intra block: its eight-bit DC value and then whatever coefficient codes follow.</summary>
  internal H263TestStream IntraBlock(int intraDc, params string[] coefficients) {
    this.Bits(intraDc, 8);
    foreach (var code in coefficients)
      this.Code(code);

    return this;
  }

  /// <summary>The escape form of a coefficient code, in H.263's shape.</summary>
  internal H263TestStream EscapedCoefficient(bool last, int run, int level) {
    this.Code(CoefficientEscape);
    this.Bits(last ? 1 : 0, 1);
    this.Bits(run, 6);
    this.Bits(level & 0xFF, 8);
    return this;
  }

  /// <summary>The escape form a Sorenson Spark stream of version 1 uses instead.</summary>
  internal H263TestStream SorensonEscapedCoefficient(bool last, int run, int level, int levelBits) {
    this.Code(CoefficientEscape);
    this.Bits(levelBits == 11 ? 1 : 0, 1);
    this.Bits(last ? 1 : 0, 1);
    this.Bits(run, 6);
    this.Bits(level & ((1 << levelBits) - 1), levelBits);
    return this;
  }

  /// <summary>
  /// A whole intra macroblock whose six blocks are flat: one DC value each and no coefficients.
  /// </summary>
  internal H263TestStream FlatIntraMacroblock(int luminanceDc, int chrominanceDc = 255) {
    this.Code(IntraMacroblock).Code(NoLuminanceCoded);
    for (var block = 0; block < 4; ++block)
      this.IntraBlock(luminanceDc);

    return this.IntraBlock(chrominanceDc).IntraBlock(chrominanceDc);
  }

  /// <summary>Fills <paramref name="count"/> macroblocks with flat intra ones.</summary>
  internal H263TestStream FlatIntraMacroblocks(int count, int luminanceDc, int chrominanceDc = 255) {
    for (var macroblock = 0; macroblock < count; ++macroblock)
      this.FlatIntraMacroblock(luminanceDc, chrominanceDc);

    return this;
  }

  /// <summary>Fills <paramref name="count"/> macroblocks of a predicted picture with uncoded ones.</summary>
  internal H263TestStream NotCodedMacroblocks(int count) {
    for (var macroblock = 0; macroblock < count; ++macroblock)
      this.Coded(false);

    return this;
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
