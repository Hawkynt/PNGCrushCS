using System;
using System.Collections.Generic;
using FileFormat.Codecs.Asv1;
using FileFormat.Codecs.Asv2;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// Codes one whole picture of either ASUS codec: the macroblock walk of clause 3.1, six blocks a
/// macroblock, and the storage order the finished packet is kept in.
/// </summary>
/// <remarks>
/// Every picture is coded on its own. Neither codec has motion compensation or prediction between
/// pictures of any kind, so there is no reference to hold, nothing accumulates across a sequence and
/// every packet is a key frame.
/// <para/>
/// The walk is the decoders' own, written once here for both: the whole macroblocks in raster order,
/// then the partial-width column top to bottom, then the partial-height row — corner included — left
/// to right.
/// </remarks>
internal static class AsvPictureEncoder {

  /// <summary>Codes one picture into the bytes a packet carries.</summary>
  /// <param name="version">Which of the two codecs is being written.</param>
  /// <param name="source">The picture's planes, padded out to whole macroblocks.</param>
  /// <param name="width">The picture's displayed width, which decides the macroblock walk.</param>
  /// <param name="height">Its displayed height.</param>
  /// <param name="dequantFactors">The decoder's own <c>floor(D * q[i] / QP)</c>, one a raster position.</param>
  internal static byte[] Encode(AsvVersion version, H263Frame source, int width, int height, int[] dequantFactors) {
    var macroblockWidth = (width + 15) / 16;
    var macroblockHeight = (height + 15) / 16;
    var writer = new AsvBitWriter();

    Span<int> samples = stackalloc int[64];
    Span<double> coefficients = stackalloc double[64];
    Span<int> levels = stackalloc int[64];

    foreach (var (mbX, mbY) in _MacroblockOrder(width, height, macroblockWidth, macroblockHeight))
      for (var index = 0; index < 6; ++index) {
        _Gather(source, mbX, mbY, index, samples);
        AsvForwardDct.Transform(samples, coefficients);
        AsvQuantiser.Levels(coefficients, dequantFactors, levels);

        var directCurrent = AsvQuantiser.DirectCurrent(coefficients[0]);
        switch (version) {
          case AsvVersion.Version1:
            _DropUncodeablePositions(levels);
            Asv1BlockEncoder.Write(writer, directCurrent, levels);
            break;
          default:
            Asv2BlockEncoder.Write(writer, directCurrent, levels);
            break;
        }
      }

    // Each codec's one storage oddity is its own inverse, so the decoder's own undoing of it is what
    // applies it here — there is no second, mirror-image copy of either scrambling in this package.
    return version == AsvVersion.Version1
      ? Asv1Bitstream.SwapWords(writer.Finish(4))
      : Asv2Bitstream.ReverseBits(writer.Finish(1));
  }

  /// <summary>Zeroes every level ASV1's ten reachable coefficient groups cannot state.</summary>
  private static void _DropUncodeablePositions(scoped Span<int> levels) {
    var codeable = Asv1BlockEncoder.CodeablePositions;
    for (var i = 1; i < 64; ++i)
      if (!codeable[i])
        levels[i] = 0;
  }

  /// <summary>Reads one of a macroblock's six blocks out of the picture's planes.</summary>
  private static void _Gather(H263Frame source, int mbX, int mbY, int index, scoped Span<int> samples) {
    var (plane, width, _) = source.PlaneOf(index);
    var (left, top) = index < 4
      ? (mbX * 16 + (index & 1) * 8, mbY * 16 + (index >> 1) * 8)
      : (mbX * 8, mbY * 8);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[row + x];
    }
  }

  /// <summary>
  /// Clause 3.1: the whole macroblocks in raster order, then the partial-width column top to bottom,
  /// then the partial-height row — corner included — left to right.
  /// </summary>
  private static IEnumerable<(int X, int Y)> _MacroblockOrder(int width, int height, int macroblockWidth, int macroblockHeight) {
    var fullColumns = width / 16;
    var fullRows = height / 16;

    for (var y = 0; y < fullRows; ++y)
      for (var x = 0; x < fullColumns; ++x)
        yield return (x, y);

    if (fullColumns < macroblockWidth)
      for (var y = 0; y < fullRows; ++y)
        yield return (fullColumns, y);

    if (fullRows < macroblockHeight)
      for (var x = 0; x < macroblockWidth; ++x)
        yield return (x, fullRows);
  }
}
