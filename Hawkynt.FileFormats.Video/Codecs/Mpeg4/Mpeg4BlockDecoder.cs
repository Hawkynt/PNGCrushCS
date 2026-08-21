using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The block layer of ISO/IEC 14496-2, clauses 6.2.7 and 7.4: run-level codes in, sixty-four
/// reconstructed samples out.
/// </summary>
/// <remarks>
/// Everything between the codes and the samples happens here, in the order clause 7.4 puts it: the
/// inverse scan, the prediction from neighbouring intra blocks, saturation, inverse quantisation,
/// the mismatch control, and the transform. The order is not interchangeable — the scan is chosen by
/// the prediction direction, so it has to be decided before a single coefficient is read, and the
/// prediction is over quantised values, so it has to happen before the dequantisation and not after.
/// <para/>
/// An intra block's DC is coded two ways and the picture header says which. Below the quantiser the
/// picture states, the DC has its own pair of variable-length code tables and its own place at the
/// front of the block; at or above it, the DC is an ordinary coefficient of the ordinary table and a
/// DC of zero is not coded at all. Reading the wrong one of the two is not a small error: the block
/// and everything after it in the picture are read from the wrong bit.
/// </remarks>
internal static class Mpeg4BlockDecoder {

  /// <summary>
  /// Reads an intra block and reconstructs its samples.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="samples">Sixty-four samples in raster order, written by this method.</param>
  /// <param name="prediction">The picture's record of what the neighbouring intra blocks decoded to.</param>
  /// <param name="address">The macroblock, counted in raster order from zero.</param>
  /// <param name="index">Which of the macroblock's six blocks.</param>
  /// <param name="quantiser">The quantiser in force for this macroblock.</param>
  /// <param name="layer">The video object layer, which says how to dequantise.</param>
  /// <param name="predictAc">Whether the picture asked for the first row or column to be predicted too.</param>
  /// <param name="hasCoefficients">Whether the coded block pattern says this block carries any.</param>
  /// <param name="useDcVlc">Whether the DC has its own code rather than being an ordinary coefficient.</param>
  internal static void ReadIntra(
    ref Mpeg4BitReader reader, scoped Span<int> samples, Mpeg4IntraPrediction prediction,
    int address, int index, int quantiser, Mpeg4VideoObjectLayer layer, bool predictAc, bool hasCoefficients,
    bool useDcVlc) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    // The direction is settled before anything is read, because it chooses the scan the coefficients
    // are read in as well as where the prediction comes from.
    var fromAbove = prediction.PredictsFromAbove(address, index);
    var scan = !predictAc
      ? Mpeg4Quantisation.ZigZag
      : fromAbove ? Mpeg4Quantisation.AlternateHorizontal : Mpeg4Quantisation.AlternateVertical;

    var first = 0;
    if (useDcVlc) {
      levels[0] = _ReadDcDifferential(ref reader, index < 4);
      first = 1;
    }

    if (hasCoefficients)
      _ReadCoefficients(ref reader, levels, scan, first, intra: true);

    var dcScaler = Mpeg4Quantisation.DcScaler(quantiser, index < 4);
    prediction.Apply(address, index, levels, quantiser, dcScaler, predictAc, fromAbove);

    // Saturation of the predicted coefficients, which is a step of its own in clause 7.4.3.4 and not
    // the same as the saturation of the dequantised ones below.
    for (var i = 0; i < 64; ++i)
      levels[i] = Mpeg4Quantisation.Clamp(levels[i]);

    samples[0] = Mpeg4Quantisation.Clamp(dcScaler * levels[0]);
    for (var i = 1; i < 64; ++i)
      samples[i] = layer.UsesMpegQuantisation
        ? Mpeg4Quantisation.DequantiseMpegIntra(levels[i], quantiser, layer.IntraQuantiserMatrix[i])
        : Mpeg4Quantisation.DequantiseH263(levels[i], quantiser);

