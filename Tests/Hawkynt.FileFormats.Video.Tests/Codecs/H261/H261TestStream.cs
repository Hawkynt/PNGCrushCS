using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>
/// Writes H.261 bitstreams a bit at a time, so a test can state exactly which syntax it is exercising.
/// </summary>
/// <remarks>
/// ffmpeg's H.261 encoder was measured never to emit the loop filter, a mid-group MQUANT, or the
/// bit-stuffing codeword — three things every real corpus comparison here therefore cannot reach, and
/// which this builds by hand instead, the same way <c>H263TestStream</c> reaches syntax ffmpeg's H.263
/// encoder never produces.
/// </remarks>
internal sealed class H261TestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  internal H261TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  internal H261TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  internal byte[] ToArray() {
    while (this._partialBits != 0)
      this._Bit(0);

    return this._bytes.ToArray();
  }

  // --------------------------------------------------------------------------------------------
  // Picture and group of blocks layers — ITU-T H.261, 4.2.1 and 4.2.2
  // --------------------------------------------------------------------------------------------

  /// <summary>A picture header naming QCIF or CIF, with the still image mode of Annex D off.</summary>
  internal H261TestStream PictureHeader(
    bool isCif = false, int temporalReference = 0, bool requestStillImage = false) {
    this.Bits(H261PictureHeader.StartCode, H261PictureHeader.StartCodeLength);
    this.Bits(temporalReference, 5);
    this.Bits(0, 3);                          // split screen, document camera, freeze release
    this.Bits(isCif ? 1 : 0, 1);
    this.Bits(requestStillImage ? 0 : 1, 1);  // HI_RES: "0" requests Annex D, "1" is ordinary video
    this.Bits(0, 1);                          // spare
    this.Bits(0, 1);                          // PEI
    return this;
  }

  /// <summary>A group of blocks header, mandatory before every group whether or not it carries macroblocks.</summary>
  internal H261TestStream GroupHeader(int groupNumber, int quantiser) {
    this.Bits(H261PictureHeader.GroupStartCode, H261PictureHeader.GroupStartCodeLength);
    this.Bits(groupNumber, 4);
    this.Bits(quantiser, 5);
    this.Bits(0, 1);                          // GEI
    return this;
  }

  // --------------------------------------------------------------------------------------------
  // Macroblock layer — ITU-T H.261, 4.2.3
  // --------------------------------------------------------------------------------------------

  internal const string MbaStuffing = "0000 0001 111";

  internal const string TypeIntra = "0001";
  internal const string TypeIntraQuant = "0000 001";
  internal const string TypeInter = "1";
  internal const string TypeInterQuant = "0000 1";
  internal const string TypeInterMc = "0000 0000 1";
  internal const string TypeInterMcCoded = "0000 0001";
  internal const string TypeInterMcCodedQuant = "0000 0000 01";
  internal const string TypeInterMcFil = "001";
  internal const string TypeInterMcFilCoded = "01";
  internal const string TypeInterMcFilCodedQuant = "0000 01";

  /// <summary>The MBA code for one macroblock at this absolute address or difference (1 to 33).</summary>
  internal H261TestStream MacroblockAddress(int value)
    => this.Code(H261VlcTables.MacroblockAddress.Entries.Single(e => e.Value == value).Code);

  /// <summary>The MVD code whose first (unwrapped) value is this one, -16 to 15.</summary>
  internal H261TestStream MotionVectorComponent(int value)
    => this.Code(H261VlcTables.MotionVectorDifference.Entries.Single(e => e.Value == value).Code);

  /// <summary>The CBP code for this pattern, 1 to 63 (32*P1 + 16*P2 + 8*P3 + 4*P4 + 2*P5 + P6).</summary>
  internal H261TestStream CodedBlockPattern(int pattern)
    => this.Code(H261VlcTables.CodedBlockPattern.Entries.Single(e => e.Value == pattern).Code);

  /// <summary>An intra block: an eight-bit DC and then whichever coefficients precede end of block.</summary>
  internal H261TestStream IntraBlock(int dc, params string[] coefficientsThenEob) {
    this.Bits(dc, 8);
    foreach (var code in coefficientsThenEob)
      this.Code(code);

    return this.Code("10"); // end of block (CoefficientNotFirst)
  }

  /// <summary>
  /// One macroblock coded Intra, with six flat blocks (DC only, no AC coefficients). Chrominance
  /// defaults to 255 — reconstruction level 1024, luminance 128 — so a non-neutral luminance DC does
  /// not also tint the macroblock's colour.
  /// </summary>
  internal H261TestStream FlatIntraMacroblock(int address, int luminanceDc, int chrominanceDc = 255) => this
    .MacroblockAddress(address).Code(TypeIntra)
    .IntraBlock(luminanceDc).IntraBlock(luminanceDc).IntraBlock(luminanceDc).IntraBlock(luminanceDc)
    .IntraBlock(chrominanceDc).IntraBlock(chrominanceDc);

  /// <summary>
  /// A whole group of blocks of thirty-three flat intra macroblocks, for use as the first picture of a
  /// stream, which must code every macroblock because it has no reference to leave any of them to.
  /// </summary>
  internal H261TestStream FlatIntraGroup(int groupNumber, int quantiser, int dc) {
    this.GroupHeader(groupNumber, quantiser);
    for (var address = 1; address <= 33; ++address)
      this.FlatIntraMacroblock(1, dc); // MBA "1" means absolute address 1 the first time, then +1 each time.

    return this;
  }

  /// <summary>The escape form of TCOEFF (clause 4.2.4.1): six-bit RUN, eight-bit two's-complement LEVEL.</summary>
  internal H261TestStream EscapedCoefficient(int run, int level) {
    this.Code("0000 01");
    this.Bits(run, 6);
    this.Bits(level & 0xFF, 8);
    return this;
  }

  /// <summary>The end-of-block code, always read from CoefficientNotFirst (Table 5's footnote).</summary>
  internal H261TestStream EndOfBlock() => this.Code("10");

  /// <summary>
  /// The first TCOEFF symbol of a coded inter block, for a (RUN, LEVEL) Table 5 gives a non-escaped
  /// code to.
  /// </summary>
  internal H261TestStream FirstCoefficient(int run, int level) {
    var magnitude = Math.Abs(level);
    var code = H261VlcTables.CoefficientFirst.Entries.Single(e => e.Value == H261VlcTables.IndexOf(run, magnitude)).Code;
    this.Code(code);
    return this.Bits(level < 0 ? 1 : 0, 1);
  }

  /// <summary>A whole flat QCIF intra picture: groups 1, 3 and 5, thirty-three macroblocks each.</summary>
  internal static byte[] FlatQcifIntraPicture(int dc, int quantiser = 1) => new H261TestStream()
    .PictureHeader()
    .FlatIntraGroup(1, quantiser, dc)
    .FlatIntraGroup(3, quantiser, dc)
    .FlatIntraGroup(5, quantiser, dc)
    .ToArray();

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
