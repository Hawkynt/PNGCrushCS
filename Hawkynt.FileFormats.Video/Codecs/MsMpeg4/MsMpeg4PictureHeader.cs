using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The picture header of Microsoft's MPEG-4: seven bits, and then between one and eight more.
/// </summary>
/// <remarks>
/// There is no sequence header and no video object layer header anywhere in any of the three versions.
/// A packet is a picture, the picture begins at its first bit, and everything ISO/IEC 14496-2 states
/// once per layer — the quantiser precision, the chrominance format, the sample depth, which inverse
/// quantisation method, whether vectors are to a quarter of a sample — is fixed rather than signalled.
/// That is why this decoder takes the picture size from the container and the MPEG-4 Part 2 decoder
/// beside it refuses to.
/// <para/>
/// Version 1 is the exception to "no start code": its picture opens with the four bytes
/// <c>00 00 01 00</c> and a five-bit picture number, which nothing after it ever refers to. The other
/// two dropped both.
/// <para/>
/// What follows the quantiser is where the three part company. Versions 1 and 2 state nothing else and
/// use the middle run-level tables always; version 3 states which of three run-level tables, which of
/// two DC tables and, in a predicted picture, which of two motion vector tables it was coded with.
/// </remarks>
internal sealed class MsMpeg4PictureHeader {

  /// <summary>Intra coded: decodable on its own.</summary>
  internal const int IntraCoded = 0;

  /// <summary>Predicted from the picture before it. There are no others — the format has no B pictures.</summary>
  internal const int PredictiveCoded = 1;

  /// <summary>The four bytes a version 1 picture opens with, and the only start code in the family.</summary>
  private const int _VERSION1_START_CODE = 0x00000100;

  /// <summary>What a slice count of one looks like on the wire in versions 2 and 3.</summary>
  /// <remarks>
  /// The field is the number of slices plus twenty-two. Version 1 puts the height of a slice here
  /// instead, which is the one place the versions disagree about a field they all have.
  /// </remarks>
  private const int _SLICE_COUNT_BIAS = 0x16;

  /// <summary>The middle run-level table, which is the only one versions 1 and 2 ever use.</summary>
  private const int _DEFAULT_RUN_LEVEL_TABLE_INDEX = 2;

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
  /// where everything moves looks like. Version 1 has no such field and always spends the bit; an
  /// intra picture has no such flag at all because none of its macroblocks may be skipped.
  /// </remarks>
  internal required bool SkipBitsArePresent { get; init; }

  /// <summary>Which of the three run-level tables the intra luminance blocks were coded with.</summary>
  internal required int RunLevelTableIndex { get; init; }

  /// <summary>Which of the three every other block was coded with, offset by three into the same array.</summary>
  internal required int ChromaRunLevelTableIndex { get; init; }

  /// <summary>Which of version 3's two DC tables this picture uses; nought for versions 1 and 2.</summary>
  internal required int DcTableIndex { get; init; }

  /// <summary>Which of version 3's two motion vector tables it uses; nought for versions 1 and 2.</summary>
  internal required int MotionVectorTableIndex { get; init; }

  /// <summary>WMV1: whether each coded macroblock restates its run-level table.</summary>
  internal bool PerMacroblockRunLevel { get; init; }

  /// <summary>WMV1: whether intra macroblocks of a predicted picture predict their DC from samples, with a stated direction.</summary>
  internal bool InterIntraPrediction { get; init; }

  /// <summary>WMV1: the bit rate the last intra picture's extension header stated, in bits a second.</summary>
  internal int BitRate { get; init; }

  /// <summary>WMV1: whether predicted pictures alternate their rounding, from the extension header.</summary>
  internal bool FlipFlopRounding { get; init; }

  /// <summary>The bit rate above which WMV1 restates run-level tables per macroblock: FFmpeg's <c>MBAC_BITRATE</c>.</summary>
  private const int _PER_MACROBLOCK_BIT_RATE = 50 * 1024;

  /// <summary>The bit rate at or below which small WMV1 pictures use inter-intra prediction: FFmpeg's <c>II_BITRATE</c>.</summary>
  private const int _INTER_INTRA_BIT_RATE = 128 * 1024;

