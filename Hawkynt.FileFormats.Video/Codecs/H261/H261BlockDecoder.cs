using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The block layer of ITU-T H.261 clause 4.2.4: run-level codes in, sixty-four dequantised and
/// transformed samples out.
/// </summary>
/// <remarks>
/// Dequantisation (the reconstruction formula of the block preceding Table 6) and the inverse
/// transform (clause 3.2.4) are both taken from <see cref="H263Quantisation"/> and <see
/// cref="H263InverseDct"/> without change, because the arithmetic is not merely similar — it is the
/// same formula, checked term for term against H.261's own text: the non-DC reconstruction levels
/// (REC = QUANT&#183;(2&#183;level&#177;1), pulled one towards zero at an even QUANT) match H.263 clause
/// 6.2.1 exactly, the intra DC step of eight with 255 standing in for the unused code 128 matches
/// clause 5.4.1 exactly, the zig-zag scan of Figure 12 is the same permutation as H.263's Figure 6, and
/// the inverse transform's defining sum and its [-256, 255] output range are the same formula H.263
/// clause 6.2.2 states — both Recommendations specify it as an accuracy bound (Annex A in each) rather
/// than an algorithm, which is why H.263 kept it unchanged from H.261 rather than replacing it.
/// <para/>
/// What is not shared is how the coefficients arrive. Where H.263's Table 16 bakes an end-of-block flag
/// into every code, H.261's Table 5 carries a separate end-of-block symbol that cannot be the first
/// thing a coded block says — so the very first coefficient is read from a different table
/// (<see cref="H261VlcTables.CoefficientFirst"/>) than every one after it
/// (<see cref="H261VlcTables.CoefficientNotFirst"/>), and only the second table can end a block at all.
/// </remarks>
internal static class H261BlockDecoder {

  /// <summary>
  /// Reads an intra-coded block: the eight-bit DC of clause 4.2.4.2, then its AC coefficients.
  /// </summary>
  /// <remarks>
  /// The DC is never read from <see cref="H261VlcTables.CoefficientFirst"/> — an intra block's "first
  /// coefficient" in Table 5's sense is its first AC term, because the DC is a fixed-length field
  /// outside TCOEFF entirely, exactly as it is in H.263.
  /// </remarks>
  internal static void ReadIntra(ref H263BitReader reader, scoped Span<int> block, int quantiser) {
    block.Clear();

    var dc = reader.ReadBits(8);
    if (dc is 0 or 128)
      throw new InvalidDataException(
        $"An H.261 intra block states an eight-bit DC field of {dc}, which ITU-T H.261 clause 4.2.4.2 leaves unused: "
        + "the reconstruction level 1024 that value 128 would carry is coded as 255 instead.");

    block[0] = H263Quantisation.DequantiseIntraDc(dc);
    _ReadCoefficients(ref reader, block, position: 0, quantiser, isFirst: false);
    H263InverseDct.Transform(block);
  }

  /// <summary>
  /// Reads a coded block of a predicted macroblock: the residual added to a motion-compensated (and
  /// possibly filtered) prediction elsewhere.
  /// </summary>
  internal static void ReadInter(ref H263BitReader reader, scoped Span<int> block, int quantiser) {
    block.Clear();
    _ReadCoefficients(ref reader, block, position: -1, quantiser, isFirst: true);
    H263InverseDct.Transform(block);
  }

  private static void _ReadCoefficients(
    ref H263BitReader reader, scoped Span<int> block, int position, int quantiser, bool isFirst) {
    for (; ; ) {
      var table = isFirst ? H261VlcTables.CoefficientFirst : H261VlcTables.CoefficientNotFirst;
      isFirst = false;

      var value = table.Read(ref reader);
      if (value == H261VlcTables.CoefficientEob)
        return;

      int run, level;
      if (value == H261VlcTables.CoefficientEscape) {
        (run, level) = _ReadEscape(ref reader);
      } else {
        run = H261VlcTables.RunOf(value);
        level = H261VlcTables.LevelOf(value);
        if (reader.ReadBit() == 1)
          level = -level;
      }

      position += run + 1;
      if ((uint)position > 63)
        throw new InvalidDataException(
          $"The run-level codes of an H.261 block reach scan position {position}, past the sixty-four Figure 12 "
          + "gives a block.");

      block[H263Quantisation.ZigZag[position]] = H263Quantisation.Dequantise(level, quantiser);
    }
  }

  /// <summary>
  /// Reads the escape form of clause 4.2.4.1: a six-bit RUN and an eight-bit two's-complement LEVEL,
  /// neither carrying an end-of-block flag of its own — unlike H.263's escape, an escaped H.261
  /// coefficient is always followed by another TCOEFF symbol from
  /// <see cref="H261VlcTables.CoefficientNotFirst"/>, which is where end of block can occur.
  /// </summary>
  private static (int Run, int Level) _ReadEscape(ref H263BitReader reader) {
    var run = reader.ReadBits(6);
    var level = reader.ReadBits(8);
    if (level > 127)
      level -= 256;

    if (level == 0)
      throw new InvalidDataException(
        "An escaped TCOEFF in the H.261 block layer states a level of zero, which Table 5's eight-bit level table "
        + "marks FORBIDDEN.");

    if (level == -128)
      throw new InvalidDataException(
        "An escaped TCOEFF in the H.261 block layer states a level of -128, which Table 5's eight-bit level table "
        + "marks FORBIDDEN.");

    return (run, level);
  }
}
