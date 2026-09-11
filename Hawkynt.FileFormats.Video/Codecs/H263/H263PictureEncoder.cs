using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Encodes one baseline H.263 picture, intra or predicted: picture header, macroblocks and blocks.
/// </summary>
/// <remarks>
/// This writer deliberately emits no optional group-of-block headers. H.263 clause 4.2.1 permits
/// headers for GOBs after the first to be empty depending on encoder strategy; with none present the
/// macroblock run simply continues across every group boundary, which the decoder beside this already
/// handles. No annex mode is signalled and one fixed picture quantiser serves the whole picture.
/// <para/>
/// A predicted picture chooses per macroblock between a forward prediction on a searched vector and
/// not transmitting the macroblock at all -- COD, clause 5.3.1 -- and its vectors are coded as the
/// difference from the median of three neighbours, which is 6.1.1's predictor and not the previous
/// macroblock's vector that MPEG-1 uses. That difference matters here: the predictor a decoder forms
/// depends on macroblocks two rows apart, so an encoder that kept its own running vector instead
/// would agree with the decoder on the first macroblock of a picture and on nothing after it.
/// </remarks>
internal sealed class H263PictureEncoder {

  /// <summary>
  /// PSC followed by GN=0, ITU-T H.263 clause 5.1.1: the 17-bit PSC has numeric value one and the
  /// following five zero bits move that one five places left in the complete 22-bit picture start field.
  /// </summary>
  private const int _PICTURE_START_CODE = 1 << 5;
  private const int _PICTURE_START_CODE_LENGTH = 22;

  /// <summary>How far a motion search looks, in whole pixels, around the predicted vector.</summary>
  /// <remarks>
  /// Clause 6.1.1 gives a baseline vector the range -16 to 15.5 whole pixels, and the search is kept
  /// inside it: a vector beyond what Table 14 can spell would come back from the decoder as the other
  /// member of the code's pair rather than as the vector that was searched for.
  /// </remarks>
  private const int _SEARCH_RANGE = 15;

  /// <summary>The range a reconstructed vector may occupy, in half-pixel units.</summary>
  private const int _VECTOR_LIMIT = 32;

  private readonly int _sourceFormat;
  private readonly int _temporalReference;
  private readonly int _quantiser;
  private readonly H263Frame _source;
  private readonly H263Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly H263BitWriter _writer = new();

  /// <summary>Every macroblock's vector, kept because 6.1.1's predictor is a median of neighbours.</summary>
  private readonly short[] _vectorX;
  private readonly short[] _vectorY;

  internal H263PictureEncoder(
    int sourceFormat, int temporalReference, int quantiser, H263Frame source, H263Frame? reference = null) {
    if (sourceFormat is < 1 or > 5)
      throw new ArgumentOutOfRangeException(nameof(sourceFormat));

    if (quantiser is < 1 or > 31)
      throw new ArgumentOutOfRangeException(nameof(quantiser));

    this._sourceFormat = sourceFormat;
    this._temporalReference = temporalReference & 0xFF;
    this._quantiser = quantiser;
    this._source = source ?? throw new ArgumentNullException(nameof(source));
    this._reference = reference;
    this._macroblockWidth = source.LumaWidth / 16;
    this._macroblockHeight = source.LumaHeight / 16;
    this._vectorX = new short[this._macroblockWidth * this._macroblockHeight];
    this._vectorY = new short[this._macroblockWidth * this._macroblockHeight];
  }

  /// <summary>Encodes the complete picture and returns its byte-aligned elementary-stream payload.</summary>
  internal byte[] Encode() {
    this._WritePictureHeader();

    Span<int> samples = stackalloc int[64];
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> direct = stackalloc int[6];
    Span<bool> coded = stackalloc bool[6];

    var count = this._macroblockWidth * this._macroblockHeight;
    for (var address = 0; address < count; ++address) {
      if (this._reference != null) {
        this._WritePredictedMacroblock(address, levels, coded);
        continue;
      }

      var luminancePattern = 0;
      var chrominancePattern = 0;

      for (var index = 0; index < 6; ++index) {
        this._ReadSource(samples, address, index);
        var block = levels.Slice(index * 64, 64);
        coded[index] = H263BlockEncoder.QuantiseIntra(samples, this._quantiser, block, out direct[index]);

        if (!coded[index])
          continue;

        if (index < 4)
          luminancePattern |= 1 << (3 - index);
        else
          chrominancePattern |= 1 << (5 - index);
      }

      this._writer.WriteCode(H263VlcWriter.IntraMacroblockType(chrominancePattern));
      this._writer.WriteCode(H263VlcWriter.LuminancePattern(luminancePattern));

      for (var index = 0; index < 6; ++index)
        H263BlockEncoder.WriteIntra(
          this._writer, direct[index], levels.Slice(index * 64, 64), coded[index]);
    }

    return this._writer.ToArray();
  }

