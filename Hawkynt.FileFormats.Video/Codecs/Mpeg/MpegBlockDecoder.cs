using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The block layer of ISO/IEC 11172-2, 2.4.2.7 and ISO/IEC 13818-2, 6.2.6: run-level codes in,
/// sixty-four reconstructed coefficients out.
/// </summary>
/// <remarks>
/// Every block leaves here dequantised, in raster order and transformed. Handing back raw levels
/// would put the dequantisation somewhere else, and the dequantisation is exactly the step a decoder
/// can be missing while still producing a picture-shaped result — so it lives against the code that
/// reads the levels, where its absence would be conspicuous.
/// <para/>
/// The two standards differ here in four places and the differences do not announce themselves. The
/// scan may be the alternate one; intra blocks may read Table B.15 instead of B.14; the escape
/// carries a twelve-bit level rather than MPEG-1's eight-with-an-escape-of-its-own; and the
/// dequantised block has its parity corrected once at the end instead of every coefficient being
/// forced odd. Each is carried in <see cref="MpegBlockRules"/> rather than being decided here.
/// </remarks>
internal static class MpegBlockDecoder {

  /// <summary>
  /// Reads an intra-coded block and returns the DC value that becomes the next block's predictor.
  /// </summary>
  /// <remarks>
  /// The predictor is in coded levels and not in reconstructed coefficients, which is what lets one
  /// piece of code serve both standards: MPEG-1 always multiplies a DC level by eight, MPEG-2
  /// multiplies it by eight, four, two or one as <c>intra_dc_precision</c> says, and the prediction
  /// itself is of the level either way.
  /// <para/>
  /// It is threaded through as a parameter rather than being a field of anything because the
  /// predictor is reset at every slice and by every non-intra macroblock, and a predictor held in an
  /// object outlives both of those resets by accident far more easily than a parameter does.
  /// </remarks>
  internal static int ReadIntra(
    ref MpegBitReader reader, scoped Span<int> block, bool isChroma, int quantiserScale, byte[] intraMatrix,
    int dcPredictor, MpegBlockRules rules) {
    block.Clear();

    var size = rules.DcSizeTable(isChroma).Read(ref reader);

    var differential = 0;
    if (size > 0) {
      var bits = reader.ReadBits(size);

      // A leading zero means the value is negative, and the standard's mapping is the same one JPEG
      // uses: the low half of the range stands for the negative values just below the positive ones.
      differential = (bits & (1 << (size - 1))) != 0 ? bits : bits - (1 << size) + 1;
    }

    var dc = dcPredictor + differential;
    block[0] = _SaturateCoefficient(dc * rules.IntraDcMultiplier);

    // The DC occupies scan position zero, so the first run-level code that follows counts its run
    // from there. Intra blocks have no dct_coeff_first: their first coefficient code is already a
    // subsequent one, because the DC was the first.
    var table = rules.UseIntraCoefficientTable ? MpegVlcTables.IntraCoefficient : MpegVlcTables.Coefficient;
    _ReadCoefficients(ref reader, block, table, 0, quantiserScale, intraMatrix, intra: true, rules);

    if (rules.IsMpeg2)
      MpegQuantisation.CorrectMismatch(block);

    MpegInverseDct.Transform(block);
    return dc;
  }

  /// <summary>Reads a non-intra-coded block: the residual added to a motion-compensated prediction.</summary>
  internal static void ReadNonIntra(
    ref MpegBitReader reader, scoped Span<int> block, int quantiserScale, byte[] nonIntraMatrix, MpegBlockRules rules) {
    block.Clear();

    // dct_coeff_first: as the first coefficient of a block a leading one is the whole code and means
    // a level of one, because there is no End of Block to compete with it for that bit. Every other
    // code in Table B.14 begins with a zero and is read from the table unchanged. Non-intra blocks
    // read Table B.14 in both standards, so this spelling is not conditional on anything.
    int run, level;
    if (reader.NextBits(1) == 1) {
      reader.Skip(1);
      run = 0;
      level = reader.ReadBit() == 1 ? -1 : 1;
    } else {
      var code = MpegVlcTables.Coefficient.Read(ref reader);
      (run, level) = code == MpegVlcTables.CoefficientEscape
        ? _ReadEscape(ref reader, rules)
        : (MpegVlcTables.RunOf(code), _Signed(ref reader, MpegVlcTables.LevelOf(code)));
    }

    var position = run;
    _Store(block, position, level, quantiserScale, nonIntraMatrix, intra: false, rules);
    _ReadCoefficients(
      ref reader, block, MpegVlcTables.Coefficient, position, quantiserScale, nonIntraMatrix, intra: false, rules);

    if (rules.IsMpeg2)
      MpegQuantisation.CorrectMismatch(block);

    MpegInverseDct.Transform(block);
  }

