using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// The picture header of ITU-T H.263 clause 5.1, and the different one a Sorenson Spark stream
/// carries in its place.
/// </summary>
/// <remarks>
/// One type for both, because the two disagree only about the header. Everything after it — the group
/// of blocks layer, the macroblock layer, the block layer, the quantisation and the prediction — is
/// H.263's, which is why a Flash Video stream is decodable by an H.263 decoder at all and why
/// splitting the two apart here would mean writing the rest of the decoder twice.
/// <para/>
/// What the two headers disagree about is worth stating, because it is more than a different field
/// order. H.263 states the picture size as one of five named formats and can state anything else only
/// through the extended header of clause 5.1.4; Sorenson states it as a code that may carry an
/// arbitrary width and height in the bitstream. H.263 has one bit for the picture type; Sorenson has
/// two, the third value meaning a predicted picture that later pictures do not predict from. And
/// H.263 has five bits of mode flags that a Sorenson header does not carry at all, which is why a
/// Sorenson stream cannot ask for the annexes this decoder refuses.
/// </remarks>
internal sealed class H263PictureHeader {

  /// <summary>The picture's width in pixels, as displayed.</summary>
  internal required int Width { get; init; }

  /// <summary>The picture's height in pixels, as displayed.</summary>
  internal required int Height { get; init; }

  /// <summary>Macroblocks across, which is the width rounded up to a multiple of sixteen, over sixteen.</summary>
  internal int MacroblockWidth => (this.Width + 15) / 16;

  /// <summary>Macroblocks down.</summary>
  internal int MacroblockHeight => (this.Height + 15) / 16;

  /// <summary>
  /// How many macroblock rows one group of blocks holds (ITU-T H.263, Table 5).
  /// </summary>
  /// <remarks>
  /// One for every format up to CIF, two for 4CIF and four for 16CIF, which is what keeps the group
  /// number inside the five bits the start code has for it whatever the picture size.
  /// </remarks>
  internal required int MacroblockRowsPerGroup { get; init; }

  /// <summary>Whether the picture is intra coded, and so decodable without a reference.</summary>
  internal required bool IsIntra { get; init; }

  /// <summary>
  /// Whether later pictures may predict from this one.
  /// </summary>
  /// <remarks>
  /// Always true in H.263, where every picture that is not a B-part of a PB-frame is a reference.
  /// A Sorenson Spark stream may state a third picture type meaning a predicted picture that nothing
  /// predicts from, which a decoder must show and must not keep — keeping it puts every following
  /// picture one prediction out of step, and the error is a smear that grows rather than a frame that
  /// is obviously wrong.
  /// </remarks>
  internal required bool IsReference { get; init; }

  /// <summary>QUANT for the first group of blocks: half the quantiser step size, 1 to 31.</summary>
  internal required int Quantiser { get; init; }

  /// <summary>
  /// Whether the escape form of the coefficient codes carries a wider level than H.263's.
  /// </summary>
  /// <remarks>
  /// False for every ITU-T H.263 stream, where the escaped level is the eight bits of clause 5.4.2.
  /// True for a Sorenson Spark stream that states version 1, which widens it. The two are the same
  /// length up to the level, so a decoder that reads the wrong one stays in step for exactly as long
  /// as no block needs an escape and then loses the bitstream entirely — which is why this is settled
  /// by the header rather than guessed at from what decodes.
  /// </remarks>
  internal required bool HasWideEscapeLevel { get; init; }

  /// <summary>
  /// Whether the picture has a group of blocks layer at all.
  /// </summary>
  /// <remarks>
  /// True for every ITU-T H.263 picture and false for every Sorenson Spark one, which drops the
  /// layer: its macroblocks run without a break from the picture header to the end of the packet, so
  /// there is no group header to look for and no group boundary for the prediction rules to treat as
  /// an edge. Looking for one anyway would be worse than pointless — the sixteen zero bits a group
  /// header begins with can occur inside Sorenson macroblock data, because nothing in that stream has
  /// to avoid producing them.
  /// </remarks>
  internal required bool HasGroupLayer { get; init; }

  /// <summary>
  /// Whether a motion vector may reach outside the reference picture, reading the edge sample where
  /// it does (ITU-T H.263 Annex D.1).
  /// </summary>
  /// <remarks>
  /// Off for a baseline ITU-T picture, where clause 6.1.1 requires every referenced sample to lie
  /// inside the coded picture, and a vector that reaches outside is a bitstream this decoder has
  /// misread. Always on for a Sorenson Spark picture, whose format has no bit to turn it off with.
  /// <para/>
  /// This is the edge rule of Annex D.1 and not the wider vector range of D.2: the vectors are still
  /// reconstructed into -16 to 15.5 as clause 6.1.1 does it. The two are separable and Sorenson takes
  /// only the first — a stream that used the second would decode to a picture that tears along the
  /// blocks whose vectors left that range, which none of the streams measured here did.
  /// </remarks>
  internal required bool AllowsVectorsOutsidePicture { get; init; }

