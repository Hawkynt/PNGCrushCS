using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// Decodes one picture — a whole progressive frame, or one field of an interlaced one.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5.2 and 5.3. A picture is a header, a table giving the size of every slice, and then
/// the slices themselves in the order the table lists them: macroblock row by macroblock row, and
/// left to right within a row.
/// <para/>
/// <b>Every slice is independent.</b> There is no prediction of any kind across a slice boundary —
/// not of DC, not of quantisation, not of the codebook adaptations — which is what lets an encoder
/// and a decoder work on the slices of a picture in parallel, and what makes the slice table worth
/// the bytes it costs. It also means a damaged slice damages only the macroblocks it covers, though
/// this decoder refuses such a frame rather than handing back a partly reconstructed one.
/// </remarks>
internal static class ProResPictureDecoder {

  /// <summary>The eight bytes of a picture header, RDD 36:2022, 5.2.1.</summary>
  private const int _PICTURE_HEADER_FIXED_SIZE = 8;

  /// <summary>The smallest slice header, RDD 36:2022, 5.3.1; two bytes more when there is alpha.</summary>
  private const int _SLICE_HEADER_MINIMUM_SIZE = 6;

  /// <summary>
  /// Decodes one picture into the frame's planes.
  /// </summary>
  /// <param name="picture">The bitstream from the start of the picture header onwards.</param>
  /// <param name="header">The frame header, which states the sampling and the weight matrices.</param>
  /// <param name="planes">The frame's planes, written into at the rows this picture occupies.</param>
  /// <param name="pictureVerticalSize">The height of this picture in luma samples.</param>
  /// <param name="fieldOffset">The frame row this picture's row 0 is; 0 for a frame picture.</param>
  /// <param name="fieldStep">1 for a frame picture, 2 for a field picture.</param>
  /// <returns>The size of the picture in bytes, which is where the next one starts.</returns>
  internal static int Decode(
    ReadOnlyMemory<byte> picture,
    ProResFrameHeader header,
    ProResPlanes planes,
    int pictureVerticalSize,
    int fieldOffset,
    int fieldStep) {
    var span = picture.Span;
    if (span.Length < _PICTURE_HEADER_FIXED_SIZE)
      throw new InvalidDataException(
        $"A ProRes picture header is {_PICTURE_HEADER_FIXED_SIZE} bytes and only {span.Length} remain in the frame.");

    var pictureHeaderSize = span[0] >> 3;
    if (pictureHeaderSize < _PICTURE_HEADER_FIXED_SIZE)
      throw new InvalidDataException(
        $"A ProRes picture states a header of {pictureHeaderSize} bytes, which is shorter than the header's own fields.");

    var pictureSize = (int)BinaryPrimitives.ReadUInt32BigEndian(span[1..]);
    if (pictureSize < pictureHeaderSize || pictureSize > span.Length)
      throw new InvalidDataException(
        $"A ProRes picture states a size of {pictureSize} bytes, which is not within the {span.Length} bytes remaining in the frame.");

    // 6.2.1: permissible values are 0 to 3, standing for 1, 2, 4 and 8 macroblocks. The field is two
    // bits wide, so it cannot state anything else and there is nothing to refuse here.
    var log2DesiredSliceSize = (span[7] >> 4) & 3;

    var widthInMacroblocks = (header.HorizontalSize + 15) / 16;
    var heightInMacroblocks = (pictureVerticalSize + 15) / 16;
    var sliceSizes = ProResSliceLayout.Build(widthInMacroblocks, log2DesiredSliceSize);

    var sliceCount = heightInMacroblocks * sliceSizes.Length;
    var tableBytes = sliceCount * 2;
    if (pictureHeaderSize + tableBytes > pictureSize)
      throw new InvalidDataException(
        $"A ProRes picture of {pictureSize} bytes has no room for a slice table of {sliceCount} slices.");

    // 7.2.2: which of the two block scans applies is decided by the frame's interlace mode, so it is
    // the same for every slice and is chosen once here.
    var scan = header.InterlaceMode == 0 ? ProResScan.Progressive : ProResScan.Interlaced;

    // 6.4: the slices begin where the stated header size says they do, not where the header's known
    // fields end. A picture header carrying data of an unrecognised version variant is longer than
    // eight bytes and the extra is stepped over rather than read as slice sizes.
    var at = pictureHeaderSize + tableBytes;

    for (var i = 0; i < heightInMacroblocks; ++i) {
      var macroblock = 0;

      for (var j = 0; j < sliceSizes.Length; ++j) {
        var entry = pictureHeaderSize + ((i * sliceSizes.Length) + j) * 2;
        var codedSize = BinaryPrimitives.ReadUInt16BigEndian(span[entry..]);

        if (at + codedSize > pictureSize)
          throw new InvalidDataException(
            $"A ProRes slice table states slice ({i}, {j}) as {codedSize} bytes ending past the {pictureSize} bytes of its picture.");

        // 5.3: every macroblock row is sixteen rows of pixels tall except possibly the last, which
        // is however many rows of the picture are left. Only the alpha channel needs to know, since
        // it is the one component whose coded size depends on it; the colour blocks of a short row
        // are coded whole and their excess rows discarded when written.
        var sliceHeight = i < heightInMacroblocks - 1 ? 16 : pictureVerticalSize - 16 * (heightInMacroblocks - 1);

        _DecodeSlice(
          picture.Slice(at, codedSize), header, planes, scan, sliceSizes[j], macroblock, i,
          sliceHeight, fieldOffset, fieldStep);

        at += codedSize;
        macroblock += sliceSizes[j];
      }
    }

    return pictureSize;
  }

