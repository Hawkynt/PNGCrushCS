using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.RealVideo;

/// <summary>
/// The header in front of one independently coded run of a RealVideo 1 picture.
/// </summary>
/// <remarks>
/// RealVideo cuts a picture into runs and sends each in its own packet, so that losing one costs part
/// of a picture rather than all of it. Every run restates the picture's type and quantiser and says
/// which macroblock it begins at and how many it carries — which is what makes a run decodable on its
/// own, and what a decoder needs in order to find the run after it.
/// <para/>
/// The layout was derived from the streams rather than from any published description of it, and
/// checked by encoding pictures at known sizes and quantisers and reading the fields back: the
/// quantiser field returns the number the encoder was given, and the count field returns exactly the
/// picture's macroblock count for a picture sent in one run — ninety-nine for 176x144, forty-eight for
/// 128x96, three hundred for 320x240.
/// </remarks>
/// <param name="IsIntra">Whether the picture is coded without reference to another.</param>
/// <param name="Quantiser">The step size this run's macroblocks start at, 1 to 31.</param>
/// <param name="FirstMacroblock">The address of the first macroblock this run carries.</param>
/// <param name="MacroblockCount">How many macroblocks this run carries.</param>
internal readonly record struct RealVideoSliceHeader(
  bool IsIntra, int Quantiser, int FirstMacroblock, int MacroblockCount) {

  /// <summary>The width of the column and row fields, which cap a picture at sixty-four macroblocks each way.</summary>
  private const int _POSITION_BITS = 6;

  /// <summary>The width of the count field.</summary>
  private const int _COUNT_BITS = 12;

  /// <summary>
  /// The bits after the count that no stream measured here gives a meaning to.
  /// </summary>
  /// <remarks>
  /// Three of them, and they are zero in every picture of every stream measured. They are stepped over
  /// rather than checked, because a stream that put something there would be one this decoder has not
  /// been written against, and its pictures would fail on their own rather than silently.
  /// </remarks>
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
        "The marker bit opening this RealVideo picture header is zero, where every header of every stream measured "
        + "has it set. This is not a RealVideo 1 picture header.");

    var isIntra = reader.ReadBit() == 0;

    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This RealVideo 1 picture sets the bit a PB-frame is signalled by, which carries a bidirectionally predicted "
        + "picture inside the macroblocks of a predicted one. That is not implemented.");

    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "This RealVideo 1 picture states a quantiser of zero. The step size runs from 1 to 31; zero is not one and "
        + "would reconstruct every coefficient as zero.");

    // Every run states where it begins and how far it goes, and a run that begins a picture states
    // nought and nought — so twelve zero bits are what a picture's first run looks like here.
    //
    // A stream may instead leave the fields out of a first run and mean the whole picture by it, and
    // there is no field saying which of the two it is: the reference decoder decides by whether the
    // bits where a position would be could be a first position, exactly as this does. What this will
    // not do is guess at the rest of such a header. Every picture of every stream measured here writes
    // the fields, so where the bits after them sit when they are absent is unmeasured, and a header
    // read one bit wrong decodes to noise that looks like a picture rather than to an error.
    if (!isContinuation && reader.NextBits(_POSITION_BITS + _POSITION_BITS) != 0)
      throw new NotSupportedException(
        "This RealVideo 1 picture leaves the macroblock position out of its first run and means the whole picture by "
        + "it. No stream measured here does that — every one states the position, even to say nought — so how many "
        + "bits follow it in that form has never been checked against a reference decoder, and reading it wrongly "
        + "would produce noise shaped like a picture rather than an error.");

    var column = reader.ReadBits(_POSITION_BITS);
    var row = reader.ReadBits(_POSITION_BITS);
    var count = reader.ReadBits(_COUNT_BITS);
    reader.Skip(_TRAILING_BITS);

    if (column >= macroblockWidth)
      throw new InvalidDataException(
        $"This RealVideo 1 picture header states that its run begins at macroblock column {column} of a picture "
        + $"{macroblockWidth} macroblock(s) across.");

    var first = (row * macroblockWidth) + column;
    if (count <= 0 || first > macroblockCount - count)
      throw new InvalidDataException(
        $"This RealVideo 1 picture header states a run of {count} macroblock(s) beginning at {first}, which does not "
        + $"fit in a picture of {macroblockCount}.");

    return new(isIntra, quantiser, first, count);
  }
}
