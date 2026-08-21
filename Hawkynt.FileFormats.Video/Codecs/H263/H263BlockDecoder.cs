using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// The block layer of ITU-T H.263 clause 5.4: run-level codes in, sixty-four reconstructed samples
/// out.
/// </summary>
/// <remarks>
/// Every block leaves here dequantised, in raster order and transformed. Handing back raw levels
/// would put the dequantisation somewhere else, and the dequantisation is exactly the step a decoder
/// can be missing while still producing a picture-shaped result — so it lives against the code that
/// reads the levels, where its absence would be conspicuous.
/// <para/>
/// H.263's coefficient codes carry an end-of-block inside them rather than beside them: every code
/// says whether it is the last coefficient of its block, so there is no separate End of Block symbol
/// to look for and a block always ends on a coefficient. That is the one structural difference from
/// the MPEG-1 block layer, and it is why a coded block here can never be empty.
/// </remarks>
internal static class H263BlockDecoder {

  /// <summary>
  /// Reads an intra-coded block: an eight-bit DC value and, when the pattern says so, the
  /// coefficients after it.
  /// </summary>
  /// <remarks>
  /// The DC is a value and not a difference. H.263 has no prediction between the DC coefficients of
  /// neighbouring blocks — that is the Advanced Intra Coding mode of Annex I, and this decoder
  /// refuses a picture that asks for it — so nothing has to be threaded from block to block here, and
  /// a slice boundary resets nothing because there is nothing to reset.
  /// </remarks>
  internal static void ReadIntra(
    ref H263BitReader reader, scoped Span<int> block, int quantiser, bool hasCoefficients, bool wideEscapeLevel) {
    block.Clear();
    block[0] = H263Quantisation.DequantiseIntraDc(_ReadIntraDc(ref reader));

    if (hasCoefficients)
      _ReadCoefficients(ref reader, block, 0, quantiser, wideEscapeLevel);

    H263InverseDct.Transform(block);
  }

  /// <summary>Reads an inter-coded block: the residual added to a motion-compensated prediction.</summary>
  internal static void ReadInter(
    ref H263BitReader reader, scoped Span<int> block, int quantiser, bool wideEscapeLevel) {
    block.Clear();
    _ReadCoefficients(ref reader, block, -1, quantiser, wideEscapeLevel);
    H263InverseDct.Transform(block);
  }

  /// <summary>
  /// Reads INTRADC (ITU-T H.263, 5.4.1).
  /// </summary>
  /// <remarks>
  /// Two of the two hundred and fifty-six values are not codes. Zero and one hundred and twenty-eight
  /// are left out because a block full of them would help the coded data look like a start code, and
  /// the level the second of them would have carried is coded as two hundred and fifty-five instead —
  /// which is why the dequantisation is not simply eight times the field.
  /// </remarks>
  private static int _ReadIntraDc(ref H263BitReader reader) {
    var value = reader.ReadBits(8);
    if (value is 0 or 128)
      throw new InvalidDataException(
        $"An H.263 intra block states INTRADC {value}, which ITU-T H.263 5.4.1 leaves unused. The level of 1024 that "
        + "128 would have carried is coded as 255 instead.");

    return value;
  }

  /// <summary>
  /// Reads TCOEF codes until one says it is the last of its block (ITU-T H.263, 5.4.2).
  /// </summary>
  /// <param name="position">
  /// The scan position the last coefficient occupied, so that the next code's run counts from the one
  /// after it. Minus one for an inter block, where nothing has been placed yet, and zero for an intra
  /// block, whose DC already occupies scan position zero.
  /// </param>
  private static void _ReadCoefficients(
    ref H263BitReader reader, scoped Span<int> block, int position, int quantiser, bool wideEscapeLevel) {
    for (; ; ) {
      var code = H263VlcTables.Coefficient.Read(ref reader);

      bool last;
      int run, level;
      if (code == H263VlcTables.CoefficientEscape) {
        (last, run, level) = _ReadEscape(ref reader, wideEscapeLevel);
      } else {
        last = H263VlcTables.CoefficientIsLast[code];
        run = H263VlcTables.CoefficientRun[code];
        level = H263VlcTables.CoefficientLevel[code];
        if (reader.ReadBit() == 1)
          level = -level;
      }

      position += run + 1;
      if ((uint)position > 63)
        throw new InvalidDataException(
          $"The run-level codes of an H.263 block reach scan position {position}, past the sixty-four a block holds.");

      block[H263Quantisation.ZigZag[position]] = H263Quantisation.Dequantise(level, quantiser);

      if (last)
        return;
    }
  }

  /// <summary>
  /// Reads the escape form of a coefficient code, in whichever of the two shapes the picture header
  /// settled on.
  /// </summary>
  /// <remarks>
  /// H.263's shape (5.4.2 and Table 17) is a last flag, a six-bit run and an eight-bit level that
  /// carries its own sign inside the value rather than as a bit after it — the one place in the block
  /// layer where that is so. Two of its values are not codes: zero, which is not a coefficient, and
  /// minus one hundred and twenty-eight, which the Recommendation reserves for the Modified
  /// Quantization mode of Annex T.
  /// <para/>
  /// A Sorenson Spark stream of version 1 puts a bit in front of all that, choosing between a level
  /// of seven bits and one of eleven. That widens the range a level can take from the ±127 of H.263
  /// to ±1023 at the cost of one bit on the common case, and it is why a version 1 stream read as an
  /// H.263 one keeps its place exactly until the first block that needs an escape and then loses the
  /// bitstream completely.
  /// </remarks>
  private static (bool Last, int Run, int Level) _ReadEscape(ref H263BitReader reader, bool wideLevel) {
    if (wideLevel) {
      var bits = reader.ReadBit() == 1 ? 11 : 7;
      var last = reader.ReadBit() == 1;
      var run = reader.ReadBits(6);
      var value = reader.ReadBits(bits);
      var sign = 1 << (bits - 1);
      return (last, run, value >= sign ? value - 2 * sign : value);
    }

    {
      var last = reader.ReadBit() == 1;
      var run = reader.ReadBits(6);
      var level = reader.ReadBits(8);
      if (level > 127)
        level -= 256;

      if (level == 0)
        throw new InvalidDataException(
          "An escaped coefficient code in the H.263 block layer states a level of zero, which ITU-T H.263 5.4.2 "
          + "forbids.");

      if (level == -128)
        throw new NotSupportedException(
          "An escaped coefficient code in the H.263 block layer states a level of -128, which ITU-T H.263 5.4.2 "
          + "allows only in the Modified Quantization mode of Annex T. That mode is not implemented.");

      return (last, run, level);
    }
  }
}