  private static void _DecodeSlice(
    ReadOnlyMemory<byte> slice,
    ProResFrameHeader header,
    ProResPlanes planes,
    int[] scan,
    int sliceSizeInMacroblocks,
    int macroblockOffset,
    int macroblockRow,
    int sliceHeight,
    int fieldOffset,
    int fieldStep) {
    var span = slice.Span;
    var hasAlpha = header.AlphaChannelType != 0;
    var headerMinimum = hasAlpha ? _SLICE_HEADER_MINIMUM_SIZE + 2 : _SLICE_HEADER_MINIMUM_SIZE;

    if (span.Length < headerMinimum)
      throw new InvalidDataException(
        $"A ProRes slice of {span.Length} bytes is shorter than its own {headerMinimum}-byte header.");

    var sliceHeaderSize = span[0] >> 3;
    if (sliceHeaderSize < headerMinimum || sliceHeaderSize > span.Length)
      throw new InvalidDataException(
        $"A ProRes slice states a header of {sliceHeaderSize} bytes, which is neither a whole header nor within the slice's {span.Length} bytes.");

    var quantizationIndex = span[1];

    // 6.3.1: permissible values are 1 to 224 and all others are reserved. A zero index would make
    // every dequantised coefficient of the slice zero — a flat mid-grey block that looks like a
    // decode rather than a refusal — so it is refused.
    if (quantizationIndex is < 1 or > 224)
      throw new InvalidDataException(
        $"A ProRes slice states quantization_index {quantizationIndex}. RDD 36 6.3.1 permits 1 to 224 and reserves the rest.");

    var qScale = QuantisationScale(quantizationIndex);

    var lumaSize = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
    var cbSize = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);

    // 5.3: the red chroma size is stated only when the slice also carries alpha, because otherwise
    // it is whatever is left over. A frame with alpha needs it stated, since the alpha data follow
    // the red chroma data and nothing else says where they begin.
    var crSize = hasAlpha
      ? BinaryPrimitives.ReadUInt16BigEndian(span[6..])
      : span.Length - sliceHeaderSize - lumaSize - cbSize;

    if (crSize < 0 || sliceHeaderSize + lumaSize + cbSize + crSize > span.Length)
      throw new InvalidDataException(
        $"A ProRes slice states component sizes of {lumaSize}, {cbSize} and {crSize} bytes, which do not fit in its {span.Length} bytes.");

    var lumaAt = sliceHeaderSize;
    var cbAt = lumaAt + lumaSize;
    var crAt = cbAt + cbSize;

    var chromaBlocks = header.ChromaBlocksPerMacroblock;
    var chromaShift = header.ChromaFormat == 3 ? 0 : 1;

    _DecodeComponent(
      slice.Slice(lumaAt, lumaSize), planes, 0, 4, sliceSizeInMacroblocks, macroblockOffset,
      macroblockRow, header.LumaMatrix, qScale, scan, chromaShift: 0, fieldOffset, fieldStep);

