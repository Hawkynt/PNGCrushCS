using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.RealVideo;

/// <summary>
/// The header in front of one independently coded run of a RealVideo 1 picture.
/// </summary>
/// <remarks>
/// RealVideo cuts a picture into runs and sends each in its own packet, so that losing one costs part
/// of a picture rather than all of it. Every run restates the picture's type and quantiser. An
/// explicitly positioned run additionally names the macroblock it begins at and how many it carries;
/// the first run may instead omit that position/count pair and thereby mean the whole picture. Later
/// RV10 micro revisions also seed the predictive intra-DC state in each intra run.
/// </remarks>
/// <param name="IsIntra">Whether the picture is coded without reference to another.</param>
/// <param name="Quantiser">The step size this run's macroblocks start at, 1 to 31.</param>
/// <param name="FirstMacroblock">The address of the first macroblock this run carries.</param>
/// <param name="MacroblockCount">How many macroblocks this run carries.</param>
/// <param name="LumaDc">Initial luma DC predictor, or -1 when literal H.263 INTRADC is used.</param>
/// <param name="CbDc">Initial Cb DC predictor, or -1 when literal H.263 INTRADC is used.</param>
/// <param name="CrDc">Initial Cr DC predictor, or -1 when literal H.263 INTRADC is used.</param>
internal readonly record struct RealVideoSliceHeader(
  bool IsIntra, int Quantiser, int FirstMacroblock, int MacroblockCount,
  int LumaDc, int CbDc, int CrDc) {

  /// <summary>Whether this run carries RealVideo-specific predictive intra DC values.</summary>
  internal bool HasPredictiveIntraDc => this.LumaDc >= 0;

  /// <summary>The width of the column and row fields, which cap a picture at sixty-four macroblocks each way.</summary>
  private const int _POSITION_BITS = 6;

  /// <summary>The width of the count field.</summary>
  private const int _COUNT_BITS = 12;

  /// <summary>The three reserved bits terminating every measured RV10 run header.</summary>
  private const int _TRAILING_BITS = 3;

  /// <summary>
  /// Reads one run's header.
  /// </summary>
  /// <param name="reader">Positioned at the run's first bit, which is a byte boundary.</param>
  /// <param name="version">What the stream's private data said about how its pictures are coded.</param>
  /// <param name="macroblockWidth">How many macroblocks the picture is across.</param>
  /// <param name="macroblockCount">How many macroblocks the whole picture holds.</param>
  /// <param name="isContinuation">Whether a run of this picture has already been decoded.</param>
  internal static RealVideoSliceHeader Read(
    ref H263BitReader reader, RealVideoBitstreamVersion version,
    int macroblockWidth, int macroblockCount, bool isContinuation) {
    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "The marker bit opening this RealVideo picture header is zero, where a RealVideo 1 run header requires one.");

    var isIntra = reader.ReadBit() == 0;

    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This RealVideo 1 picture sets its PB-frame bit. RV10 carries the B picture inside a P picture rather than "
        + "as a standalone B frame, and the RealVideo-specific PB timing/prediction syntax is not implemented.");

    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "This RealVideo 1 picture states a quantiser of zero. The step size runs from 1 to 31; zero is not one and "
        + "would reconstruct every coefficient as zero.");

    var lumaDc = -1;
    var cbDc = -1;
    var crDc = -1;
    if (isIntra && version.UsesPredictiveIntraDc) {
      lumaDc = reader.ReadBits(8);
      cbDc = reader.ReadBits(8);
      crDc = reader.ReadBits(8);
    }

    int first, count;

    // RV10 has no flag saying whether the first run carries a position. Its reference decoder uses
    // the twelve bits that an explicit (0,0) position would occupy as the discriminator: twelve
    // zeroes mean the fields are present; anything else means that the first run implicitly covers
    // the whole picture. Continuation runs always carry their position and count.
    if (!isContinuation && reader.NextBits(_POSITION_BITS + _POSITION_BITS) != 0) {
      first = 0;
      count = macroblockCount;
    } else {
      var column = reader.ReadBits(_POSITION_BITS);
      var row = reader.ReadBits(_POSITION_BITS);
      count = reader.ReadBits(_COUNT_BITS);

      if (column >= macroblockWidth)
        throw new InvalidDataException(
          $"This RealVideo 1 picture header states that its run begins at macroblock column {column} of a picture "
          + $"{macroblockWidth} macroblock(s) across.");

      first = (row * macroblockWidth) + column;
      if (count <= 0 || first > macroblockCount - count)
        throw new InvalidDataException(
          $"This RealVideo 1 picture header states a run of {count} macroblock(s) beginning at {first}, which does not "
          + $"fit in a picture of {macroblockCount}.");
    }

    reader.Skip(_TRAILING_BITS);

    return new(isIntra, quantiser, first, count, lumaDc, cbDc, crDc);
  }
}