    // No mismatch control here, and that is a deliberate departure from the standard's own summary —
    // see the remarks on Mpeg4Quantisation.ControlMismatch for what was measured and why.
    Mpeg4InverseDct.Transform(samples);
  }

  /// <summary>Reads an inter block: the residual added to a motion-compensated prediction.</summary>
  internal static void ReadInter(
    ref Mpeg4BitReader reader, scoped Span<int> samples, int quantiser, Mpeg4VideoObjectLayer layer) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    _ReadCoefficients(ref reader, levels, Mpeg4Quantisation.ZigZag, 0, intra: false);

    for (var i = 0; i < 64; ++i)
      samples[i] = layer.UsesMpegQuantisation
        ? Mpeg4Quantisation.DequantiseMpegNonIntra(levels[i], quantiser, layer.NonIntraQuantiserMatrix[i])
        : Mpeg4Quantisation.DequantiseH263(levels[i], quantiser);

    if (layer.UsesMpegQuantisation)
      Mpeg4Quantisation.ControlMismatch(samples);

    Mpeg4InverseDct.Transform(samples);
  }

  /// <summary>
  /// Reads the DC of an intra block from its own tables (ISO/IEC 14496-2, Tables B-13 to B-15).
  /// </summary>
  /// <remarks>
  /// The size names how many bits the value occupies and the value's top bit names its sign, in the
  /// mapping JPEG uses: the low half of the range stands for the negative values just below the
  /// positive ones. A size past eight is followed by a marker bit, because a DC that large could
  /// otherwise leave a run of zeroes long enough to look like a resync marker.
  /// </remarks>
  private static int _ReadDcDifferential(ref Mpeg4BitReader reader, bool isLuminance) {
    var size = (isLuminance ? Mpeg4VlcTables.LuminanceDcSize : Mpeg4VlcTables.ChrominanceDcSize).Read(ref reader);
    if (size == 0)
      return 0;

    if (size > 8) {
      var wide = reader.ReadBits(size);
      reader.ReadMarkerBit("after a DC differential of more than eight bits");
      return wide >= 1 << (size - 1) ? wide : wide - (1 << size) + 1;
    }

    var bits = reader.ReadBits(size);
    return bits >= 1 << (size - 1) ? bits : bits - (1 << size) + 1;
  }

  /// <summary>
  /// Reads coefficient codes until one says it is the last of its block (ISO/IEC 14496-2, 7.4.1).
  /// </summary>
  /// <param name="first">
  /// The scan position the first coefficient may occupy: zero for an inter block and for an intra
  /// block whose DC is an ordinary coefficient, one for an intra block whose DC was read separately.
  /// </param>
  private static void _ReadCoefficients(
    ref Mpeg4BitReader reader, scoped Span<int> levels, int[] scan, int first, bool intra) {
    var table = intra ? Mpeg4VlcTables.IntraCoefficient : Mpeg4VlcTables.InterCoefficient;
    var position = first - 1;

    for (; ; ) {
      var code = table.Read(ref reader);

      bool last;
      int run, level;
      if (code == Mpeg4VlcTables.CoefficientEscape)
        (last, run, level) = _ReadEscape(ref reader, table, intra);
      else
        (last, run, level) = _Row(ref reader, code, intra);

      position += run + 1;
      if ((uint)position > 63)
        throw new InvalidDataException(
          $"The run-level codes of an MPEG-4 block reach scan position {position}, past the sixty-four a block holds.");

      levels[scan[position]] = level;

      if (last)
        return;
    }
  }

  /// <summary>One ordinary coefficient code and the sign bit that follows it.</summary>
  private static (bool Last, int Run, int Level) _Row(ref Mpeg4BitReader reader, int code, bool intra) {
    var last = intra ? Mpeg4VlcTables.IntraIsLast[code] : Mpeg4VlcTables.InterIsLast[code];
    var run = intra ? Mpeg4VlcTables.IntraRun[code] : Mpeg4VlcTables.InterRun[code];
    int level = intra ? Mpeg4VlcTables.IntraLevel[code] : Mpeg4VlcTables.InterLevel[code];

    return (last, run, reader.ReadBit() == 1 ? -level : level);
  }

  /// <summary>
  /// Reads one of the three escape forms of ISO/IEC 14496-2 clause 7.4.1.3.
  /// </summary>
  /// <remarks>
  /// Three forms rather than one, and the first two are the interesting idea: instead of spending
  /// thirty bits to say a level or a run the table does not have, they say <i>how far past</i> the
  /// table's largest one it is and spend an ordinary code on the remainder. That is why the tables of
  /// largest levels and largest runs exist at all, and why they are derived here from the coefficient
  /// tables rather than transcribed again — the two would be two statements of the same hundred and
  /// two rows, and could disagree.
  /// </remarks>
  private static (bool Last, int Run, int Level) _ReadEscape(
    ref Mpeg4BitReader reader, Mpeg4VlcTable table, bool intra) {
    if (reader.ReadBit() == 0) {
      // Escape type 1: an ordinary code whose level is a difference from the largest the table holds
      // for that run.
      var nested = table.Read(ref reader);
      Mpeg4VlcTables.RefuseNestedEscape(nested);
      var (last, run, level) = _Row(ref reader, nested, intra);
      var largest = Mpeg4VlcTables.LargestLevel(intra, last, run);
      var magnitude = (level < 0 ? -level : level) + largest;
      return (last, run, level < 0 ? -magnitude : magnitude);
    }

    if (reader.ReadBit() == 0) {
      // Escape type 2: an ordinary code whose run is a difference from the largest the table holds
      // for that level.
      var nested = table.Read(ref reader);
      Mpeg4VlcTables.RefuseNestedEscape(nested);
      var (last, run, level) = _Row(ref reader, nested, intra);
      var largest = Mpeg4VlcTables.LargestRun(intra, last, level < 0 ? -level : level);
      return (last, run + largest + 1, level);
    }

    // Escape type 3: the whole triple written out, with a marker bit on each side of the level so
    // that a large one cannot leave a run of zeroes long enough to look like a resync marker.
    {
      var last = reader.ReadBit() == 1;
      var run = reader.ReadBits(6);
      reader.ReadMarkerBit("before an escaped coefficient level");
      var level = reader.ReadBits(12);
      reader.ReadMarkerBit("after an escaped coefficient level");

      if (level >= 1 << 11)
        level -= 1 << 12;

      if (level == 0)
        throw new InvalidDataException(
          "An escaped coefficient code in the MPEG-4 block layer states a level of zero, which ISO/IEC 14496-2 "
          + "Table B-18 forbids.");

      if (level == -2048)
        throw new InvalidDataException(
          "An escaped coefficient code in the MPEG-4 block layer states a level of -2048, which ISO/IEC 14496-2 "
          + "Table B-18 forbids.");

      return (last, run, level);
    }
  }
}