  /// <summary>Writes one macroblock of a predicted picture, or nothing at all when COD says so.</summary>
  private void _WritePredictedMacroblock(int address, scoped Span<int> levels, scoped Span<bool> coded) {
    var reference = this._reference!;
    var (vectorX, vectorY) = this._SearchMotion(reference, address);

    var luminancePattern = 0;
    var chrominancePattern = 0;
    for (var index = 0; index < 6; ++index) {
      this._ReadResidual(reference, address, index, vectorX, vectorY, levels.Slice(index * 64, 64), out coded[index]);
      if (!coded[index])
        continue;

      if (index < 4)
        luminancePattern |= 1 << (3 - index);
      else
        chrominancePattern |= 1 << (5 - index);
    }

    // COD, clause 5.3.1: a set bit and the macroblock carries nothing at all -- it is the co-located
    // macroblock of the reference, and 6.1.1 has every later predictor treat its vector as zero. It
    // is what makes a predicted picture cheap, and it is only available when there is genuinely
    // nothing to say: no displacement and no residual.
    if (vectorX == 0 && vectorY == 0 && luminancePattern == 0 && chrominancePattern == 0) {
      this._writer.WriteBit(1);
      this._vectorX[address] = 0;
      this._vectorY[address] = 0;
      return;
    }

    this._writer.WriteBit(0);
    this._writer.WriteCode(H263VlcWriter.PredictedMacroblockType(chrominancePattern));

    // CBPY states the complement of an inter macroblock's luminance pattern, which is the one place
    // the field means something different in a predicted picture than in an intra one.
    this._writer.WriteCode(H263VlcWriter.LuminancePattern(luminancePattern ^ 0xF));

    var predictedX = this._PredictVector(this._vectorX, address);
    var predictedY = this._PredictVector(this._vectorY, address);
    this._writer.WriteCode(H263VlcWriter.MotionVectorDifference(vectorX - predictedX));
    this._writer.WriteCode(H263VlcWriter.MotionVectorDifference(vectorY - predictedY));

    this._vectorX[address] = (short)vectorX;
    this._vectorY[address] = (short)vectorY;

    for (var index = 0; index < 6; ++index)
      if (coded[index])
        H263BlockEncoder.WriteInter(this._writer, levels.Slice(index * 64, 64));
  }

  /// <summary>
  /// Finds the whole-pixel vector whose 16x16 luminance prediction differs least from the source, in
  /// the half-pixel units the field counts.
  /// </summary>
  /// <remarks>
  /// The search is whole-pixel, so the vector it returns is always even and its luminance prediction
  /// is a plain copy rather than an interpolation. The zero vector is the incumbent and is only
  /// displaced by a strictly better one: a macroblock that did not move has to come out of here with
  /// a zero vector or COD cannot leave it out, and on flat or repeating content many vectors score
  /// identically.
  /// </remarks>
  private (int X, int Y) _SearchMotion(H263Frame reference, int address) {
    var originX = address % this._macroblockWidth * 16;
    var originY = address / this._macroblockWidth * 16;

    var best = (X: 0, Y: 0);
    var bestCost = this._MatchCost(reference, originX, originY, 0, 0, int.MaxValue);

    for (var candidateY = -_SEARCH_RANGE; candidateY <= _SEARCH_RANGE; ++candidateY)
    for (var candidateX = -_SEARCH_RANGE; candidateX <= _SEARCH_RANGE; ++candidateX) {
      if (2 * candidateX < -_VECTOR_LIMIT || 2 * candidateX >= _VECTOR_LIMIT
          || 2 * candidateY < -_VECTOR_LIMIT || 2 * candidateY >= _VECTOR_LIMIT)
        continue;

      // A vector that reads outside the reference is one the decoder refuses, and the cost function
      // would score it happily because it repeats the edge sample instead of failing.
      if (originX + candidateX < 0 || originY + candidateY < 0
          || originX + candidateX + 16 > reference.LumaWidth
          || originY + candidateY + 16 > reference.LumaHeight)
        continue;

      var cost = this._MatchCost(reference, originX, originY, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (2 * candidateX, 2 * candidateY);
    }

    return best;
  }

  /// <summary>
  /// Absolute difference between a macroblock and the prediction one whole-pixel vector offers,
  /// abandoned as soon as it cannot beat <paramref name="ceiling"/>.
  /// </summary>
  private int _MatchCost(
    H263Frame reference, int originX, int originY, int vectorX, int vectorY, int ceiling) {
    var (source, sourceWidth, _) = this._source.PlaneOf(0);
    var (target, targetWidth, _) = reference.PlaneOf(0);
    var targetHeight = reference.LumaHeight;

    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y)
    for (var x = 0; x < 16; ++x) {
      var referenceX = Math.Clamp(originX + x + vectorX, 0, targetWidth - 1);
      var referenceY = Math.Clamp(originY + y + vectorY, 0, targetHeight - 1);
      cost += Math.Abs(
        source[(originY + y) * sourceWidth + originX + x] - target[referenceY * targetWidth + referenceX]);
    }

    return cost;
  }

