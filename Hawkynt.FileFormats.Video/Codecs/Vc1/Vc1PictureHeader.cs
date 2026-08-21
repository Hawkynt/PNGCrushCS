using System;
using System.IO;

namespace FileFormat.Codecs.Vc1;

/// <summary>What a coded picture is, as its PTYPE says (7.1.1.4).</summary>
internal enum Vc1PictureType {

  Intra,

  Predicted,

  Bidirectional,

  Skipped,
}

/// <summary>
/// The picture layer header of a progressive Simple or Main profile picture (Figure 13, 7.1.1).
/// </summary>
/// <remarks>
/// The elements are read in the order the syntax diagram lays them out, and most of them are present
/// only on a condition stated in the sequence header — which is why the header cannot be parsed
/// without the container having carried <c>STRUCT_C</c> across.
/// <para/>
/// Several of the fields are read only to be stepped over. A frame interpolation hint, a frame count
/// and a buffer fullness are all stated to have no effect on decoding, and the motion vector range of
/// a Main profile I picture is stated to be ignored; each still occupies bits, and a reader that
/// skipped one would find every element after it at the wrong offset.
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
  int ResolutionIndex) {

  /// <summary>
  /// Table 36: what quantiser step each index means when the sequence signals the quantiser implicitly.
  /// </summary>
  /// <remarks>
  /// Not an offset, though the middle of it looks like one. Indices 1 to 8 are the step itself, 9 to 28
  /// are three less than the index, and the last three break the pattern again at 27, 29 and 31 — a
  /// reader that carried the subtraction to the end would quantise the coarsest pictures wrongly and
  /// only those.
  /// </remarks>
  private static ReadOnlySpan<byte> _ImplicitQuantiser =>
    [0, 1, 2, 3, 4, 5, 6, 7, 8, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 27, 29, 31];

  /// <summary>Table 39: the code that selects one of the three AC coding set indices.</summary>
  private static int _ReadCodingSetIndex(ref Vc1BitReader reader)
    => reader.ReadBit() == 0 ? 0 : reader.ReadBit() == 0 ? 1 : 2;

  internal static Vc1PictureHeader ReadFrom(ref Vc1BitReader reader, Vc1SequenceHeader sequence) {
    // A hint to the display about temporal interpolation, which the standard says is outside decoding.
    if (sequence.FrameInterpolation)
      reader.ReadBit();

    // A frame counter, for spotting a lost picture. It has no effect on decoding and every picture
    // header of these two profiles carries it.
    reader.ReadBits(2);

    var rangeReduced = sequence.RangeReduction && reader.ReadBit() != 0;

    var pictureType = sequence.MaxBFrames == 0
      ? reader.ReadBit() == 0 ? Vc1PictureType.Intra : Vc1PictureType.Predicted
      : reader.ReadBit() == 1
        ? Vc1PictureType.Predicted
        : reader.ReadBit() == 1
          ? Vc1PictureType.Intra
          : Vc1PictureType.Bidirectional;

    if (pictureType != Vc1PictureType.Intra)
      return new(pictureType, rangeReduced, 0, 0, false, false, 0, 0, false, 0);

    // Buffer fullness at the encoder when this picture was written, as a percentage. Present in every
    // Simple and Main profile I picture and used by none of the decoding process.
    reader.ReadBits(7);

    var quantiserIndex = reader.ReadBits(5);
    if (quantiserIndex == 0)
      throw new InvalidDataException("The picture states a quantiser index of zero, which the standard reserves.");

    // Implicit means the index says both the step and which of the two quantisers is meant; explicit
    // means the index is the step and the quantiser was stated elsewhere.
    var implicitQuantiser = sequence.Quantiser == 0;
    var quantiser = implicitQuantiser ? _ImplicitQuantiser[quantiserIndex] : quantiserIndex;

    var halfStep = quantiserIndex <= 8 && reader.ReadBit() != 0;

    // Table 259: the sequence either leaves the choice to the index, states it per picture in the bit
    // below, or fixes it for the whole sequence as nonuniform (2) or uniform (3).
    var uniform = implicitQuantiser
      ? quantiserIndex <= 8
      : sequence.Quantiser == 1
        ? reader.ReadBit() != 0
        : sequence.Quantiser == 3;

    // The motion vector range, which a Main profile I picture states and the standard says to ignore.
    // Table 37 spells it 0b, 10b, 110b and 111b, so it is never more than three bits — a run that kept
    // reading while it saw ones would swallow the first bit of the next element on the longest of them.
    if (sequence.ExtendedMotionVectors && reader.ReadBit() != 0 && reader.ReadBit() != 0)
      reader.ReadBit();

    var resolutionIndex = sequence.MultiResolution ? reader.ReadBits(2) : 0;

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
      resolutionIndex);
  }
}
