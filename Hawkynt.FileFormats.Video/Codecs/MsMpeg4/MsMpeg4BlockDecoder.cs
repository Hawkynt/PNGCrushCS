using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The block layer of Microsoft's MPEG-4: run-level codes in, sixty-four reconstructed samples out.
/// </summary>
/// <remarks>
/// The same shape as ISO/IEC 14496-2's block layer, with the same three escape forms, the same inverse
/// scan and the same transform — those are taken from the MPEG-4 decoder beside this one rather than
/// written again. What differs is entirely in how the codes are read, and it differs by version:
/// <list type="bullet">
/// <item>the run-level tables are Microsoft's own except the pair versions 1 and 2 always use, which
/// are the standard's B-16 and B-17; version 3 states which of three it used, once per picture and
/// separately for luminance;</item>
/// <item>an intra block's DC always has its own code, where the standard lets a picture say the DC is
/// an ordinary coefficient above some quantiser; none of the three has such a field;</item>
/// <item>the DC step is eight at every quantiser in versions 1 and 2, and version 3 brings back a
/// table of steps that varies with the quantiser;</item>
/// <item>the escape form is chosen by a code of its own — <c>1</c>, <c>01</c>, <c>00</c> — where the
/// standard spends one bit and then another, and version 1 has only the third form and spends nothing
/// at all;</item>
/// <item>the second escape form adds one to the run it recovers in versions 1 and 3 and adds nothing
/// in version 2.</item>
/// </list>
/// That last one is a single <c>+ 1</c> and it is the kind of difference that produces a picture: the
/// run lands one coefficient early, every coefficient after it in the block is one position out, and
/// the block still decodes.
/// </remarks>
internal sealed class MsMpeg4BlockDecoder {

  /// <summary>The step an intra DC is quantised with in versions 1 and 2, at every quantiser.</summary>
  /// <remarks>
  /// Constant, unlike ISO/IEC 14496-2 Table 7-3 and unlike version 3, which brought the standard's
  /// varying step back. It is why an intra picture of one flat grey codes the same bits whatever
  /// quantiser it is given.
  /// </remarks>
  internal const int FixedDcStep = 8;

  /// <summary>
  /// How far past a block's last coefficient a scan position is pushed to say it is the last.
  /// </summary>
  /// <remarks>
  /// The run of a code that ends its block is carried a hundred and ninety-two past where it belongs,
  /// so that one comparison against sixty-two settles both "this is the last coefficient" and "this
  /// block overran", and neither costs a branch of its own in the common case. Taking it off again is
  /// what turns the flag back into a position.
  /// </remarks>
  private const int _LAST_COEFFICIENT_BIAS = 192;

  private readonly MsMpeg4Version _version;
  private readonly int _quantiser;
  private readonly int _dcTableIndex;
  private readonly MsMpeg4RunLevelTable _intraLuminanceCodes;
  private readonly MsMpeg4RunLevelTable _chrominanceCodes;
  private readonly MsMpeg4RunLevelTable _predictedCodes;

  /// <summary>The DC of the last intra block of each plane, which is all version 1 predicts from.</summary>
  private readonly int[] _lastDc = new int[3];

  internal MsMpeg4BlockDecoder(MsMpeg4Version version, MsMpeg4PictureHeader header) {
    ArgumentNullException.ThrowIfNull(header);

    this._version = version;
    this._quantiser = header.Quantiser;
    this._dcTableIndex = header.DcTableIndex;
    this._intraLuminanceCodes = MsMpeg4Tables.RunLevel[header.RunLevelTableIndex];
    this._chrominanceCodes = MsMpeg4Tables.RunLevel[3 + header.ChromaRunLevelTableIndex];
    this._predictedCodes = MsMpeg4Tables.RunLevel[3 + header.RunLevelTableIndex];

    this.ResetLastDc();
  }

  /// <summary>The step the luminance DC of an intra block is quantised with.</summary>
  internal int LuminanceDcStep => this._version == MsMpeg4Version.Version3
    ? MsMpeg4Data.Version3LuminanceDcStep[this._quantiser]
    : FixedDcStep;

  /// <summary>The same for a chrominance block.</summary>
  internal int ChrominanceDcStep => this._version == MsMpeg4Version.Version3
    ? MsMpeg4Data.Version3ChrominanceDcStep[this._quantiser]
    : FixedDcStep;