  /// <summary>
  /// Reads a picture header from the first bit of a packet.
  /// </summary>
  /// <param name="reader">The bitstream, positioned at the very start of the picture.</param>
  /// <param name="version">Which of the three bitstreams this is, as the container named it.</param>
  /// <param name="macroblockHeight">The picture's height in macroblocks, which sizes a slice.</param>
  /// <param name="previousSliceHeight">
  /// What the last intra picture said, for a predicted picture that does not say.
  /// </param>
  /// <param name="previous">The last picture's header, whose bit rate and rounding a WMV1 predicted picture inherits.</param>
  /// <param name="pictureArea">Width times height, which decides WMV1's inter-intra prediction.</param>
  internal static MsMpeg4PictureHeader Parse(
    ref Mpeg4BitReader reader, MsMpeg4Version version, int macroblockHeight, int previousSliceHeight,
    MsMpeg4PictureHeader? previous = null, int pictureArea = 0) {
    if (version == MsMpeg4Version.Version1) {
      var startCode = (reader.ReadBits(16) << 16) | reader.ReadBits(16);
      if (startCode != _VERSION1_START_CODE)
        throw new InvalidDataException(
          $"This Microsoft MPEG-4 version 1 picture opens with {startCode:X8} where the format's only start code is "
          + $"{_VERSION1_START_CODE:X8}. Either the packet is not a picture or the stream is one of the other two "
          + "versions under a four-character code this decoder was handed by mistake.");

      // The picture number. Nothing after it refers to it, and no decoder has ever needed it.
      reader.Skip(5);
    }

    var codingType = reader.ReadBits(2);
    if (codingType > PredictiveCoded)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 picture states picture type {codingType}. The format has only two, the intra coded "
        + "picture and the predicted one, so a packet stating anything else is not a picture of this codec — most "
        + "likely the stream is one of the other Microsoft variants under a four-character code this decoder was "
        + "handed by mistake.");

    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "This Microsoft MPEG-4 picture states a quantiser of zero, which is not a step size and would reconstruct "
        + "every coefficient as zero. The field holds 1 to 31.");

    if (version == MsMpeg4Version.Wmv1)
      return codingType == IntraCoded
        ? _ParseWmv1Intra(ref reader, quantiser, macroblockHeight)
        : _ParseWmv1Predicted(ref reader, quantiser, previousSliceHeight, previous, pictureArea);

    return codingType == IntraCoded
      ? _ParseIntra(ref reader, version, quantiser, macroblockHeight)
      : _ParsePredicted(ref reader, version, quantiser, previousSliceHeight);
  }

  private static MsMpeg4PictureHeader _ParseIntra(
    ref Mpeg4BitReader reader, MsMpeg4Version version, int quantiser, int macroblockHeight) {
    var sliceHeight = _ParseSliceField(ref reader, version, macroblockHeight);

    if (version != MsMpeg4Version.Version3)
      return new() {
        CodingType = IntraCoded,
        Quantiser = quantiser,
        SliceHeight = sliceHeight,
        SkipBitsArePresent = false,
        RunLevelTableIndex = _DEFAULT_RUN_LEVEL_TABLE_INDEX,
        ChromaRunLevelTableIndex = _DEFAULT_RUN_LEVEL_TABLE_INDEX,
        DcTableIndex = 0,
        MotionVectorTableIndex = 0,
      };

    // Chrominance first and luminance second, which is the other way round from every other place the
    // two are named together.
    var chroma = _ReadZeroOneOrTwo(ref reader);
    var luminance = _ReadZeroOneOrTwo(ref reader);

    return new() {
      CodingType = IntraCoded,
      Quantiser = quantiser,
      SliceHeight = sliceHeight,
      SkipBitsArePresent = false,
      RunLevelTableIndex = luminance,
      ChromaRunLevelTableIndex = chroma,
      DcTableIndex = reader.ReadBit(),
      MotionVectorTableIndex = 0,
    };
  }

  private static MsMpeg4PictureHeader _ParsePredicted(
    ref Mpeg4BitReader reader, MsMpeg4Version version, int quantiser, int previousSliceHeight) {
    // Version 1 spends a bit per macroblock on the skip flag whether or not any macroblock is skipped;
    // the other two say once per picture whether the bits are there at all.
    var skipBitsArePresent = version == MsMpeg4Version.Version1 || reader.ReadBit() == 1;

    if (version != MsMpeg4Version.Version3)
      return new() {
        CodingType = PredictiveCoded,
        Quantiser = quantiser,
        SliceHeight = previousSliceHeight,
        SkipBitsArePresent = skipBitsArePresent,
        RunLevelTableIndex = _DEFAULT_RUN_LEVEL_TABLE_INDEX,
        ChromaRunLevelTableIndex = _DEFAULT_RUN_LEVEL_TABLE_INDEX,
        DcTableIndex = 0,
        MotionVectorTableIndex = 0,
      };

    var runLevel = _ReadZeroOneOrTwo(ref reader);

    return new() {
      CodingType = PredictiveCoded,
      Quantiser = quantiser,
      SliceHeight = previousSliceHeight,
      SkipBitsArePresent = skipBitsArePresent,
      RunLevelTableIndex = runLevel,
      ChromaRunLevelTableIndex = runLevel,
      DcTableIndex = reader.ReadBit(),
      MotionVectorTableIndex = reader.ReadBit(),
    };
  }

  /// <summary>
  /// Reads the five-bit slice field, which version 1 reads as a height and the other two as a count.
  /// </summary>
  /// <remarks>
  /// The division is a truncating one, so a picture whose macroblock rows do not divide evenly among
  /// its slices gives every slice the smaller height and the last one whatever is left over. Rounding
  /// the other way looks more natural and puts the boundary in the wrong place, which shows up as a
  /// row of macroblocks predicted from neighbours the encoder treated as absent.
  /// </remarks>
  /// <summary>
  /// A WMV1 intra picture: the slice count, then the extension header that version 3 puts after the
  /// picture — frame rate, bit rate and the rounding flag — and the tables, as FFmpeg's
  /// <c>msmpeg4_decode_picture_header</c> reads them.
  /// </summary>
  private static MsMpeg4PictureHeader _ParseWmv1Intra(ref Mpeg4BitReader reader, int quantiser, int macroblockHeight) {
    var sliceHeight = _ParseSliceField(ref reader, MsMpeg4Version.Wmv1, macroblockHeight);
    reader.Skip(5);
    var bitRate = reader.ReadBits(11) * 1024;
    var flipFlop = reader.ReadBit() == 1;

    var perMacroblock = bitRate > _PER_MACROBLOCK_BIT_RATE && reader.ReadBit() == 1;
    int chroma = 0, luminance = 0;
    if (!perMacroblock) {
      chroma = _ReadZeroOneOrTwo(ref reader);
      luminance = _ReadZeroOneOrTwo(ref reader);
    }

    return new() {
      CodingType = IntraCoded,
      Quantiser = quantiser,
      SliceHeight = sliceHeight,
      SkipBitsArePresent = false,
      RunLevelTableIndex = luminance,
      ChromaRunLevelTableIndex = chroma,
      DcTableIndex = reader.ReadBit(),
      MotionVectorTableIndex = 0,
      PerMacroblockRunLevel = perMacroblock,
      BitRate = bitRate,
      FlipFlopRounding = flipFlop,
    };
  }

  private static MsMpeg4PictureHeader _ParseWmv1Predicted(
    ref Mpeg4BitReader reader, int quantiser, int previousSliceHeight, MsMpeg4PictureHeader? previous, int pictureArea) {
    var bitRate = previous?.BitRate ?? 0;
    var skipBitsArePresent = reader.ReadBit() == 1;
    var perMacroblock = bitRate > _PER_MACROBLOCK_BIT_RATE && reader.ReadBit() == 1;
    var runLevel = perMacroblock ? 0 : _ReadZeroOneOrTwo(ref reader);

    return new() {
      CodingType = PredictiveCoded,
      Quantiser = quantiser,
      SliceHeight = previousSliceHeight,
      SkipBitsArePresent = skipBitsArePresent,
      RunLevelTableIndex = runLevel,
      ChromaRunLevelTableIndex = runLevel,
      DcTableIndex = reader.ReadBit(),
      MotionVectorTableIndex = reader.ReadBit(),
      PerMacroblockRunLevel = perMacroblock,
      InterIntraPrediction = pictureArea < 320 * 240 && bitRate <= _INTER_INTRA_BIT_RATE,
      BitRate = bitRate,
      FlipFlopRounding = previous?.FlipFlopRounding ?? false,
    };
  }

  /// <summary>Zero, one or two, written as 0, 10 and 11: FFmpeg's <c>decode012</c>.</summary>
  internal static int ReadZeroOneOrTwo(ref Mpeg4BitReader reader) => _ReadZeroOneOrTwo(ref reader);

  private static int _ParseSliceField(ref Mpeg4BitReader reader, MsMpeg4Version version, int macroblockHeight) {
    var code = reader.ReadBits(5);

    if (version == MsMpeg4Version.Version1) {
      if (code == 0 || code > macroblockHeight)
        throw new InvalidDataException(
          $"This Microsoft MPEG-4 version 1 intra picture states a slice height of {code} macroblock rows in a "
          + $"picture {macroblockHeight} rows tall.");

      return code;
    }

    if (code < _SLICE_COUNT_BIAS + 1)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 intra picture states a slice field of {code}, which is below the bias of "
        + $"{_SLICE_COUNT_BIAS} the field carries and so states fewer than one slice.");

    var slices = code - _SLICE_COUNT_BIAS;
    var sliceHeight = macroblockHeight / slices;
    if (sliceHeight < 1)
      throw new InvalidDataException(
        $"This Microsoft MPEG-4 intra picture states {slices} slices for a picture {macroblockHeight} macroblock "
        + "rows tall, and a slice is always a whole number of macroblock rows.");

    return sliceHeight;
  }

  /// <summary>
  /// Reads one of three values in one or two bits: a nought is nought, and a one is followed by the
  /// bit that tells one from two.
  /// </summary>
  private static int _ReadZeroOneOrTwo(ref Mpeg4BitReader reader)
    => reader.ReadBit() == 0 ? 0 : reader.ReadBit() + 1;
}
