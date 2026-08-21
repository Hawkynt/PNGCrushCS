using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The block layer of Microsoft's MPEG-4 version 2: run-level codes in, sixty-four reconstructed
/// samples out.
/// </summary>
/// <remarks>
/// The same block layer ISO/IEC 14496-2 has, with the same three escape forms, the same inverse scan,
/// the same H.263 inverse quantisation and the same transform — the pieces are taken from the MPEG-4
/// decoder beside this one rather than written again. What differs is small and entirely in how the
/// codes are read:
/// <list type="bullet">
/// <item>an intra block's DC always has its own code, where the standard lets a picture say the DC is
/// an ordinary coefficient above some quantiser; version 2 has no such field;</item>
/// <item>the DC step is eight at every quantiser, where the standard varies it by a table;</item>
/// <item>the escape form is chosen by a code of its own — <c>1</c>, <c>01</c>, <c>00</c> for the three
/// forms — where the standard spends one bit and then another;</item>
/// <item>the second escape form adds nothing to the run it recovers, where the standard adds one.</item>
/// </list>
/// That last one is a single <c>+ 1</c> and it is the kind of difference that produces a picture: the
/// run lands one coefficient early, every coefficient after it in the block is one position out, and
/// the block still decodes.
/// </remarks>
internal static class MsMpeg4BlockDecoder {

  /// <summary>The step an intra DC is quantised with, at every quantiser this format has.</summary>
  /// <remarks>
  /// Constant, unlike ISO/IEC 14496-2 Table 7-3 and unlike Microsoft's own version 3, which brought
  /// the standard's varying step back. It is why an intra picture of one flat grey codes the same bits
  /// whatever quantiser it is given, which is how the constant was recognised in the first place.
  /// </remarks>
  internal const int DcStep = 8;

  /// <summary>
  /// Reads an intra block and reconstructs its samples.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="samples">Sixty-four samples in raster order, written by this method.</param>
  /// <param name="prediction">The picture's record of what the neighbouring intra blocks decoded to.</param>
  /// <param name="address">The macroblock, counted in raster order from zero.</param>
  /// <param name="index">Which of the macroblock's six blocks.</param>
  /// <param name="quantiser">The quantiser the picture states.</param>
  /// <param name="predictAc">Whether the macroblock asked for the first row or column to be predicted.</param>
  /// <param name="hasCoefficients">Whether the coded block pattern says this block carries any.</param>
  internal static void ReadIntra(
    ref Mpeg4BitReader reader, scoped Span<int> samples, MsMpeg4IntraPrediction prediction,
    int address, int index, int quantiser, bool predictAc, bool hasCoefficients) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    // Settled before anything is read, because it chooses the scan the coefficients are written in as
    // well as where the prediction comes from.
    var fromAbove = prediction.PredictsFromAbove(address, index);
    var scan = !predictAc
      ? Mpeg4Quantisation.ZigZag
      : fromAbove ? Mpeg4Quantisation.AlternateHorizontal : Mpeg4Quantisation.AlternateVertical;

    levels[0] = _ReadDcDifferential(ref reader, index < 4);

    if (hasCoefficients)
      _ReadCoefficients(ref reader, levels, scan, first: 1, intra: index < 4);

    prediction.Apply(address, index, levels, predictAc, fromAbove);

    for (var i = 0; i < 64; ++i)
      levels[i] = Mpeg4Quantisation.Clamp(levels[i]);

    samples[0] = Mpeg4Quantisation.Clamp(DcStep * levels[0]);
    for (var i = 1; i < 64; ++i)
      samples[i] = Mpeg4Quantisation.DequantiseH263(levels[i], quantiser);