    _DecodeComponent(
      slice.Slice(cbAt, cbSize), planes, 1, chromaBlocks, sliceSizeInMacroblocks, macroblockOffset,
      macroblockRow, header.ChromaMatrix, qScale, scan, chromaShift, fieldOffset, fieldStep);

    _DecodeComponent(
      slice.Slice(crAt, crSize), planes, 2, chromaBlocks, sliceSizeInMacroblocks, macroblockOffset,
      macroblockRow, header.ChromaMatrix, qScale, scan, chromaShift, fieldOffset, fieldStep);

    if (!hasAlpha)
      return;

    // 5.3: the alpha data are whatever is left of the slice once the three colour components have
    // been accounted for, which is why a slice that has them must state the red chroma size rather
    // than leaving it to be inferred.
    var alphaAt = crAt + crSize;

    ProResAlpha.Decode(
      slice[alphaAt..], header.AlphaChannelType, planes.Alpha!, planes.Width, planes.Height,
      macroblockOffset * 16, macroblockRow * 16, sliceSizeInMacroblocks * 16, sliceHeight,
      fieldOffset, fieldStep);
  }

  private static void _DecodeComponent(
    ReadOnlyMemory<byte> data,
    ProResPlanes planes,
    int component,
    int blocksPerMacroblock,
    int sliceSizeInMacroblocks,
    int macroblockOffset,
    int macroblockRow,
    byte[] weights,
    int qScale,
    int[] scan,
    int chromaShift,
    int fieldOffset,
    int fieldStep) {
    var blockCount = blocksPerMacroblock * sliceSizeInMacroblocks;
    var coefficients = ProResCoefficients.Decode(data, blockCount);

    var plane = planes.Plane(component);
    var planeWidth = planes.PlaneWidth(component);
    var planeHeight = component == 0 ? planes.Height : planes.ChromaHeight;
    var macroblockWidth = 16 >> chromaShift;

    for (var m = 0; m < sliceSizeInMacroblocks; ++m) {
      var originX = (macroblockOffset + m) * macroblockWidth;

      for (var b = 0; b < blocksPerMacroblock; ++b) {
        var (blockX, blockY) = _BlockPosition(component, blocksPerMacroblock, b);

        ProResBlocks.Reconstruct(
          coefficients, blocksPerMacroblock, sliceSizeInMacroblocks, m, b, weights, qScale, scan,
          plane, planeWidth, planeHeight, originX + blockX, macroblockRow * 16 + blockY,
          fieldOffset, fieldStep, planes.BitDepth);
      }
    }
  }

  /// <summary>
  /// Where a block sits inside its macroblock, RDD 36:2022, Figures 6, 7 and 8.
  /// </summary>
  /// <remarks>
  /// The three arrangements are not the same, and the specification says so in a note of its own:
  /// four luma blocks run left to right then top to bottom, while four 4:4:4 chroma blocks run top
  /// to bottom then left to right. Reading the chroma the way the luma is read transposes the colour
  /// of every macroblock in quarters — visible on a hard edge, invisible on anything smooth, which
  /// is exactly the sort of thing a still frame of a gradient would not catch.
  /// </remarks>
  private static (int X, int Y) _BlockPosition(int component, int blocksPerMacroblock, int block) {
    // Figure 6, the four luma blocks: left to right, then top to bottom.
    if (component == 0)
      return (8 * (block & 1), 8 * (block >> 1));

    // Figure 7, the two chroma blocks of a 4:2:2 macroblock: one above the other, in a region eight
    // samples wide.
    if (blocksPerMacroblock == 2)
      return (0, 8 * block);

    // Figure 8, the four chroma blocks of a 4:4:4 macroblock: top to bottom, then left to right.
    return (8 * (block >> 1), 8 * (block & 1));
  }

  /// <summary>
  /// The quantisation scale factor a slice's quantisation index stands for.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, Table 15. The index is the scale factor up to 128 and then steps by four, so the
  /// 224 permissible indices cover scale factors from 1 to 512 — fine control where the quantisation
  /// is light and coarse control where it is already heavy, which is where a step of one would be
  /// wasted.
  /// </remarks>
  internal static int QuantisationScale(int quantizationIndex)
    => quantizationIndex <= 128 ? quantizationIndex : 128 + 4 * (quantizationIndex - 128);
}
