using System;
using System.IO;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// Decodes one coding unit — for a progressive frame, the whole picture.
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

  /// <summary>The macroblock header, 7.3.1.1: twelve bits, of which the top eleven are the scale.</summary>
  private const int _MACROBLOCK_HEADER_BITS = 12;

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
  /// channels' bottom halves.
  /// </remarks>
  private static readonly (int Component, int X, int Y)[] _Blocks444 = [
    (0, 0, 0), (0, 8, 0), (1, 0, 0), (1, 8, 0), (2, 0, 0), (2, 8, 0),
    (0, 0, 8), (0, 8, 8), (1, 0, 8), (1, 8, 8), (2, 0, 8), (2, 8, 8),
  ];

  /// <summary>Decodes the coding unit's macroblocks into the planes.</summary>
  internal static void Decode(ReadOnlyMemory<byte> unit, DnxHdFrameHeader header, DnxHdPlanes planes) {
    var payload = unit[header.HeaderSize..];
    var bits = new DnxHdBitReader(payload);
    var blocks = header.SubSampling == 2 ? _Blocks444 : _Blocks422;
    var chromaMacroblockWidth = header.SubSampling == 2 ? 16 : 8;
    var decoder = new DnxHdBlockDecoder(header);

    var width = header.WidthInMacroblocks;
    var height = header.HeightInMacroblocks;

    // Table 11: only these two identifiers put a colour-mode flag in the macroblock header.
    var colourModeFlagged = header.CompressionIdValue is 1256 or 1270;

    // 8.2.4 keeps one prediction per component type; three here, since the alpha channel and the
    // fourth channel of a 4:4:4:4 bitstream are refused before this is reached.
    var predictions = new int[3];

    for (var scanLine = 0; scanLine < height; ++scanLine) {
      bits.SeekToByte(header.ScanIndices[scanLine]);
      Array.Clear(predictions);

      for (var macroblock = 0; macroblock < width; ++macroblock) {
        var macroblockHeader = bits.Bits(_MACROBLOCK_HEADER_BITS);

        // 7.3.1.1, Table 11 and Figures 30 and 31: the eleven high bits are always the quantisation
        // scale factor. The low bit is reserved for most compression identifiers, but for 1256 and
        // 1270 it is the colour mode of this macroblock — a bitstream flagged as RGB codes each
        // macroblock either as red, green and blue or as luma and colour difference, and says which
        // here rather than once for the frame.
        var quantisationScale = macroblockHeader >> 1;
        if (quantisationScale == 0)
          throw new InvalidDataException(
            $"A VC-3 macroblock at scan line {scanLine}, column {macroblock} states a quantisation scale factor of zero, which would make every coefficient of it vanish.");

        if (colourModeFlagged && (macroblockHeader & 1) == 0)
          throw new NotSupportedException(
            $"A VC-3 macroblock at scan line {scanLine}, column {macroblock} is coded in RGB mode (compression ID {header.CompressionIdValue}, macroblock colour flag clear). Only the luma and colour-difference mode is read here, and a macroblock of red, green and blue is refused rather than shown as though its channels were luma and chroma.");

        foreach (var (component, x, y) in blocks) {
          var plane = planes.Plane(component);
          var planeWidth = planes.PlaneWidth(component);
          var planeHeight = component == 0 ? planes.Height : planes.ChromaHeight;
          var macroblockWidth = component == 0 ? 16 : chromaMacroblockWidth;

          decoder.Decode(
            bits, component != 0, quantisationScale, ref predictions[component],
            plane, planeWidth, planeHeight,
            macroblock * macroblockWidth + x, scanLine * 16 + y);
        }
      }
    }
  }
}
