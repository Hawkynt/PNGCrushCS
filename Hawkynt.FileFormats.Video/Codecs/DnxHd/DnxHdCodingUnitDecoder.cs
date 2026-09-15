using System;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// Decodes one coding unit — for a progressive or frame-encoded stream, the whole picture; for a
/// classic interlaced stream, one field.
/// </summary>
/// <remarks>
/// SMPTE ST 2019-1:2016, 7.3 and 8.1. The compressed payload is a run of macroblocks, grouped into
/// scan lines that each run the full width of the raster, and each scan line begins at a byte offset
/// the header states (7.2.11) rather than wherever the previous one happened to stop. Every scan
/// line is therefore independent: the DC prediction is reset at the start of one (8.2.4), the offset
/// is stated rather than inferred, and 7.3.1 pads the end of one so the next starts on a four-byte
/// boundary.
/// <para/>
/// That independence is the format's whole reason for existing. A frame of VC-3 is meant to be
/// decodable by as many workers as there are scan lines, and recoverable across a damaged one, which
/// is what an editing codec on a shared storage system needs and what an entropy coder running the
/// length of a frame cannot give.
/// </remarks>
internal static class DnxHdCodingUnitDecoder {

  /// <summary>
  /// Which component each block of a 4:2:2 macroblock belongs to, and where it sits — Table 5.
  /// </summary>
  /// <remarks>
  /// The order is not the obvious one. Four luma blocks and four chroma ones interleave as
  /// Y, Y, Cb, Cr, Y, Y, Cb, Cr, so the top half of the macroblock is complete in all three
  /// components before the bottom half begins. Reading them in plane order instead decodes the
  /// right number of blocks in the wrong places and still produces a picture.
  /// </remarks>
  private static readonly (int Component, int X, int Y)[] _Blocks422 = [
    (0, 0, 0), (0, 8, 0), (1, 0, 0), (2, 0, 0),
    (0, 0, 8), (0, 8, 8), (1, 0, 8), (2, 0, 8),
  ];

  /// <summary>
  /// Which component each block of a 4:4:4 macroblock belongs to, and where it sits — Table 6.
  /// </summary>
  /// <remarks>
  /// Twelve blocks, interleaved the same way: the three channels' top halves, then the three
  /// channels' bottom halves. Under the RGB format rules these same component indices are R, G and B
  /// when ACF is clear, or Y′, Cb and Cr when ACF requests the alternate BT.709 representation.
  /// </remarks>
  private static readonly (int Component, int X, int Y)[] _Blocks444 = [
    (0, 0, 0), (0, 8, 0), (1, 0, 0), (1, 8, 0), (2, 0, 0), (2, 8, 0),
    (0, 0, 8), (0, 8, 8), (1, 0, 8), (1, 8, 8), (2, 0, 8), (2, 8, 8),
  ];

  /// <summary>Decodes the coding unit's macroblocks into the planes.</summary>
  /// <param name="fieldOffset">First raster row of a classic field coding unit.</param>
  /// <param name="fieldStep">One for frame coding, two when weaving a classic field coding unit.</param>
  internal static void Decode(
    ReadOnlyMemory<byte> unit,
    DnxHdFrameHeader header,
    DnxHdPlanes planes,
    int fieldOffset = 0,
    int fieldStep = 1) {
    var payload = unit[header.HeaderSize..];
    var bits = new DnxHdBitReader(payload);
    var blocks = header.SubSampling == 2 ? _Blocks444 : _Blocks422;
    var chromaMacroblockWidth = header.SubSampling == 2 ? 16 : 8;
    var decoder = new DnxHdBlockDecoder(header);

    var width = header.WidthInMacroblocks;
    var height = header.HeightInMacroblocks;

    // Table 11: 1256/1270 use ACF in the low header bit; CID 1260 instead prefixes MFF and has a
    // ten-bit quantisation scale. Every macroblock header remains exactly twelve bits.
    var colourModeFlagged = header.CompressionIdValue is 1256 or 1270;
    var adaptiveFieldMacroblocks = header.CompressionIdValue == 1260 && header.AdaptiveMacroblocks;

    // 8.2.4 keeps one prediction per component type; three here, since alpha is refused before this
    // is reached. Under direct RGB the three predictors simply belong to R, G and B instead.
    var predictions = new int[3];

    for (var scanLine = 0; scanLine < height; ++scanLine) {
      bits.SeekToByte(header.ScanIndices[scanLine]);
      Array.Clear(predictions);

      for (var macroblock = 0; macroblock < width; ++macroblock) {
        bool fieldMacroblock;
        int quantisationScale;
        int lowBit;

        if (adaptiveFieldMacroblocks) {
          // Figure 32: MFF, ten bits of qsf, one reserved zero bit.
          fieldMacroblock = bits.Bit() != 0;
          quantisationScale = bits.Bits(10);
          lowBit = bits.Bit();
        } else {
          // Figures 30/31: eleven bits of qsf, then reserved zero or ACF respectively.
          fieldMacroblock = false;
          quantisationScale = bits.Bits(11);
          lowBit = bits.Bit();
        }

        if (quantisationScale == 0)
          throw new InvalidDataException(
            $"A VC-3 macroblock at scan line {scanLine}, column {macroblock} states a quantisation scale factor of zero, which would make every coefficient of it vanish.");

        if (adaptiveFieldMacroblocks && lowBit != 0)
          throw new InvalidDataException(
            $"A CID 1260 macroblock at scan line {scanLine}, column {macroblock} sets the reserved low bit of its Figure 32 header.");

        if (colourModeFlagged) {
          var alternateColour = lowBit != 0;

          // Figure 31 permits ACF=1 only under the RGB format rules and only when the colour volume
          // is not out-of-band. ACF=0 under CLF=1 is direct RGB; ACF=1 is BT.709 Y′CbCr transformed
          // back to RGB during final packing. CLF=0/ACF=0 remains ordinary 4:4:4 Y′CbCr.
          if (alternateColour && (!header.Rgb || header.ColorVolume == 3))
            throw new InvalidDataException(
              $"A VC-3 macroblock at scan line {scanLine}, column {macroblock} sets ACF where Coding Control B does not permit the alternate BT.709 colour transform.");

          planes.SetDirectRgbMacroblock(macroblock, scanLine, header.Rgb && !alternateColour);
        }

        var scanLineY = fieldOffset + scanLine * 16 * fieldStep;
        var rowStep = fieldStep * (fieldMacroblock ? 2 : 1);

        foreach (var (component, x, y) in blocks) {
          var plane = planes.Plane(component);
          var planeWidth = planes.PlaneWidth(component);
          var planeHeight = component == 0 ? planes.Height : planes.ChromaHeight;
          var macroblockWidth = component == 0 ? 16 : chromaMacroblockWidth;

          // An adaptive field macroblock puts the first eight-line block on one field and the second
          // on the other: their first rows are therefore one raster line apart, with each block then
          // stepping two rows. Classic field coding instead steps the whole coding unit by two.
          var blockY = fieldMacroblock ? (y == 0 ? 0 : fieldStep) : y * fieldStep;

          decoder.Decode(
            bits, component != 0, quantisationScale, ref predictions[component],
            plane, planeWidth, planeHeight,
            macroblock * macroblockWidth + x, scanLineY + blockY, rowStep);
        }
      }
    }
  }
}