  /// <summary>Reads coefficient codes until End of Block.</summary>
  private static void _ReadCoefficients(
    ref MpegBitReader reader, scoped Span<int> block, MpegVlcTable table, int position, int quantiserScale,
    byte[] matrix, bool intra, MpegBlockRules rules) {
    for (; ; ) {
      var code = table.Read(ref reader);
      if (code == MpegVlcTables.EndOfBlock)
        return;

      var (run, level) = code == MpegVlcTables.CoefficientEscape
        ? _ReadEscape(ref reader, rules)
        : (MpegVlcTables.RunOf(code), _Signed(ref reader, MpegVlcTables.LevelOf(code)));

      position += run + 1;
      _Store(block, position, level, quantiserScale, matrix, intra, rules);
    }
  }

  private static void _Store(
    Span<int> block, int position, int level, int quantiserScale, byte[] matrix, bool intra, MpegBlockRules rules) {
    if ((uint)position > 63)
      throw new InvalidDataException(
        $"The run-level codes of an MPEG block reach scan position {position}, past the sixty-four a block holds.");

    var raster = rules.Scan[position];
    block[raster] = rules.IsMpeg2
      ? intra
        ? MpegQuantisation.DequantiseIntraMpeg2(level, quantiserScale, matrix[raster])
        : MpegQuantisation.DequantiseNonIntraMpeg2(level, quantiserScale, matrix[raster])
      : intra
        ? MpegQuantisation.DequantiseIntraMpeg1(level, quantiserScale, matrix[raster])
        : MpegQuantisation.DequantiseNonIntraMpeg1(level, quantiserScale, matrix[raster]);
  }

  /// <summary>Applies the sign bit that follows every non-escape run-level code.</summary>
  private static int _Signed(ref MpegBitReader reader, int level) => reader.ReadBit() == 1 ? -level : level;

  private static (int Run, int Level) _ReadEscape(ref MpegBitReader reader, MpegBlockRules rules)
    => rules.IsMpeg2 ? _ReadEscapeMpeg2(ref reader) : _ReadEscapeMpeg1(ref reader);

  /// <summary>
  /// Reads the MPEG-1 escape form: a six-bit run and a level that carries its own sign
  /// (11172-2, 2.4.3.7).
  /// </summary>
  /// <remarks>
  /// The level is eight bits read as a signed value, except that the two values which would be zero
  /// and minus one-hundred-and-twenty-eight instead introduce a further eight bits — that is how the
  /// range reaches ±255 without spending sixteen bits on the levels that fit in eight. It is the one
  /// place in the block layer where the sign is inside the value rather than a bit after it.
  /// </remarks>
  private static (int Run, int Level) _ReadEscapeMpeg1(ref MpegBitReader reader) {
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

  /// <summary>
  /// Reads the MPEG-2 escape form: a six-bit run and a twelve-bit two's complement level
  /// (13818-2, Table B.16).
  /// </summary>
  /// <remarks>
  /// MPEG-2 dropped MPEG-1's escape-within-an-escape and spends a flat twelve bits instead, which is
  /// two fewer than the sixteen the double form costs for the levels that need it and six more than
  /// the eight it costs for the levels that do not. The two forms are the same length of code up to
  /// this point, so a decoder that reads the wrong one does not fail at the escape — it reads the
  /// next few codes out of the middle of a level and produces a block of noise.
  /// </remarks>
  private static (int Run, int Level) _ReadEscapeMpeg2(ref MpegBitReader reader) {
    var run = reader.ReadBits(6);
    var bits = reader.ReadBits(12);
    var level = bits >= 2048 ? bits - 4096 : bits;

    if (level is 0 or -2048)
      throw new InvalidDataException(
        $"An escaped run-level code in the MPEG-2 block layer states signed_level {level}, which ISO/IEC 13818-2 "
        + "Table B.16 forbids; the range is -2047 to 2047 excluding zero.");

    return (run, level);
  }

  /// <summary>Saturates a reconstructed coefficient to the range 13818-2 7.4.3 defines it over.</summary>
  private static int _SaturateCoefficient(int value) => value < -2048 ? -2048 : value > 2047 ? 2047 : value;
}
