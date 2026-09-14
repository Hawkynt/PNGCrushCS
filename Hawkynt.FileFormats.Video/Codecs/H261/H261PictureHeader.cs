using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>The picture header of ITU-T H.261 clause 4.2.1.</summary>
/// <remarks>
/// Unlike H.263's PTYPE, H.261's carries no intra/inter bit at all — clause 3.2 puts that choice on
/// every macroblock's own MTYPE (Table 2/H.261), not on the picture as a whole. A single H.261 picture
/// may freely mix intra- and inter-coded macroblocks; only the very first picture of a stream is
/// constrained, and only because nothing exists yet for an inter macroblock to predict from, which is
/// enforced where the macroblocks are decoded and not here.
/// <para/>
/// Only two ordinary picture sizes exist — QCIF and CIF, named directly by one bit of PTYPE (clause
/// 3.1). Annex D's still-image transmission does not add a third coded size: HI_RES selects four
/// QCIF/CIF sub-images whose temporal-reference low bits identify the samples to interleave into a
/// picture twice as wide and twice as high.
/// </remarks>
internal sealed class H261PictureHeader {

  /// <summary>The twenty-bit picture start code of clause 4.2.1.1, value 0000 0000 0000 0001 0000.</summary>
  internal const int StartCode = 0b0000_0000_0000_0001_0000;

  /// <summary>How many bits <see cref="StartCode"/> occupies.</summary>
  internal const int StartCodeLength = 20;

  /// <summary>The sixteen-bit group of blocks start code of clause 4.2.2.1, value 0000 0000 0000 0001.</summary>
  internal const int GroupStartCode = 0b0000_0000_0000_0001;

  /// <summary>How many bits <see cref="GroupStartCode"/> occupies.</summary>
  internal const int GroupStartCodeLength = 16;

  /// <summary>The coded sub-picture's width in pixels: 176 for QCIF, 352 for CIF.</summary>
  internal required int Width { get; init; }

  /// <summary>Whether this is a CIF picture rather than QCIF.</summary>
  internal bool IsCif => this.Width == 352;

  /// <summary>The coded sub-picture's height in pixels: 144 for QCIF, 288 for CIF.</summary>
  internal required int Height { get; init; }

  /// <summary>Macroblocks across: eleven for QCIF, twenty-two for CIF.</summary>
  internal int MacroblockWidth => this.Width / 16;

  /// <summary>Macroblocks down: nine for QCIF, eighteen for CIF.</summary>
  internal int MacroblockHeight => this.Height / 16;

  /// <summary>
  /// How many groups of blocks the columns of the picture hold: one for QCIF, two for CIF (clause
  /// 4.2.2 and Figure 6).
  /// </summary>
  internal int GroupColumns => this.MacroblockWidth / 11;

  /// <summary>
  /// How many groups of blocks the picture holds in total: three for QCIF, twelve for CIF.
  /// </summary>
  internal int GroupCount => this.GroupColumns * (this.MacroblockHeight / 3);

  /// <summary>The picture's temporal reference, five bits (clause 4.2.1.2).</summary>
  internal required int TemporalReference { get; init; }

  /// <summary>Whether HI_RES selects Annex D still-image transmission rather than ordinary motion video.</summary>
  internal required bool IsStillImage { get; init; }

  /// <summary>Which of Annex D's four sub-images this picture carries.</summary>
  internal int StillImageSubImageIndex => this.TemporalReference & 0b11;

  /// <summary>Whether another header describes the same coded sub-picture geometry as this one.</summary>
  internal bool SameGeometryAs(H261PictureHeader other) {
    ArgumentNullException.ThrowIfNull(other);

    return this.Width == other.Width && this.Height == other.Height;
  }

  /// <summary>
  /// Reads a picture header, positioned just past the twenty-bit picture start code.
  /// </summary>
  internal static H261PictureHeader Parse(ref H263BitReader reader) {
    var temporalReference = reader.ReadBits(5);

    // PTYPE, clause 4.2.1.3. Bits 1 to 3 (split screen, document camera, freeze picture release) are
    // instructions to a display and not to this decoder, so they are read and ignored exactly as the
    // same kind of bits are in H.263's PTYPE.
    reader.ReadBits(3);

    var isCif = reader.ReadBit() == 1;
    var isStillImage = reader.ReadBit() == 0;

    // Annex D.3 gives the low two bits of TR a second meaning while HI_RES is zero and requires the
    // upper three bits to be zero. Refusing a non-canonical value here prevents four independent old
    // temporal references from being mistaken for one high-resolution still picture.
    if (isStillImage && (temporalReference & ~0b11) != 0)
      throw new InvalidDataException(
        $"This H.261 Annex D still-image sub-picture has temporal reference {temporalReference}; Annex D.3 "
        + "requires the three high bits of TR to be zero and uses only the low two bits to identify sub-images 0 to 3.");

    // Bit 6 is spare and carries nothing (clause 4.2.1.3).
    reader.ReadBit();

    while (reader.ReadBit() == 1)
      reader.ReadBits(8);

    var width = isCif ? 352 : 176;
    var height = isCif ? 288 : 144;

    return new() {
      Width = width,
      Height = height,
      TemporalReference = temporalReference,
      IsStillImage = isStillImage,
    };
  }
}