    Mpeg4InverseDct.Transform(samples);
  }

  /// <summary>Reads the residual of a block of a predicted macroblock.</summary>
  internal static void ReadInter(ref Mpeg4BitReader reader, scoped Span<int> samples, int quantiser) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    _ReadCoefficients(ref reader, levels, Mpeg4Quantisation.ZigZag, first: 0, intra: false);

    for (var i = 0; i < 64; ++i)
      samples[i] = Mpeg4Quantisation.DequantiseH263(levels[i], quantiser);

    // No mismatch control: that belongs to the standard's weighted quantisation method, which this
    // format does not have. The H.263 method's reconstruction levels are odd multiples of the step
    // size, which is what stops two conforming transforms drifting apart without it.
    Mpeg4InverseDct.Transform(samples);
  }

  /// <summary>
  /// Reads an intra block's DC differential.
  /// </summary>
  /// <remarks>
  /// The size names how many bits the value occupies and the value's top bit names its sign, in the
  /// mapping JPEG uses. A size past eight is followed by a marker bit, the same as in ISO/IEC 14496-2.
  /// The tables are the standard's with every bit inverted — see
  /// <see cref="MsMpeg4VlcTables.LuminanceDcSize"/>.
  /// </remarks>
  private static int _ReadDcDifferential(ref Mpeg4BitReader reader, bool isLuminance) {
    var size = (isLuminance ? MsMpeg4VlcTables.LuminanceDcSize : MsMpeg4VlcTables.ChrominanceDcSize).Read(ref reader);
    if (size == 0)
      return 0;

    var bits = reader.ReadBits(size);
    if (size > 8)
      reader.ReadMarkerBit("after a DC differential of more than eight bits");

    return bits >= 1 << (size - 1) ? bits : bits - (1 << size) + 1;
  }

  /// <summary>
  /// Reads coefficient codes until one says it is the last of its block.
  /// </summary>
  /// <param name="first">
  /// The scan position the first coefficient may occupy: one for an intra block, whose DC was read
  /// separately, and nought for a block of a predicted macroblock.
  /// </param>
  /// <param name="intra">
  /// Whether to read Table B-16 rather than Table B-17. Only an intra <i>luminance</i> block reads
  /// B-16: an intra chrominance block reads B-17, the same table every block of a predicted
  /// macroblock reads. That split is not something a reader of the standard would guess — there the
  /// table follows the macroblock — and it was found by putting a single known coefficient in one
  /// chrominance block and reading the codeword back out.
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
          $"The run-level codes of a Microsoft MPEG-4 version 2 block reach scan position {position}, past the "
          + "sixty-four a block holds.");

      levels[scan[position]] = level;

      if (last)
        return;
    }
  }

  private static (bool Last, int Run, int Level) _Row(ref Mpeg4BitReader reader, int code, bool intra) {
    var last = intra ? Mpeg4VlcTables.IntraIsLast[code] : Mpeg4VlcTables.InterIsLast[code];
    var run = intra ? Mpeg4VlcTables.IntraRun[code] : Mpeg4VlcTables.InterRun[code];
    int level = intra ? Mpeg4VlcTables.IntraLevel[code] : Mpeg4VlcTables.InterLevel[code];

    return (last, run, reader.ReadBit() == 1 ? -level : level);
  }

  /// <summary>
  /// Reads one of the three escape forms.
  /// </summary>
  /// <remarks>
  /// The same three the standard has and the same idea behind the first two — say how far past the
  /// table's largest level, or its largest run, the real one is, and spend an ordinary code on the
  /// remainder — but reached by a code of their own rather than by a bit and then another, and with
  /// the second form's <c>+ 1</c> absent.
  /// <para/>
  /// The bounds are derived from the coefficient tables rather than transcribed beside them, which is
  /// what the MPEG-4 decoder does for the same reason: the two would be two statements of the same
  /// hundred and two rows and could disagree.
  /// </remarks>
  private static (bool Last, int Run, int Level) _ReadEscape(
    ref Mpeg4BitReader reader, Mpeg4VlcTable table, bool intra) {
    if (reader.ReadBit() == 1) {
      var nested = table.Read(ref reader);
      Mpeg4VlcTables.RefuseNestedEscape(nested);
      var (last, run, level) = _Row(ref reader, nested, intra);
      var largest = Mpeg4VlcTables.LargestLevel(intra, last, run);
      var magnitude = (level < 0 ? -level : level) + largest;
      return (last, run, level < 0 ? -magnitude : magnitude);
    }

    if (reader.ReadBit() == 1) {
      var nested = table.Read(ref reader);
      Mpeg4VlcTables.RefuseNestedEscape(nested);
      var (last, run, level) = _Row(ref reader, nested, intra);
      var largest = Mpeg4VlcTables.LargestRun(intra, last, level < 0 ? -level : level);

      // No "+ 1" here, which is where this parts company with ISO/IEC 14496-2 7.4.1.3.
      return (last, run + largest, level);
    }

    {
      var last = reader.ReadBit() == 1;
      var run = reader.ReadBits(6);
      var level = reader.ReadBits(8);
      if (level >= 1 << 7)
        level -= 1 << 8;

      return (last, run, level);
    }
  }
}