  /// <summary>The picture's temporal reference, which the container's timestamps do not replace.</summary>
  internal required int TemporalReference { get; init; }

  /// <summary>Whether another header describes the same picture geometry as this one.</summary>
  internal bool SameGeometryAs(H263PictureHeader other) {
    ArgumentNullException.ThrowIfNull(other);

    return this.Width == other.Width && this.Height == other.Height;
  }

  // ============================================================================================
  // ITU-T H.263, 5.1
  // ============================================================================================

  /// <summary>
  /// Reads an H.263 picture header, positioned just past the seventeen-bit start code and its
  /// five-bit group number.
  /// </summary>
  internal static H263PictureHeader Parse(ref H263BitReader reader) {
    var temporalReference = reader.ReadBits(8);

    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "Bit 1 of PTYPE in this H.263 picture header is zero. ITU-T H.263 5.1.3 fixes it at one so that a picture "
        + "header cannot be mistaken for a start code, so this is not an H.263 picture header.");

    if (reader.ReadBit() != 0)
      throw new InvalidDataException(
        "Bit 2 of PTYPE in this H.263 picture header is one. ITU-T H.263 5.1.3 fixes it at zero to distinguish H.263 "
        + "from H.261, so this is not an H.263 picture header.");

    var splitScreen = reader.ReadBit();
    var documentCamera = reader.ReadBit();
    var freezeRelease = reader.ReadBit();
    var sourceFormat = reader.ReadBits(3);

    // Bits 3 to 5 say nothing about how a sample is coded — they are instructions to a display about
    // what to do with the pictures — so they are read and not acted on. That is deliberate rather
    // than an omission: acting on them would mean this decoder deciding to hand back half a picture.
    _ = splitScreen;
    _ = documentCamera;
    _ = freezeRelease;

    if (sourceFormat == 7)
      throw new NotSupportedException(
        "This H.263 picture header states source format 111, the extended PTYPE of ITU-T H.263 5.1.4. The extended "
        + "header carries the custom picture formats, the picture and clock conversion factors, and the annexes "
        + "signalled by OPPTYPE and MPPTYPE; none of it is implemented. This decoder reads the five standard formats "
        + "of Table 5.");

    var (width, height, rowsPerGroup) = _StandardFormat(sourceFormat);

    var isIntra = reader.ReadBit() == 0;
    var unrestrictedMotionVectors = reader.ReadBit() == 1;
    var arithmeticCoding = reader.ReadBit() == 1;
    var advancedPrediction = reader.ReadBit() == 1;
    var pbFrames = reader.ReadBit() == 1;

    if (unrestrictedMotionVectors)
      throw new NotSupportedException(
        "This H.263 picture uses the Unrestricted Motion Vector mode of ITU-T H.263 Annex D (PTYPE bit 10). Its "
        + "vectors may point outside the picture and are coded over a wider range with a different table, neither of "
        + "which is implemented.");

    if (arithmeticCoding)
      throw new NotSupportedException(
        "This H.263 picture uses the Syntax-based Arithmetic Coding mode of ITU-T H.263 Annex E (PTYPE bit 11). Every "
        + "variable-length code in the picture is replaced by an arithmetic-coded symbol, which is not implemented.");

    if (advancedPrediction)
      throw new NotSupportedException(
        "This H.263 picture uses the Advanced Prediction mode of ITU-T H.263 Annex F (PTYPE bit 12): four motion "
        + "vectors per macroblock and overlapped block motion compensation. Neither is implemented.");

    if (pbFrames)
      throw new NotSupportedException(
        "This H.263 picture is a PB-frame (ITU-T H.263 Annex G, PTYPE bit 13), which carries a bidirectionally "
        + "predicted picture inside the macroblocks of a predicted one. That is not implemented.");

    var quantiser = _ReadQuantiser(ref reader, "PQUANT");

    var continuousPresenceMultipoint = reader.ReadBit() == 1;
    if (continuousPresenceMultipoint)
      throw new NotSupportedException(
        "This H.263 picture sets CPM (ITU-T H.263 5.1.20), the Continuous Presence Multipoint mode of Annex C, in "
        + "which the picture is one of four independently coded sub-bitstreams identified by PSBI. That is not "
        + "implemented.");

    // PEI and PSUPP: bytes the Recommendation gives no meaning to, each introduced by a set bit.
    while (reader.ReadBit() == 1)
      reader.ReadBits(8);

