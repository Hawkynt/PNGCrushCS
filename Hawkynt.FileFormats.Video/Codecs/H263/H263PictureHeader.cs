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
/// </remarks>
internal sealed class H263PictureHeader {

  internal required int Width { get; init; }
  internal required int Height { get; init; }
  internal int MacroblockWidth => (this.Width + 15) / 16;
  internal int MacroblockHeight => (this.Height + 15) / 16;
  internal required int MacroblockRowsPerGroup { get; init; }
  internal required bool IsIntra { get; init; }
  internal required bool IsReference { get; init; }
  internal required int Quantiser { get; init; }
  internal required bool HasWideEscapeLevel { get; init; }
  internal required bool HasGroupLayer { get; init; }

  /// <summary>Whether motion compensation may extend the reference picture by repeating edge samples.</summary>
  internal required bool AllowsVectorsOutsidePicture { get; init; }

  /// <summary>Whether Annex D.2's extended motion-vector component range is active.</summary>
  internal bool UsesExtendedMotionVectorRange { get; init; }

  /// <summary>Whether Annex F four-vector prediction and overlapped motion compensation are active.</summary>
  internal bool UsesAdvancedPrediction { get; init; }

  internal required int TemporalReference { get; init; }

  internal bool SameGeometryAs(H263PictureHeader other) {
    ArgumentNullException.ThrowIfNull(other);
    return this.Width == other.Width && this.Height == other.Height;
  }

  // ============================================================================================
  // ITU-T H.263, 5.1
  // ============================================================================================

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

    _ = reader.ReadBit(); // split screen
    _ = reader.ReadBit(); // document camera
    _ = reader.ReadBit(); // freeze picture release
    var sourceFormat = reader.ReadBits(3);

    if (sourceFormat == 7)
      throw new NotSupportedException(
        "This H.263 picture header states source format 111, the extended PTYPE of ITU-T H.263 5.1.4. The extended "
        + "header carries custom picture formats and later optional modes that are not implemented by this parser.");

    var (width, height, rowsPerGroup) = _StandardFormat(sourceFormat);

    var isIntra = reader.ReadBit() == 0;
    var unrestrictedMotionVectors = reader.ReadBit() == 1;
    var arithmeticCoding = reader.ReadBit() == 1;
    var advancedPrediction = reader.ReadBit() == 1;
    var pbFrames = reader.ReadBit() == 1;

    // The shared picture decoder below implements these two modes for H.263-derived codecs such as
    // RV10. The public baseline-H.263 parser deliberately keeps its existing advertised scope in this
    // PR; widening that surface needs its own corpus/oracle pass rather than piggy-backing on RV10.
    if (unrestrictedMotionVectors)
      throw new NotSupportedException(
        "This H.263 picture uses the Unrestricted Motion Vector mode of ITU-T H.263 Annex D (PTYPE bit 10). "
        + "The shared motion engine supports its reconstruction rules, but baseline-H.263 Annex-D streams are not "
        + "enabled by this parser yet.");

    if (arithmeticCoding)
      throw new NotSupportedException(
        "This H.263 picture uses the Syntax-based Arithmetic Coding mode of ITU-T H.263 Annex E (PTYPE bit 11). Every "
        + "variable-length code in the picture is replaced by an arithmetic-coded symbol, which is not implemented.");

    if (advancedPrediction)
      throw new NotSupportedException(
        "This H.263 picture uses the Advanced Prediction mode of ITU-T H.263 Annex F (PTYPE bit 12). The shared "
        + "picture decoder implements four-vector prediction and OBMC for H.263-derived codecs, but Annex-F H.263 "
        + "streams are not enabled by this baseline parser yet.");

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

  private static int _GroupRows(int height) => height <= 400 ? 1 : height <= 800 ? 2 : 4;

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

    _ = reader.ReadBit(); // deblocking/display flag
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
