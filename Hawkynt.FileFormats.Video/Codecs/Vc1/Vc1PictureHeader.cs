using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>What a coded picture is, as its PTYPE says (7.1.1.4).</summary>
internal enum Vc1PictureType {

  Intra,

  Predicted,

  Bidirectional,

  BidirectionalIntra,

  Skipped,
}

/// <summary>The progressive motion-compensation mode selected by MVMODE.</summary>
internal enum Vc1MotionMode {

  None,

  OneMv,

  OneMvHalfPel,

  OneMvHalfPelBilinear,

  MixedMv,

  IntensityCompensation,
}

/// <summary>
/// The picture layer header of a progressive Simple or Main profile picture (Figure 13, 7.1.1).
/// </summary>
/// <remarks>
/// This type deliberately stops before the picture bitplanes. Those are followed immediately by table selectors and
/// macroblock syntax and therefore belong to the predictive-picture decoder rather than to the common header. I and BI
/// pictures have no such bitplanes, so their coding-set selectors are consumed here exactly as before.
/// </remarks>
internal readonly record struct Vc1PictureHeader(
  Vc1PictureType PictureType,
  bool RangeReduced,
  int QuantiserIndex,
  int Quantiser,
  bool HalfStep,
  bool UniformQuantiser,
  int LumaCodingSetIndex,
  int ChromaCodingSetIndex,
  bool HighMotionDcTable,
  int ResolutionIndex,
  Vc1MotionMode MotionMode = Vc1MotionMode.None,
  int MotionVectorRange = 0,
  int BFractionNumerator = 0,
  int BFractionDenominator = 0) {

  private static ReadOnlySpan<byte> _ImplicitQuantiser =>
    [0, 1, 2, 3, 4, 5, 6, 7, 8, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 27, 29, 31];

  private static int _ReadCodingSetIndex(ref Vc1BitReader reader)
    => reader.ReadBit() == 0 ? 0 : reader.ReadBit() == 0 ? 1 : 2;

  internal static int ReadCodingSetIndex(ref Vc1BitReader reader) => _ReadCodingSetIndex(ref reader);

  internal static Vc1PictureHeader ReadFrom(ref Vc1BitReader reader, Vc1SequenceHeader sequence) {
    if (sequence.FrameInterpolation)
      reader.ReadBit();

    reader.ReadBits(2); // FRMCNT is diagnostic only.
    var rangeReduced = sequence.RangeReduction && reader.ReadBit() != 0;

    var pictureType = sequence.MaxBFrames == 0
      ? reader.ReadBit() == 0 ? Vc1PictureType.Intra : Vc1PictureType.Predicted
      : reader.ReadBit() == 1
        ? Vc1PictureType.Predicted
        : reader.ReadBit() == 1
          ? Vc1PictureType.Intra
          : Vc1PictureType.Bidirectional;

    var fractionNumerator = 0;
    var fractionDenominator = 0;
    if (pictureType == Vc1PictureType.Bidirectional) {
      var fraction = _ReadBFraction(ref reader);
      if (fraction.IsBi)
        pictureType = Vc1PictureType.BidirectionalIntra;
      else {
        fractionNumerator = fraction.Numerator;
        fractionDenominator = fraction.Denominator;
      }
    }

    if (pictureType == Vc1PictureType.Intra)
      reader.ReadBits(7); // BF, encoder buffer fullness.

    var (quantiserIndex, quantiser, halfStep, uniform) = _ReadQuantiser(ref reader, sequence);
    var motionVectorRange = sequence.ExtendedMotionVectors ? _ReadMotionVectorRange(ref reader) : 0;

    var resolutionIndex = pictureType switch {
      Vc1PictureType.Intra or Vc1PictureType.Predicted when sequence.MultiResolution => reader.ReadBits(2),
      _ => 0,
    };

    if (pictureType is Vc1PictureType.Intra or Vc1PictureType.BidirectionalIntra) {
      var chromaCodingSetIndex = _ReadCodingSetIndex(ref reader);
      var lumaCodingSetIndex = _ReadCodingSetIndex(ref reader);
      var highMotionDcTable = reader.ReadBit() != 0;

      return new(
        pictureType,
        rangeReduced,
        quantiserIndex,
        quantiser,
        halfStep,
        uniform,
        lumaCodingSetIndex,
        chromaCodingSetIndex,
        highMotionDcTable,
        resolutionIndex,
        BFractionNumerator: fractionNumerator,
        BFractionDenominator: fractionDenominator);
    }

    var motionMode = pictureType == Vc1PictureType.Bidirectional
      ? reader.ReadBit() != 0 ? Vc1MotionMode.OneMv : Vc1MotionMode.OneMvHalfPelBilinear
      : _ReadPMotionMode(ref reader, quantiser);

    if (motionMode == Vc1MotionMode.IntensityCompensation) {
      _ReadPMotionMode2(ref reader, quantiser);
      reader.ReadBits(6); // LUMSCALE
      reader.ReadBits(6); // LUMSHIFT
    }

    return new(
      pictureType,
      rangeReduced,
      quantiserIndex,
      quantiser,
      halfStep,
      uniform,
      0,
      0,
      false,
      resolutionIndex,
      motionMode,
      motionVectorRange,
      fractionNumerator,
      fractionDenominator);
  }

  private static (int Index, int Quantiser, bool HalfStep, bool Uniform) _ReadQuantiser(
    ref Vc1BitReader reader,
    Vc1SequenceHeader sequence) {
    var quantiserIndex = reader.ReadBits(5);
    if (quantiserIndex == 0)
      throw new InvalidDataException("The picture states a quantiser index of zero, which the standard reserves.");

    var implicitQuantiser = sequence.Quantiser == 0;
    var quantiser = implicitQuantiser ? _ImplicitQuantiser[quantiserIndex] : quantiserIndex;
    var halfStep = quantiserIndex <= 8 && reader.ReadBit() != 0;
    var uniform = implicitQuantiser
      ? quantiserIndex <= 8
      : sequence.Quantiser == 1
        ? reader.ReadBit() != 0
        : sequence.Quantiser == 3;

    return (quantiserIndex, quantiser, halfStep, uniform);
  }

  private static int _ReadMotionVectorRange(ref Vc1BitReader reader) {
    if (reader.ReadBit() == 0)
      return 0;
    if (reader.ReadBit() == 0)
      return 1;
    return reader.ReadBit() == 0 ? 2 : 3;
  }

  private static Vc1MotionMode _ReadPMotionMode(ref Vc1BitReader reader, int quantiser) {
    if (reader.ReadBit() != 0)
      return quantiser <= 12 ? Vc1MotionMode.OneMv : Vc1MotionMode.OneMvHalfPelBilinear;
    if (reader.ReadBit() != 0)
      return quantiser <= 12 ? Vc1MotionMode.MixedMv : Vc1MotionMode.OneMv;
    if (reader.ReadBit() != 0)
      return Vc1MotionMode.OneMvHalfPel;
    return reader.ReadBit() != 0
      ? quantiser <= 12 ? Vc1MotionMode.OneMvHalfPelBilinear : Vc1MotionMode.MixedMv
      : Vc1MotionMode.IntensityCompensation;
  }

  private static Vc1MotionMode _ReadPMotionMode2(ref Vc1BitReader reader, int quantiser) {
    if (reader.ReadBit() != 0)
      return quantiser <= 12 ? Vc1MotionMode.OneMv : Vc1MotionMode.OneMvHalfPelBilinear;
    if (reader.ReadBit() != 0)
      return quantiser <= 12 ? Vc1MotionMode.MixedMv : Vc1MotionMode.OneMv;
    if (reader.ReadBit() != 0)
      return Vc1MotionMode.OneMvHalfPel;
    return quantiser <= 12 ? Vc1MotionMode.OneMvHalfPelBilinear : Vc1MotionMode.MixedMv;
  }

  private static (int Numerator, int Denominator, bool IsBi) _ReadBFraction(ref Vc1BitReader reader) {
    var shortCode = reader.ReadBits(3);
    if (shortCode < 7)
      return shortCode switch {
        0 => (1, 2, false),
        1 => (1, 3, false),
        2 => (2, 3, false),
        3 => (1, 4, false),
        4 => (3, 4, false),
        5 => (1, 5, false),
        _ => (2, 5, false),
      };

    var code = 0x70 | reader.ReadBits(4);
    return code switch {
      0x70 => (3, 5, false),
      0x71 => (4, 5, false),
      0x72 => (1, 6, false),
      0x73 => (5, 6, false),
      0x74 => (1, 7, false),
      0x75 => (2, 7, false),
      0x76 => (3, 7, false),
      0x77 => (4, 7, false),
      0x78 => (5, 7, false),
      0x79 => (6, 7, false),
      0x7A => (1, 8, false),
      0x7B => (3, 8, false),
      0x7C => (5, 8, false),
      0x7D => (7, 8, false),
      0x7E => throw new InvalidDataException("The B picture uses the reserved BFRACTION code 1111110b."),
      _ => (0, 0, true),
    };
  }
}