    return new() {
      Width = width,
      Height = height,
      MacroblockRowsPerGroup = rowsPerGroup,
      IsIntra = isIntra,
      IsReference = true,
      Quantiser = quantiser,
      HasWideEscapeLevel = false,
      HasGroupLayer = true,
      AllowsVectorsOutsidePicture = false,
      TemporalReference = temporalReference,
    };
  }

  /// <summary>
  /// How many macroblock rows one group of blocks holds, which ITU-T H.263 4.2.1 and Table 4 make a
  /// function of the picture's height alone.
  /// </summary>
  /// <remarks>
  /// Of the height and not of the source format, because a Sorenson Spark picture states a height
  /// without stating a format. The five standard formats fall out of the same rule — ninety-six, a
  /// hundred and forty-four and two hundred and eighty-eight lines are all at or under four hundred,
  /// five hundred and seventy-six is in the middle band and one thousand one hundred and fifty-two is
  /// in the last — so there is one rule here rather than a table and an exception.
  /// </remarks>
  private static int _GroupRows(int height) => height <= 400 ? 1 : height <= 800 ? 2 : 4;

  /// <summary>The five picture formats of ITU-T H.263 Table 5.</summary>
  private static (int Width, int Height, int RowsPerGroup) _StandardFormat(int sourceFormat) => sourceFormat switch {
    1 => (128, 96, _GroupRows(96)),
    2 => (176, 144, _GroupRows(144)),
    3 => (352, 288, _GroupRows(288)),
    4 => (704, 576, _GroupRows(576)),
    5 => (1408, 1152, _GroupRows(1152)),
    0 => throw new InvalidDataException(
      "This H.263 picture header states source format 000, which ITU-T H.263 5.1.3 forbids."),
    _ => throw new InvalidDataException(
      $"This H.263 picture header states source format {Convert.ToString(sourceFormat, 2).PadLeft(3, '0')}, which "
      + "ITU-T H.263 5.1.3 reserves."),
  };

  // ============================================================================================
  // Sorenson Spark
  // ============================================================================================

  /// <summary>
  /// Reads the picture header of a Sorenson Spark stream, positioned just past the seventeen-bit
  /// start code.
  /// </summary>
  /// <remarks>
  /// Positioned past seventeen bits and not past twenty-two, because a Sorenson header has no group
  /// number: the five bits that would be one carry a version instead, and they are read here.
  /// </remarks>
  internal static H263PictureHeader ParseSorenson(ref H263BitReader reader) {
    var version = reader.ReadBits(5);
    if (version > 1)
      throw new NotSupportedException(
        $"This Sorenson Spark picture states version {version}. Only versions 0 and 1 are defined; a later one would "
        + "be a bitstream this decoder has not been written against.");

    var temporalReference = reader.ReadBits(8);
    var sizeCode = reader.ReadBits(3);

    var (width, height) = sizeCode switch {
      0 => (reader.ReadBits(8), reader.ReadBits(8)),
      1 => (reader.ReadBits(16), reader.ReadBits(16)),
      2 => (352, 288),
      3 => (176, 144),
      4 => (128, 96),
      5 => (320, 240),
      6 => (160, 120),
      _ => throw new InvalidDataException(
        "This Sorenson Spark picture states picture size code 7, which is reserved."),
    };

    if (width <= 0 || height <= 0)
      throw new InvalidDataException(
        $"This Sorenson Spark picture states a size of {width}x{height}, and neither dimension may be zero.");

    var pictureType = reader.ReadBits(2);
    var (isIntra, isReference) = pictureType switch {
      0 => (true, true),
      1 => (false, true),
      2 => (false, false),
      _ => throw new InvalidDataException(
        "This Sorenson Spark picture states picture type 3, which is reserved. Types 0 (intra), 1 (inter) and "
        + "2 (disposable inter) are the ones defined."),
    };

    // The deblocking flag asks whoever shows the picture to smooth its block edges first. It is a
    // request about the displayed picture and not about the decode: the pictures a Sorenson stream
    // predicts from are the unfiltered ones, which is why a decoder that ignores it stays in step
    // with the encoder rather than drifting away from it. It is read and not acted on, so the
    // pictures this hands back are the reconstructed ones and have not been smoothed. Every stream
    // measured here had the flag set, and every picture matched the reference decoder's — which also
    // does not filter — sample for sample.
    reader.ReadBit();

    var quantiser = _ReadQuantiser(ref reader, "the Sorenson Spark quantiser");

    while (reader.ReadBit() == 1)
      reader.ReadBits(8);

    return new() {
      Width = width,
      Height = height,
      MacroblockRowsPerGroup = _GroupRows(height),
      IsIntra = isIntra,
      IsReference = isReference,
      Quantiser = quantiser,
      HasWideEscapeLevel = version == 1,
      HasGroupLayer = false,
      AllowsVectorsOutsidePicture = true,
      TemporalReference = temporalReference,
    };
  }

  private static int _ReadQuantiser(ref H263BitReader reader, string field) {
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        $"An H.263 picture states {field} 0. ITU-T H.263 5.1.19 gives QUANT the range 1 to 31; zero is not a step "
        + "size and would reconstruct every coefficient as zero.");

    return quantiser;
  }
}
