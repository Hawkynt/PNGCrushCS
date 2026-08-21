using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The picture header of Microsoft's MPEG-4 version 2: seven bits, and then five more or one.
/// </summary>
/// <remarks>
/// There is no start code, no sequence header and no layer header anywhere in the bitstream. A packet
/// is a picture and the picture begins at its first bit, which is why this decoder takes the picture
/// size from the container and the MPEG-4 Part 2 decoder refuses to. Everything ISO/IEC 14496-2 states
/// once per layer — the quantiser precision, the chrominance format, the sample depth, which
/// inverse quantisation method, whether vectors are to a quarter of a sample — is fixed here rather
/// than signalled, so there is nothing to read and nothing to refuse.
/// <para/>
/// The fields and their order are from Michael Niedermayer's <i>DIVX3 / MS-MPEG4v1-v3 / WMV7-8</i>
/// 0.07 (GNU Free Documentation Licence), and each was checked against encoded streams: the picture
/// type by which pictures a group of pictures starts with, the quantiser by encoding the same content
/// at every quantiser from 1 to 31 and watching those five bits count, and the slice count by asking
/// for more than one slice.
/// </remarks>
internal sealed class MsMpeg4PictureHeader {

  /// <summary>Intra coded: decodable on its own.</summary>
  internal const int IntraCoded = 0;

  /// <summary>Predicted from the picture before it. There are no other kinds — the format has no B pictures.</summary>
  internal const int PredictiveCoded = 1;

  /// <summary>
  /// What a slice count of one looks like on the wire.
  /// </summary>
  /// <remarks>
  /// The field is the number of slices plus twenty-two. Version 1 puts the height of a slice here
  /// instead, which is the one place the two versions disagree about a field they both have.
  /// </remarks>
  private const int _SLICE_COUNT_BIAS = 0x16;

  /// <summary>Which of I or P this picture is.</summary>
  internal required int CodingType { get; init; }

  /// <summary>The quantiser the whole picture uses; the macroblock layer cannot change it.</summary>
  internal required int Quantiser { get; init; }

  /// <summary>
  /// How many macroblock rows a slice holds, or the height of the picture where there is one slice.
  /// </summary>
  /// <remarks>
  /// Stated only by an intra picture, and it holds until the next one: the predicted pictures after it
  /// carry no slice field and are divided the same way. Prediction of every kind stops at a slice
  /// boundary — the vectors, the DC and the alternating current coefficients — so a decoder that
  /// forgot the count between pictures would predict a predicted picture's first row of every slice
  /// but the first from macroblocks the encoder treated as absent.
  /// </remarks>
  internal required int SliceHeight { get; init; }

  /// <summary>Whether each macroblock of a predicted picture carries a bit saying it is skipped.</summary>
  /// <remarks>
  /// When it is clear no macroblock is skipped and no bit is spent saying so, which is what a picture
  /// where everything moves looks like. An intra picture has no such flag because none of its
  /// macroblocks may be skipped.
  /// </remarks>
  internal required bool SkipBitsArePresent { get; init; }

  /// <summary>
  /// Reads a picture header from the first bit of a packet.
  /// </summary>
  /// <param name="reader">The bitstream, positioned at the very start of the picture.</param>
  /// <param name="macroblockHeight">The picture's height in macroblocks, which sizes a slice.</param>
  /// <param name="previousSliceHeight">
  /// What the last intra picture said, for a predicted picture that does not say.
  /// </param>
  internal static MsMpeg4PictureHeader Parse(
    ref Mpeg4BitReader reader, int macroblockHeight, int previousSliceHeight) {
    var codingType = reader.ReadBits(2);
    if (codingType > PredictiveCoded)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 version 2 picture states picture type {codingType}. The format has only two, the "
        + "intra coded picture and the predicted one, so a packet stating anything else is not a picture of this "
        + "codec — most likely the stream is one of the other Microsoft variants under a four-character code this "
        + "decoder was handed by mistake.");

    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "This Microsoft MPEG-4 version 2 picture states a quantiser of zero, which is not a step size and would "
        + "reconstruct every coefficient as zero. The field holds 1 to 31.");

    if (codingType != IntraCoded)
      return new() {
        CodingType = codingType,
        Quantiser = quantiser,
        SliceHeight = previousSliceHeight,
        SkipBitsArePresent = reader.ReadBit() == 1,
      };

    var sliceCode = reader.ReadBits(5);
    var slices = sliceCode - _SLICE_COUNT_BIAS;
    if (slices < 1 || slices > macroblockHeight)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 version 2 intra picture states a slice field of {sliceCode}, which is "
        + $"{slices} slices once the bias of {_SLICE_COUNT_BIAS} is taken off. A picture {macroblockHeight} "
        + "macroblocks tall cannot be divided into that many, and a slice is always a whole number of macroblock "
        + "rows.");

    return new() {
      CodingType = IntraCoded,
      Quantiser = quantiser,
      // Divided as evenly as whole rows allow, with the last slice taking whatever is left over.
      SliceHeight = (macroblockHeight + slices - 1) / slices,
      SkipBitsArePresent = false,
    };
  }
}