  /// <summary>
  /// Forgets what version 1 predicts a DC from, which happens at the start of every macroblock row.
  /// </summary>
  /// <remarks>
  /// Every row and not every slice, which is the one place version 1's prediction is coarser than the
  /// slice structure around it. Mid-grey is what it starts from, in the quantised units the DC is
  /// carried in.
  /// </remarks>
  internal void ResetLastDc() {
    var absent = MsMpeg4IntraPrediction.AbsentDc(FixedDcStep);
    this._lastDc[0] = absent;
    this._lastDc[1] = absent;
    this._lastDc[2] = absent;
  }

  /// <summary>
  /// Reads an intra block and reconstructs its samples.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="samples">Sixty-four samples in raster order, written by this method.</param>
  /// <param name="prediction">The picture's record of what the neighbouring intra blocks decoded to.</param>
  /// <param name="address">The macroblock, counted in raster order from zero.</param>
  /// <param name="index">Which of the macroblock's six blocks.</param>
  /// <param name="predictAc">Whether the macroblock asked for the first row or column to be predicted.</param>
  /// <param name="hasCoefficients">Whether the coded block pattern says this block carries any.</param>
  internal void ReadIntra(
    ref Mpeg4BitReader reader, scoped Span<int> samples, MsMpeg4IntraPrediction prediction,
    int address, int index, bool predictAc, bool hasCoefficients) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    var isLuminance = index < 4;
    var step = isLuminance ? this.LuminanceDcStep : this.ChrominanceDcStep;
    var codes = isLuminance ? this._intraLuminanceCodes : this._chrominanceCodes;

    if (this._version == MsMpeg4Version.Version1) {
      this._ReadVersion1Intra(ref reader, levels, prediction, address, index, hasCoefficients, codes);
    } else {
      // Settled before anything is read, because the gradient chooses the scan the coefficients are
      // written in as well as where the prediction comes from.
      var absentDc = MsMpeg4IntraPrediction.AbsentDc(step);
      var fromAbove = prediction.PredictsFromAbove(address, index, absentDc);
      var scan = !predictAc
        ? Mpeg4Quantisation.ZigZag
        : fromAbove ? Mpeg4Quantisation.AlternateHorizontal : Mpeg4Quantisation.AlternateVertical;

      levels[0] = this._ReadDcDifferential(ref reader, isLuminance);

      if (hasCoefficients)
        this._ReadCoefficients(ref reader, levels, scan, codes, intra: true);

      prediction.Apply(address, index, levels, predictAc, fromAbove, absentDc);
    }

    if (levels[0] < 0 || levels[0] > 256 * step)
      throw new InvalidDataException(
        $"An intra block of this Microsoft MPEG-4 picture reconstructs a DC of {levels[0]}, which is outside the "
        + $"nought to {256 * step} a DC quantised with a step of {step} can hold. The stream is corrupt, or it is "
        + "not the version the container says it is.");

