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
/// Only two sizes exist — QCIF and CIF, named directly by one bit of PTYPE (clause 3.1) — where H.263
/// names one of five formats and, past that, an entire extended header (clause 5.1.4) for anything
/// else. There is nothing here that plays that role: a source format H.261 does not define is not a
/// syntax this header can even state.
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

  /// <summary>The picture's width in pixels: 176 for QCIF, 352 for CIF.</summary>
  internal required int Width { get; init; }

  /// <summary>Whether this is a CIF picture rather than QCIF.</summary>
  internal bool IsCif => this.Width == 352;

  /// <summary>The picture's height in pixels: 144 for QCIF, 288 for CIF.</summary>
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

  /// <summary>Whether another header describes the same picture geometry as this one.</summary>
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

    var stillImageOff = reader.ReadBit() == 1;
    if (!stillImageOff)
      throw new NotSupportedException(
        "This H.261 picture sets HI_RES to 0 in PTYPE, requesting the still image transmission of Annex D: four "
        + "sub-images sub-sampled from a picture at four times this stream's resolution, sent as a sequence of "
        + "ordinary pictures whose temporal reference low bits select which sub-image each belongs to. That "
        + "reassembly is not implemented.");

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
    };
  }
}