  /// <summary>Quantises one block of source-minus-prediction into <paramref name="levels"/>.</summary>
  /// <remarks>
  /// The prediction is formed by the routine the decoder predicts with, so the residual is measured
  /// against what the decoder will actually hold. That matters in chrominance even though the search
  /// is whole-pixel: 6.1.1 halves the vector for the chrominance planes, so an odd luminance
  /// displacement lands chrominance between two samples and the decoder interpolates there.
  /// </remarks>
  private void _ReadResidual(
    H263Frame reference, int address, int index, int vectorX, int vectorY,
    scoped Span<int> levels, out bool coded) {
    var (plane, width, _) = this._source.PlaneOf(index);
    var (referencePlane, referenceWidth, _) = reference.PlaneOf(index);
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var left = index < 4 ? column * 16 + (index & 1) * 8 : column * 8;
    var top = index < 4 ? row * 16 + (index >> 1) * 8 : row * 8;

    var blockVectorX = index < 4 ? vectorX : H263MotionCompensation.ToChroma(vectorX);
    var blockVectorY = index < 4 ? vectorY : H263MotionCompensation.ToChroma(vectorY);

    Span<int> prediction = stackalloc int[64];
    if (!H263MotionCompensation.TryPredict(
          prediction, referencePlane, referenceWidth, left, top, blockVectorX, blockVectorY, clampToEdge: false))
      throw new InvalidDataException(
        $"The motion search chose a vector of ({vectorX}, {vectorY}) half-pixels for macroblock {address}, "
        + "which reads outside the reference picture.");

    Span<int> residual = stackalloc int[64];
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      residual[y * 8 + x] = plane[(top + y) * width + left + x] - prediction[y * 8 + x];

    coded = H263BlockEncoder.QuantiseInter(residual, this._quantiser, levels);
  }

  /// <summary>
  /// The median of the three candidate predictors of Figure 12, with the substitutions clause 6.1.1
  /// makes at the edges.
  /// </summary>
  /// <remarks>
  /// This is the decoder's rule read in the same order, and the order is the whole of it: the left
  /// candidate is zeroed first at the left edge, and only then do the two above it take its value at
  /// the top edge, so the first macroblock of a picture predicts from zero rather than from whatever
  /// the arrays happen to hold. This writer emits no group-of-block headers, so no boundary inside
  /// the picture interrupts the prediction.
  /// </remarks>
  private int _PredictVector(short[] vectors, int address) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var atLeftEdge = column == 0;
    var atRightEdge = column == this._macroblockWidth - 1;
    var atTop = row == 0;

    var left = atLeftEdge ? 0 : vectors[address - 1];
    var above = atTop ? left : vectors[address - this._macroblockWidth];
    var aboveRight = atTop
      ? left
      : atRightEdge ? 0 : vectors[address - this._macroblockWidth + 1];

    if (atRightEdge)
      aboveRight = 0;

    return _Median(left, above, aboveRight);
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);

    if (b > c)
      b = c;

    return a > b ? a : b;
  }

  private void _WritePictureHeader() {
    this._writer.Write(_PICTURE_START_CODE, _PICTURE_START_CODE_LENGTH);
    this._writer.Write(this._temporalReference, 8);

    // PTYPE (5.1.3): the two fixed discriminator bits, three display-only flags clear, one of Table 5's
    // five source formats, an I-picture, and every optional baseline mode clear.
    this._writer.WriteBit(1);
    this._writer.WriteBit(0);
    this._writer.Write(0, 3);
    this._writer.Write(this._sourceFormat, 3);
    this._writer.WriteBit(this._reference == null ? 0 : 1); // PICTURE CODING TYPE
    this._writer.Write(0, 4);             // UMV, SAC, advanced prediction, PB-frames

    this._writer.Write(this._quantiser, 5);
    this._writer.WriteBit(0);             // CPM
    this._writer.WriteBit(0);             // PEI: no extra picture information
  }

  private void _ReadSource(scoped Span<int> samples, int address, int index) {
    var (plane, width, _) = this._source.PlaneOf(index);
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var left = index < 4 ? column * 16 + (index & 1) * 8 : column * 8;
    var top = index < 4 ? row * 16 + (index >> 1) * 8 : row * 8;

    for (var y = 0; y < 8; ++y) {
      var source = (top + y) * width + left;
      var target = y * 8;
      for (var x = 0; x < 8; ++x)
        samples[target + x] = plane[source + x];
    }
  }
}