    this._Dequantise(levels, samples, step);
    Mpeg4InverseDct.Transform(samples);
  }

  /// <summary>Reads the residual of a block of a predicted macroblock.</summary>
  internal void ReadInter(ref Mpeg4BitReader reader, scoped Span<int> samples) {
    Span<int> levels = stackalloc int[64];
    levels.Clear();

    // Reconstructed as they are read rather than afterwards: the codes of a predicted block state a
    // level already multiplied by the step, because the table a picture reads is chosen per quantiser.
    this._ReadCoefficients(ref reader, levels, Mpeg4Quantisation.ZigZag, this._predictedCodes, intra: false);

    levels.CopyTo(samples);

    // No mismatch control: that belongs to the standard's weighted quantisation method, which none of
    // these three has. The H.263 method's reconstruction levels are odd multiples of the step size,
    // which is what stops two conforming transforms drifting apart without it.
    Mpeg4InverseDct.Transform(samples);
  }

  // ============================================================================================
  // Version 1's intra blocks
  // ============================================================================================

  /// <summary>
  /// Reads an intra block the way version 1 does, which is the way MPEG-1 does.
  /// </summary>
  /// <remarks>
  /// The DC is a difference from the last one decoded in the same plane rather than from a neighbour
  /// chosen by a gradient, there is no alternating current prediction, and the scan is always the
  /// zig-zag. What the block decodes to is still recorded, because a picture may mix version 1's
  /// macroblocks with nothing else and the record costs nothing — but nothing reads it.
  /// </remarks>
  private void _ReadVersion1Intra(
    ref Mpeg4BitReader reader, scoped Span<int> levels, MsMpeg4IntraPrediction prediction,
    int address, int index, bool hasCoefficients, MsMpeg4RunLevelTable codes) {
    var plane = index < 4 ? 0 : index - 3;
    levels[0] = this._ReadDcDifferential(ref reader, index < 4) + this._lastDc[plane];
    this._lastDc[plane] = levels[0];

    if (hasCoefficients)
      this._ReadCoefficients(ref reader, levels, Mpeg4Quantisation.ZigZag, codes, intra: true);

    prediction.Record(address, index, levels);
  }

  // ============================================================================================
  // The DC
  // ============================================================================================

  /// <summary>
  /// Reads an intra block's DC differential.
  /// </summary>
  /// <remarks>
  /// Two forms. Versions 1 and 2 read one codeword that stands for the whole differential, out of a
  /// table built from ISO/IEC 14496-2's by inverting every bit and pulling the magnitude bits inside
  /// the codeword. Version 3 reads a magnitude out of one of two tables of its own and then a sign
  /// bit, with the largest entry an escape into eight plain bits and a sign — which is how it reaches
  /// a differential no table entry stands for.
  /// </remarks>
  private int _ReadDcDifferential(ref Mpeg4BitReader reader, bool isLuminance) {
    if (this._version != MsMpeg4Version.Version3)
      return MsMpeg4Tables.V2Dc[isLuminance ? 0 : 1].Read(ref reader) - MsMpeg4Tables.V2DcBias;

    var level = MsMpeg4Tables.Dc[this._dcTableIndex][isLuminance ? 0 : 1].Read(ref reader);
    if (level == MsMpeg4Tables.V3DcEscape)
      level = reader.ReadBits(8);
    else if (level == 0)
      return 0;

    return reader.ReadBit() == 1 ? -level : level;
  }

  // ============================================================================================
  // The coefficients
  // ============================================================================================

  /// <summary>
  /// Reads coefficient codes until one says it is the last of its block.
  /// </summary>
  /// <remarks>
  /// Whether the block is an intra one decides two things at once: where its first coefficient may sit
  /// — one, because the DC was read separately, against nought for a predicted block — and whether the
  /// levels come out quantised or already reconstructed. An intra block's stay quantised because the
  /// alternating current prediction is applied to them before anything is reconstructed.
  /// </remarks>
  private void _ReadCoefficients(
    ref Mpeg4BitReader reader, scoped Span<int> levels, int[] scan, MsMpeg4RunLevelTable codes, bool intra) {
    // An intra block's levels stay quantised, because the alternating current prediction is applied to
    // them before anything is reconstructed. A predicted block's are reconstructed here, because
    // nothing is predicted into them and the multiplication is one pass fewer.
    var multiplier = intra ? 1 : 2 * this._quantiser;
    var offset = intra ? 0 : (this._quantiser - 1) | 1;

    // The second escape form's run is one short in versions 1 and 3 and exact in version 2. One added
    // integer, and every coefficient after it lands one position out when it is wrong.
    var runOffset = intra || this._version == MsMpeg4Version.Version2 ? 0 : 1;

    var position = intra ? 0 : -1;

    for (; ; ) {
      var index = codes.Codes.Read(ref reader);
      int advance;
      int level;

      if (index != codes.EscapeIndex) {
        advance = _Advance(codes, index, 0);
        level = _Sign(ref reader, codes.LevelOf(index) * multiplier + offset);
      } else if (this._version == MsMpeg4Version.Version1 || reader.NextBits(1) == 0) {
        if (this._version == MsMpeg4Version.Version1 || reader.NextBits(2) == 0) {
          (advance, level) = _ReadThirdEscape(ref reader, this._version, multiplier, offset);
        } else {
          reader.Skip(2);
          (advance, level) = _ReadSecondEscape(ref reader, codes, multiplier, offset, runOffset);
        }
      } else {
        reader.Skip(1);
        (advance, level) = _ReadFirstEscape(ref reader, codes, multiplier, offset);
      }

      position += advance;

      // One comparison for two questions. A code that ends its block carries the bias, so it lands
      // here and nowhere else; a code that does not and still lands here has overrun its block, which
      // taking the bias off turns into a position outside nought to sixty-three.
      if (position <= 62) {
        levels[scan[position]] = level;
        continue;
      }

      position -= _LAST_COEFFICIENT_BIAS;
      if ((uint)position > 63)
        throw new InvalidDataException(
          $"The run-level codes of a block of this Microsoft MPEG-4 picture reach scan position "
          + $"{position + _LAST_COEFFICIENT_BIAS}, past the sixty-four a block holds, without stating a last "
          + "coefficient.");

      levels[scan[position]] = level;
      return;
    }
  }

  /// <summary>How far one table row moves the scan position, with the bias where the row ends its block.</summary>
  private static int _Advance(MsMpeg4RunLevelTable codes, int index, int extraRun)
    => codes.RunOf(index) + 1 + extraRun + (codes.IsLast(index) ? _LAST_COEFFICIENT_BIAS : 0);

  /// <summary>
  /// The first escape: the level is this table entry's plus the largest the table can state for the
  /// same run.
  /// </summary>
  private static (int Advance, int Level) _ReadFirstEscape(
    ref Mpeg4BitReader reader, MsMpeg4RunLevelTable codes, int multiplier, int offset) {
    var index = codes.Codes.Read(ref reader);
    _RefuseNestedEscape(codes, index);

    var level = codes.LevelOf(index) + codes.LargestLevel(codes.IsLast(index), codes.RunOf(index));

    return (_Advance(codes, index, 0), _Sign(ref reader, level * multiplier + offset));
  }

  /// <summary>
  /// The second escape: the run is this table entry's plus the longest the table can state for the
  /// same level.
  /// </summary>
  private static (int Advance, int Level) _ReadSecondEscape(
    ref Mpeg4BitReader reader, MsMpeg4RunLevelTable codes, int multiplier, int offset, int runOffset) {
    var index = codes.Codes.Read(ref reader);
    _RefuseNestedEscape(codes, index);

    var extra = codes.LargestRun(codes.IsLast(index), codes.LevelOf(index)) + runOffset;

    return (_Advance(codes, index, extra), _Sign(ref reader, codes.LevelOf(index) * multiplier + offset));
  }

  /// <summary>
  /// The third escape: the whole triple written out, and the only form version 1 has.
  /// </summary>
  /// <remarks>
  /// The level is eight bits two's complement and carries its own sign, so no sign bit follows. The
  /// step is applied here rather than by the caller because the sign has to be taken off first: a
  /// level of <c>-n</c> reconstructs as <c>-(n * step + offset)</c> and not as
  /// <c>-n * step + offset</c>, and a level of nought reconstructs as minus the offset — which is what
  /// the reference decoder produces and what an encoder's own reconstruction loop was built against.
  /// </remarks>
  private static (int Advance, int Level) _ReadThirdEscape(
    ref Mpeg4BitReader reader, MsMpeg4Version version, int multiplier, int offset) {
    if (version != MsMpeg4Version.Version1)
      reader.Skip(2);

    var last = reader.ReadBit() == 1;
    var run = reader.ReadBits(6);
    var level = reader.ReadBits(8);
    if (level >= 1 << 7)
      level -= 1 << 8;

    return (run + 1 + (last ? _LAST_COEFFICIENT_BIAS : 0),
      level > 0 ? level * multiplier + offset : level * multiplier - offset);
  }

  private static int _Sign(ref Mpeg4BitReader reader, int magnitude) => reader.ReadBit() == 1 ? -magnitude : magnitude;

  private static void _RefuseNestedEscape(MsMpeg4RunLevelTable codes, int index) {
    if (index == codes.EscapeIndex)
      throw new InvalidDataException(
        $"An escape code of {codes.Name} is followed by another escape code. The escape forms state how far past the "
        + "table one value is and take an ordinary code for the rest, so a second escape inside one is not a "
        + "codeword any encoder can produce.");
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  /// <summary>
  /// Turns an intra block's quantised levels into coefficients.
  /// </summary>
  /// <remarks>
  /// The DC by its own step and the rest by the H.263 rule, which is the split that makes the DC finer
  /// than the coefficients around it — an error in the DC spreads sideways into every block that
  /// predicts from it, where an error in a coefficient stays in its own block.
  /// </remarks>
  private void _Dequantise(scoped ReadOnlySpan<int> levels, scoped Span<int> samples, int step) {
    var multiplier = 2 * this._quantiser;
    var offset = (this._quantiser - 1) | 1;

    samples[0] = levels[0] * step;
    for (var i = 1; i < 64; ++i) {
      var level = levels[i];
      samples[i] = level == 0 ? 0 : level < 0 ? level * multiplier - offset : level * multiplier + offset;
    }
  }
}
