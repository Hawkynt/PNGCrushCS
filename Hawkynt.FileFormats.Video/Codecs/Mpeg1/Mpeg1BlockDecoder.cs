using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// The block layer of ISO/IEC 11172-2, 2.4.2.7: run-level codes in, sixty-four reconstructed
/// coefficients out.
/// </summary>
/// <remarks>
/// Every block leaves here dequantised, in raster order and transformed. Handing back raw levels
/// would put the dequantisation somewhere else, and the dequantisation is exactly the step a decoder
/// can be missing while still producing a picture-shaped result — so it lives against the code that
/// reads the levels, where its absence would be conspicuous.
/// </remarks>
internal static class Mpeg1BlockDecoder {

  /// <summary>
  /// Reads an intra-coded block and returns the DC value that becomes the next block's predictor.
  /// </summary>
  /// <remarks>
  /// The DC is coded as a difference from the last intra block of the same component in this slice,
  /// which is why it is threaded through rather than being a field of anything: the predictor is
  /// reset at every slice and by every non-intra macroblock, and a predictor held in an object
  /// outlives both of those resets by accident far more easily than a parameter does.
  /// </remarks>
  internal static int ReadIntra(
    ref Mpeg1BitReader reader, scoped Span<int> block, int blockIndex, int quantiserScale, byte[] intraMatrix, int dcPredictor) {
    block.Clear();

    var sizeTable = blockIndex < 4 ? Mpeg1VlcTables.LuminanceDcSize : Mpeg1VlcTables.ChrominanceDcSize;
    var size = sizeTable.Read(ref reader);

    var differential = 0;
    if (size > 0) {
      var bits = reader.ReadBits(size);

      // A leading zero means the value is negative, and the standard's mapping is the same one JPEG
      // uses: the low half of the range stands for the negative values just below the positive ones.
      differential = (bits & (1 << (size - 1))) != 0 ? bits : bits - (1 << size) + 1;
    }

    var dc = dcPredictor + differential * 8;
    block[0] = dc;

    // The DC occupies scan position zero, so the first run-level code that follows counts its run
    // from there. Intra blocks have no dct_coeff_first: their first coefficient code is already a
    // subsequent one, because the DC was the first.
    _ReadCoefficients(ref reader, block, 0, quantiserScale, intraMatrix, intra: true);

    Mpeg1InverseDct.Transform(block);
    return dc;
  }

  /// <summary>Reads a non-intra-coded block: the residual added to a motion-compensated prediction.</summary>
  internal static void ReadNonIntra(
    ref Mpeg1BitReader reader, scoped Span<int> block, int quantiserScale, byte[] nonIntraMatrix) {
    block.Clear();

    // dct_coeff_first: as the first coefficient of a block a leading one is the whole code and means
    // a level of one, because there is no End of Block to compete with it for that bit. Every other
    // code in Table B.14 begins with a zero and is read from the table unchanged.
    int run, level;
    if (reader.NextBits(1) == 1) {
      reader.Skip(1);
      run = 0;
      level = reader.ReadBit() == 1 ? -1 : 1;
    } else {
      var code = Mpeg1VlcTables.Coefficient.Read(ref reader);
      (run, level) = code == Mpeg1VlcTables.CoefficientEscape
        ? _ReadEscape(ref reader)
        : (Mpeg1VlcTables.RunOf(code), _Signed(ref reader, Mpeg1VlcTables.LevelOf(code)));
    }

    var position = run;
    _Store(block, position, level, quantiserScale, nonIntraMatrix, intra: false);
    _ReadCoefficients(ref reader, block, position, quantiserScale, nonIntraMatrix, intra: false);

    Mpeg1InverseDct.Transform(block);
  }

  /// <summary>Reads dct_coeff_next codes until End of Block.</summary>
  private static void _ReadCoefficients(
    ref Mpeg1BitReader reader, scoped Span<int> block, int position, int quantiserScale, byte[] matrix, bool intra) {
    for (; ; ) {
      var code = Mpeg1VlcTables.Coefficient.Read(ref reader);
      if (code == Mpeg1VlcTables.EndOfBlock)
        return;

      var (run, level) = code == Mpeg1VlcTables.CoefficientEscape
        ? _ReadEscape(ref reader)
        : (Mpeg1VlcTables.RunOf(code), _Signed(ref reader, Mpeg1VlcTables.LevelOf(code)));

      position += run + 1;
      _Store(block, position, level, quantiserScale, matrix, intra);
    }
  }

  private static void _Store(Span<int> block, int position, int level, int quantiserScale, byte[] matrix, bool intra) {
    if ((uint)position > 63)
      throw new InvalidDataException(
        $"The run-level codes of an MPEG-1 block reach scan position {position}, past the sixty-four a block holds.");

    var raster = Mpeg1Quantisation.ZigZag[position];
    block[raster] = intra
      ? Mpeg1Quantisation.DequantiseIntra(level, quantiserScale, matrix[raster])
      : Mpeg1Quantisation.DequantiseNonIntra(level, quantiserScale, matrix[raster]);
  }

  /// <summary>Applies the sign bit that follows every non-escape run-level code.</summary>
  private static int _Signed(ref Mpeg1BitReader reader, int level) => reader.ReadBit() == 1 ? -level : level;

  /// <summary>
  /// Reads the escape form: a six-bit run and a level that carries its own sign (11172-2, 2.4.3.7).
  /// </summary>
  /// <remarks>
  /// The level is eight bits read as a signed value, except that the two values which would be zero
  /// and minus one-hundred-and-twenty-eight instead introduce a further eight bits — that is how the
  /// range reaches ±255 without spending sixteen bits on the levels that fit in eight. It is the one
  /// place in the block layer where the sign is inside the value rather than a bit after it.
  /// </remarks>
  private static (int Run, int Level) _ReadEscape(ref Mpeg1BitReader reader) {
    var run = reader.ReadBits(6);
    var first = reader.ReadBits(8);

    var level = first switch {
      0 => reader.ReadBits(8),
      128 => reader.ReadBits(8) - 256,
      > 128 => first - 256,
      _ => first,
    };

    if (level == 0)
      throw new InvalidDataException(
        "An escaped run-level code in the MPEG-1 block layer decodes to a level of zero, which the standard forbids.");

    return (run, level);
  }
}
