using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>The picture header of ITU-T H.263, including PLUSPTYPE, and the Sorenson Spark variant.</summary>
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

  internal required bool AllowsVectorsOutsidePicture { get; init; }

  internal required int TemporalReference { get; init; }

  /// <summary>The picture code type from baseline PTYPE or MPPTYPE.</summary>
  internal H263PictureKind PictureKind { get; init; } = H263PictureKind.Intra;

  /// <summary>RCONTROL used by half-pixel interpolation. Baseline and B-pictures use zero.</summary>
  internal int RoundingType { get; init; }

  /// <summary>The scalability layer of this picture. The base layer is one.</summary>
  internal int EnhancementLayerNumber { get; init; } = 1;

  /// <summary>The reference layer named by an enhancement picture.</summary>
  internal int ReferenceLayerNumber { get; init; } = 1;

  internal bool IsBidirectional => this.PictureKind == H263PictureKind.Bidirectional;

  internal bool SameGeometryAs(H263PictureHeader other) {
    ArgumentNullException.ThrowIfNull(other);
    return this.Width == other.Width && this.Height == other.Height;
  }

  // ============================================================================================
  // ITU-T H.263, 5.1
  // ============================================================================================

  /// <summary>
  /// Reads an H.263 picture header, positioned immediately after PSC and GN=0.
  /// </summary>
  internal static H263PictureHeader Parse(ref H263BitReader reader) {
    var temporalReference = reader.ReadBits(8);

    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "Bit 1 of PTYPE in this H.263 picture header is zero; ITU-T H.263 5.1.3 fixes it at one.");
    if (reader.ReadBit() != 0)
      throw new InvalidDataException(
        "Bit 2 of PTYPE in this H.263 picture header is one; ITU-T H.263 5.1.3 fixes it at zero to distinguish H.263 from H.261.");

    // Split-screen, document-camera and freeze-release are display instructions, not coding tools.
    reader.ReadBits(3);
    var sourceFormat = reader.ReadBits(3);

    if (sourceFormat == 7)
      return _ParseExtended(ref reader, temporalReference);

    var (width, height, rowsPerGroup) = _StandardFormat(sourceFormat);
    var isIntra = reader.ReadBit() == 0;
    var unrestrictedMotionVectors = reader.ReadBit() == 1;
    var arithmeticCoding = reader.ReadBit() == 1;
    var advancedPrediction = reader.ReadBit() == 1;
    var pbFrames = reader.ReadBit() == 1;

    if (unrestrictedMotionVectors)
      throw new NotSupportedException(
        "This H.263 picture uses the Unrestricted Motion Vector mode of Annex D, which is not implemented for baseline P-pictures.");
    if (arithmeticCoding)
      throw new NotSupportedException(
        "This H.263 picture uses Syntax-based Arithmetic Coding (Annex E), which is not implemented.");
    if (advancedPrediction)
      throw new NotSupportedException(
        "This H.263 picture uses Advanced Prediction (Annex F), including four-vector macroblocks and OBMC, which is not implemented.");
    if (pbFrames)
      throw new NotSupportedException(
        "This H.263 picture is a PB-frame (Annex G). Separate Annex O B-pictures are supported, but the combined PB macroblock syntax is not.");

    var quantiser = _ReadQuantiser(ref reader, "PQUANT");
    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This H.263 picture sets CPM, the Continuous Presence Multipoint mode of Annex C, which is not implemented.");

    _ReadExtraPictureInformation(ref reader);

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
      PictureKind = isIntra ? H263PictureKind.Intra : H263PictureKind.Predicted,
      RoundingType = 0,
    };
  }

  /// <summary>Reads PLUSPTYPE and the fields controlled by the subset of it this codec implements.</summary>
  private static H263PictureHeader _ParseExtended(ref H263BitReader reader, int temporalReference) {
    var ufep = reader.ReadBits(3);
    if (ufep != 1)
      throw new NotSupportedException(
        $"This H.263+ picture states UFEP={Convert.ToString(ufep, 2).PadLeft(3, '0')}. This decoder requires UFEP=001 so the optional PLUSPTYPE state is present in the picture itself; carrying UFEP=000 state across packets is not implemented.");

    var sourceFormat = reader.ReadBits(3);
    if (sourceFormat is 0 or 7)
      throw new InvalidDataException(
        $"This H.263+ OPPTYPE states reserved source format {Convert.ToString(sourceFormat, 2).PadLeft(3, '0')}.");

    var customPictureClock = reader.ReadBit() == 1;
    var unrestrictedMotionVectors = reader.ReadBit() == 1;
    var arithmeticCoding = reader.ReadBit() == 1;
    var advancedPrediction = reader.ReadBit() == 1;
    var advancedIntraCoding = reader.ReadBit() == 1;
    var deblockingFilter = reader.ReadBit() == 1;
    var sliceStructured = reader.ReadBit() == 1;
    var referencePictureSelection = reader.ReadBit() == 1;
    var independentSegmentDecoding = reader.ReadBit() == 1;
    var alternativeInterVlc = reader.ReadBit() == 1;
    var modifiedQuantisation = reader.ReadBit() == 1;

    if (reader.ReadBit() != 1 || reader.ReadBits(3) != 0)
      throw new InvalidDataException("This H.263+ OPPTYPE has an invalid marker or reserved bit.");

    if (customPictureClock)
      throw new NotSupportedException(
        "This H.263+ picture uses a custom picture clock frequency (CPCFC/ETR), which is not implemented.");
    if (unrestrictedMotionVectors)
      throw new NotSupportedException(
        "This H.263+ picture enables Unrestricted Motion Vectors (Annex D). Annex O B-pictures apply their own edge rule, but Annex D P-picture vector syntax is not implemented.");
    if (arithmeticCoding)
      throw new NotSupportedException("This H.263+ picture enables Syntax-based Arithmetic Coding (Annex E), which is not implemented.");
    if (advancedPrediction)
      throw new NotSupportedException("This H.263+ picture enables Advanced Prediction (Annex F), which is not implemented.");
    if (advancedIntraCoding)
      throw new NotSupportedException("This H.263+ picture enables Advanced Intra Coding (Annex I), which is not implemented.");
    if (deblockingFilter)
      throw new NotSupportedException("This H.263+ picture enables the Deblocking Filter mode (Annex J), which is not implemented.");
    if (sliceStructured)
      throw new NotSupportedException("This H.263+ picture enables Slice Structured mode (Annex K), which is not implemented.");
    if (referencePictureSelection)
      throw new NotSupportedException("This H.263+ picture enables Reference Picture Selection (Annex N), which is not implemented.");
    if (independentSegmentDecoding)
      throw new NotSupportedException("This H.263+ picture enables Independent Segment Decoding (Annex R), which is not implemented.");
    if (alternativeInterVlc)
      throw new NotSupportedException("This H.263+ picture enables Alternative INTER VLC mode (Annex S), which is not implemented.");
    if (modifiedQuantisation)
      throw new NotSupportedException("This H.263+ picture enables Modified Quantization (Annex T), which is not implemented.");

    var pictureKindValue = reader.ReadBits(3);
    if (pictureKindValue > (int)H263PictureKind.EnhancementPredicted)
      throw new InvalidDataException(
        $"This H.263+ MPPTYPE states reserved picture code type {Convert.ToString(pictureKindValue, 2).PadLeft(3, '0')}.");

    var pictureKind = (H263PictureKind)pictureKindValue;
    var referencePictureResampling = reader.ReadBit() == 1;
    var reducedResolutionUpdate = reader.ReadBit() == 1;
    var roundingType = reader.ReadBit();

    if (reader.ReadBits(2) != 0 || reader.ReadBit() != 1)
      throw new InvalidDataException("This H.263+ MPPTYPE has an invalid marker or reserved bit.");

    if (referencePictureResampling)
      throw new NotSupportedException("This H.263+ picture enables Reference Picture Resampling (Annex P), which is not implemented.");
    if (reducedResolutionUpdate)
      throw new NotSupportedException("This H.263+ picture enables Reduced-Resolution Update (Annex Q), which is not implemented.");
    if (roundingType != 0 && pictureKind is not H263PictureKind.Predicted)
      throw new InvalidDataException("H.263 MPPTYPE permits RTYPE=1 only for P, Improved-PB and EP pictures.");

    switch (pictureKind) {
      case H263PictureKind.ImprovedPb:
        throw new NotSupportedException(
          "This H.263+ picture is an Improved PB-frame (Annex M). Separate Annex O B-pictures are supported, but the combined Improved-PB syntax is not.");
      case H263PictureKind.EnhancementIntra:
      case H263PictureKind.EnhancementPredicted:
        throw new NotSupportedException(
          $"This H.263+ picture is {pictureKind}, which uses spatial/SNR scalability from Annex O. Temporal Annex O B-pictures are supported; EI/EP reconstruction is not.");
    }

    // With PLUSPTYPE present, CPM is here rather than after PQUANT (5.1.4.7).
    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This H.263+ picture sets CPM, the Continuous Presence Multipoint mode of Annex C, which is not implemented.");

    int width, height, rowsPerGroup;
    if (sourceFormat == 6) {
      var pixelAspectRatio = reader.ReadBits(4);
      if (pixelAspectRatio == 0 || pixelAspectRatio is >= 6 and <= 14)
        throw new InvalidDataException($"This H.263 custom picture format uses reserved PAR code {pixelAspectRatio}.");

      width = (reader.ReadBits(9) + 1) * 4;
      if (reader.ReadBit() != 1)
        throw new InvalidDataException("The anti-emulation marker in H.263 CPFMT is zero.");
      var heightIndication = reader.ReadBits(9);
      if (heightIndication == 0)
        throw new InvalidDataException("H.263 CPFMT states picture height indication zero, which is forbidden.");
      height = heightIndication * 4;
      rowsPerGroup = _GroupRows(height);

      if (pixelAspectRatio == 15) {
        var parWidth = reader.ReadBits(8);
        var parHeight = reader.ReadBits(8);
        if (parWidth == 0 || parHeight == 0)
          throw new InvalidDataException("H.263 EPAR states a zero pixel-aspect-ratio component, which is forbidden.");
      }
    } else {
      (width, height, rowsPerGroup) = _StandardFormat(sourceFormat);
    }

    var enhancementLayerNumber = 1;
    var referenceLayerNumber = 1;
    if (pictureKind == H263PictureKind.Bidirectional) {
      enhancementLayerNumber = reader.ReadBits(4);
      referenceLayerNumber = reader.ReadBits(4);
      if (enhancementLayerNumber <= 1)
        throw new InvalidDataException(
          $"An Annex O B-picture must be in an enhancement layer above the base layer; ELNUM={enhancementLayerNumber} was stated.");
      if (referenceLayerNumber != 1)
        throw new NotSupportedException(
          $"This Annex O B-picture refers to scalability layer {referenceLayerNumber}. This decoder currently supports temporal B-pictures referencing base layer 1 only.");
    }

    var quantiser = _ReadQuantiser(ref reader, "PQUANT");
    _ReadExtraPictureInformation(ref reader);

    return new() {
      Width = width,
      Height = height,
      MacroblockRowsPerGroup = rowsPerGroup,
      IsIntra = pictureKind == H263PictureKind.Intra,
      IsReference = pictureKind != H263PictureKind.Bidirectional,
      Quantiser = quantiser,
      HasWideEscapeLevel = false,
      HasGroupLayer = true,
      AllowsVectorsOutsidePicture = pictureKind == H263PictureKind.Bidirectional,
      TemporalReference = temporalReference,
      PictureKind = pictureKind,
      RoundingType = roundingType,
      EnhancementLayerNumber = enhancementLayerNumber,
      ReferenceLayerNumber = referenceLayerNumber,
    };
  }

  private static int _GroupRows(int height) => height <= 400 ? 1 : height <= 800 ? 2 : 4;

  private static (int Width, int Height, int RowsPerGroup) _StandardFormat(int sourceFormat) => sourceFormat switch {
    1 => (128, 96, _GroupRows(96)),
    2 => (176, 144, _GroupRows(144)),
    3 => (352, 288, _GroupRows(288)),
    4 => (704, 576, _GroupRows(576)),
    5 => (1408, 1152, _GroupRows(1152)),
    0 => throw new InvalidDataException("This H.263 picture header states forbidden source format 000."),
    _ => throw new InvalidDataException(
      $"This H.263 picture header states reserved source format {Convert.ToString(sourceFormat, 2).PadLeft(3, '0')}.")
  };

  // ============================================================================================
  // Sorenson Spark
  // ============================================================================================

  internal static H263PictureHeader ParseSorenson(ref H263BitReader reader) {
    var version = reader.ReadBits(5);
    if (version > 1)
      throw new NotSupportedException($"This Sorenson Spark picture states unsupported version {version}.");

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
      _ => throw new InvalidDataException("This Sorenson Spark picture states reserved picture size code 7."),
    };

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"This Sorenson Spark picture states invalid size {width}x{height}.");

    var pictureType = reader.ReadBits(2);
    var (isIntra, isReference) = pictureType switch {
      0 => (true, true),
      1 => (false, true),
      2 => (false, false),
      _ => throw new InvalidDataException("This Sorenson Spark picture states reserved picture type 3."),
    };

    // Display-only deblocking flag.
    reader.ReadBit();
    var quantiser = _ReadQuantiser(ref reader, "the Sorenson Spark quantiser");
    _ReadExtraPictureInformation(ref reader);

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
      PictureKind = isIntra ? H263PictureKind.Intra : H263PictureKind.Predicted,
      RoundingType = 0,
    };
  }

  private static void _ReadExtraPictureInformation(ref H263BitReader reader) {
    while (reader.ReadBit() == 1)
      reader.ReadBits(8);
  }

  private static int _ReadQuantiser(ref H263BitReader reader, string field) {
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        $"An H.263 picture states {field} 0. ITU-T H.263 gives QUANT the range 1 to 31.");
    return quantiser;
  }
}
