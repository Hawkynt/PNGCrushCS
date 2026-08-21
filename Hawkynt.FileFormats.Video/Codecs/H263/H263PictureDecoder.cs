using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Decodes one coded picture: its groups of blocks, its macroblocks and their blocks (ITU-T H.263,
/// clauses 5.2 through 5.4 and 6.1 through 6.2).
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture, because everything it holds is reset by the
/// next one: the quantiser, the motion vectors every later macroblock's predictor is the median of,
/// and the reference. A decoder that kept any of them across pictures would still produce a picture,
/// and it would be wrong from its second frame onward.
/// <para/>
/// The group of blocks layer is not a slice layer and does not behave like one. A group's header is
/// optional for every group but the first, whose header is the picture's, and whether it was present
/// changes the prediction: the substitution rule of clause 6.1.1 treats the macroblocks above a group
/// as unavailable only when the group carried a header. So whether each group had one is remembered
/// rather than assumed, and prediction crosses a group boundary in a stream that omits the headers.
/// </remarks>
internal sealed class H263PictureDecoder {

  /// <summary>INTER: predicted, one vector for the whole macroblock (ITU-T H.263, Table 9).</summary>
  private const int _INTER = 0;

  /// <summary>INTER+Q: predicted, and carrying a change to the quantiser.</summary>
  private const int _INTER_WITH_QUANTISER = 1;

  /// <summary>INTER4V: predicted with four vectors, which is the Advanced Prediction mode of Annex F.</summary>
  private const int _INTER_FOUR_VECTORS = 2;

  /// <summary>INTRA: coded on its own.</summary>
  private const int _INTRA = 3;

  /// <summary>INTRA+Q: coded on its own, and carrying a change to the quantiser.</summary>
  private const int _INTRA_WITH_QUANTISER = 4;

  /// <summary>INTER4V+Q: four vectors and a change to the quantiser.</summary>
  private const int _INTER_FOUR_VECTORS_WITH_QUANTISER = 5;

  /// <summary>The group number a picture start code carries (ITU-T H.263, 5.2.3).</summary>
  private const int _PICTURE_GROUP_NUMBER = 0;

  /// <summary>The group number that marks the end of a bitstream rather than a group.</summary>
  private const int _END_OF_SEQUENCE = 31;

  /// <summary>The group number that marks the end of a sub-bitstream (ITU-T H.263, 5.2.3).</summary>
  private const int _END_OF_SUB_BITSTREAM = 30;

  private readonly H263PictureHeader _header;
  private readonly H263Frame _target;
  private readonly H263Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;

  /// <summary>Each macroblock's vector, in half-pixel units, or zero where clause 6.1.1 says zero.</summary>
  private readonly short[] _vectorX;

  private readonly short[] _vectorY;

  /// <summary>Whether the group a macroblock row belongs to opened with a header of its own.</summary>
  private readonly bool[] _groupHasHeader;

  private int _quantiser;

  /// <summary>
  /// The first macroblock of the run being decoded, before which nothing may be predicted from.
  /// </summary>
  /// <remarks>
  /// Zero for an H.263 picture, whose macroblocks are one run from the first to the last, so the
  /// substitution rules of clause 6.1.1 behave exactly as they did before this existed. A RealVideo
  /// picture arrives as several independently coded runs — it sends each in its own packet so that
  /// losing one costs part of a picture rather than all of it — and a run that predicted from the run
  /// before it would not be independent at all.
  /// </remarks>
  private int _runStart;

  private H263PictureDecoder(H263PictureHeader header, H263Frame target, H263Frame? reference) {
    this._header = header;
    this._target = target;
    this._reference = reference;
    this._macroblockWidth = header.MacroblockWidth;
    this._macroblockHeight = header.MacroblockHeight;
    this._vectorX = new short[this._macroblockWidth * this._macroblockHeight];
    this._vectorY = new short[this._macroblockWidth * this._macroblockHeight];
    this._groupHasHeader = new bool[this._macroblockHeight];
    this._quantiser = header.Quantiser;
  }

  /// <summary>The picture being reconstructed.</summary>
  internal H263Frame Target => this._target;

  /// <summary>
  /// Prepares to decode a picture whose header has been read.
  /// </summary>
  /// <exception cref="InvalidDataException">
  /// The picture is predicted and the stream has supplied nothing to predict from.
  /// </exception>
  internal static H263PictureDecoder BeginPicture(H263PictureHeader header, H263Frame target, H263Frame? reference) {
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(target);

    if (!header.IsIntra && reference == null)
      throw new InvalidDataException(
        "An H.263 predicted picture arrived before any intra picture, so there is nothing for it to be predicted from. "
        + "Decoding must begin at an intra picture.");

    return new(header, target, header.IsIntra ? null : reference);
  }

  // ============================================================================================
  // Group of blocks layer — ITU-T H.263, 5.2
  // ============================================================================================

  /// <summary>
  /// Decodes every macroblock of the picture, taking the group headers that appear between them.
  /// </summary>
  internal void DecodePicture(ref H263BitReader reader) {
    var count = this._macroblockWidth * this._macroblockHeight;
    var groupRows = this._header.MacroblockRowsPerGroup;

    for (var address = 0; address < count; ++address) {
      var row = address / this._macroblockWidth;
      var isGroupStart = address % this._macroblockWidth == 0 && row % groupRows == 0;

      if (this._header.HasGroupLayer && isGroupStart && row != 0 && reader.AtStartCode())
        this._ReadGroupHeader(ref reader, row / groupRows, row);

      this._DecodeMacroblock(ref reader, address);
    }
  }

  /// <summary>
  /// Decodes one independently coded run of macroblocks, at a quantiser of its own.
  /// </summary>
  /// <remarks>
  /// For RealVideo, whose pictures are cut into runs that each restate the picture's type and
  /// quantiser and say which macroblock they begin at and how many they carry. There is no group of
  /// blocks layer to look for between them: a run ends when its count is exhausted, and the next one
  /// begins at the next byte of the picture.
  /// <para/>
  /// The reconstructed samples land in the same target as every other run of the picture, which is
  /// what makes the runs pieces of one picture rather than pictures of their own. What does not carry
  /// across is prediction: <see cref="_runStart"/> makes every macroblock before this run unavailable
  /// to the vector predictor, so a run decodes to the same samples whether or not the runs before it
  /// arrived.
  /// </remarks>
  internal void DecodeRun(ref H263BitReader reader, int firstAddress, int count, int quantiser) {
    var total = this._macroblockWidth * this._macroblockHeight;
    if (firstAddress < 0 || count < 0 || firstAddress > total - count)
      throw new InvalidDataException(
        $"A run of {count} macroblock(s) beginning at {firstAddress} does not fit in a picture of {total}.");

    this._quantiser = quantiser;
    this._runStart = firstAddress;

    var end = firstAddress + count;
    for (var address = firstAddress; address < end; ++address)
      this._DecodeMacroblock(ref reader, address);
  }

  private void _ReadGroupHeader(ref H263BitReader reader, int expectedGroupNumber, int row) {
    reader.ConsumeStartCode();
    var groupNumber = reader.ReadBits(5);

    switch (groupNumber) {
      case _PICTURE_GROUP_NUMBER:
        throw new InvalidDataException(
          $"A picture start code was reached at macroblock row {row} of an H.263 picture that still has "
          + $"{this._macroblockHeight - row} row(s) to decode. The picture's groups of blocks do not cover it.");

      case _END_OF_SEQUENCE:
      case _END_OF_SUB_BITSTREAM:
        throw new InvalidDataException(
          $"An end-of-sequence code (group number {groupNumber}) was reached at macroblock row {row} of an H.263 "
          + $"picture that still has {this._macroblockHeight - row} row(s) to decode.");

      default:
        if (groupNumber != expectedGroupNumber)
          throw new InvalidDataException(
            $"An H.263 group of blocks states group number {groupNumber} where {expectedGroupNumber} was due. "
            + "ITU-T H.263 4.2.1 requires the groups of a picture in increasing order with none left out.");

        break;
    }

    // GFID: the same value in every group of one picture, so that a decoder joining a stream part way
    // through can tell whether the picture header it missed was the one these groups belong to. This
    // decoder is handed whole pictures and has the header, so there is nothing here for it to learn.
    reader.ReadBits(2);

    this._quantiser = _ReadQuantiser(ref reader);
    this._groupHasHeader[row] = true;
  }

  private static int _ReadQuantiser(ref H263BitReader reader) {
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "An H.263 group of blocks states GQUANT 0. ITU-T H.263 5.2.6 gives QUANT the range 1 to 31; zero is not a "
        + "step size and would reconstruct every coefficient as zero.");

    return quantiser;
  }

  // ============================================================================================
  // Macroblock layer — ITU-T H.263, 5.3
  // ============================================================================================

  private void _DecodeMacroblock(ref H263BitReader reader, int address) {
    int macroblockType;
    int chromaPattern;

    for (; ; ) {
      // COD is present in a predicted picture only. A set bit means the macroblock carries nothing at
      // all: it is the co-located macroblock of the reference, and its vector is zero for the sake of
      // every later macroblock's predictor (ITU-T H.263, 6.1.1).
      if (!this._header.IsIntra && reader.ReadBit() == 1) {
        this._CopyFromReference(address);
        return;
      }

      var mcbpc = (this._header.IsIntra
        ? H263VlcTables.IntraMacroblockType
        : H263VlcTables.PredictedMacroblockType).Read(ref reader);

      // The stuffing code carries no macroblock. Reading it puts the decoder back at the start of a
      // macroblock, which in a predicted picture means back at a COD bit and not at another MCBPC.
      if (mcbpc == H263VlcTables.McbpcStuffing)
        continue;

      macroblockType = H263VlcTables.TypeOf(mcbpc);
      chromaPattern = H263VlcTables.ChromaPatternOf(mcbpc);
      break;
    }

    if (macroblockType is _INTER_FOUR_VECTORS or _INTER_FOUR_VECTORS_WITH_QUANTISER)
      throw new NotSupportedException(
        $"Macroblock {address} of this H.263 picture states type {macroblockType} (INTER4V"
        + (macroblockType == _INTER_FOUR_VECTORS_WITH_QUANTISER ? "+Q" : string.Empty)
        + "), which carries one motion vector for each of the four luminance blocks. Four vectors per macroblock is "
        + "the Advanced Prediction mode of ITU-T H.263 Annex F, which is not implemented.");

    var isIntra = macroblockType is _INTRA or _INTRA_WITH_QUANTISER;

    // CBPY names the luminance blocks that carry coefficients. An inter macroblock means the
    // complement of what the table's value states, and reading it uncomplemented leaves exactly the
    // blocks that were coded as pure prediction — which is a picture, and is wrong.
    var luminancePattern = H263VlcTables.LuminancePattern.Read(ref reader);
    if (!isIntra)
      luminancePattern ^= 0xF;

    if (macroblockType is _INTER_WITH_QUANTISER or _INTRA_WITH_QUANTISER)
      this._ApplyQuantiserDifference(ref reader);

    var vectorX = 0;
    var vectorY = 0;
    if (!isIntra) {
      vectorX = this._ReadVector(ref reader, address, horizontal: true);
      vectorY = this._ReadVector(ref reader, address, horizontal: false);
    }

    // An intra macroblock predicts from nothing, so clause 6.1.1 has the macroblocks after it treat
    // its vector as zero rather than carrying the last one across it.
    this._vectorX[address] = (short)(isIntra ? 0 : vectorX);
    this._vectorY[address] = (short)(isIntra ? 0 : vectorY);

    var pattern = (luminancePattern << 2) | chromaPattern;
    if (isIntra)
      this._ReconstructIntra(ref reader, address, pattern);
    else
      this._ReconstructInter(ref reader, address, pattern, vectorX, vectorY);
  }

  /// <summary>
  /// Applies DQUANT: a two-bit change to the quantiser (ITU-T H.263, 5.3.6 and Table 13).
  /// </summary>
  /// <remarks>
  /// Clipped rather than refused, because the Recommendation says so in as many words: a value that
  /// would leave the range one to thirty-one is clipped to the end it left. That makes a stream whose
  /// encoder relied on the clipping decodable, and it is the only place in this decoder where an
  /// out-of-range field is not an error.
  /// </remarks>
  private void _ApplyQuantiserDifference(ref H263BitReader reader) {
    var difference = reader.ReadBits(2) switch { 0 => -1, 1 => -2, 2 => 1, _ => 2 };
    var quantiser = this._quantiser + difference;
    this._quantiser = quantiser < 1 ? 1 : quantiser > 31 ? 31 : quantiser;
  }

  // ============================================================================================
  // Motion vectors — ITU-T H.263, 6.1.1
  // ============================================================================================

  /// <summary>
  /// Reconstructs one component of a macroblock's motion vector from its predictor and the coded
  /// difference.
  /// </summary>
  /// <remarks>
  /// Each code in Table 14 stands for two differences thirty-two whole pixels apart, and the one that
  /// was meant is whichever puts the vector inside the permitted range of -16 to 15.5. So the
  /// wraparound below is not a clamp and does not lose anything: it is how the second of the pair is
  /// reached. Clamping instead would produce a vector nobody coded.
  /// </remarks>
  private int _ReadVector(ref H263BitReader reader, int address, bool horizontal) {
    var predictor = this._PredictVector(address, horizontal);
    var vector = predictor + H263VlcTables.MotionVectorDifference.Read(ref reader);

    if (vector < -32)
      vector += 64;
    else if (vector > 31)
      vector -= 64;

    return vector;
  }

  /// <summary>
  /// The median of the three candidate predictors of ITU-T H.263 Figure 12, with the substitutions
  /// clause 6.1.1 makes at the edges.
  /// </summary>
  /// <remarks>
  /// The four substitution rules are applied in the order the Recommendation gives them, and the
  /// order matters: the left candidate is zeroed first at the left edge, and only then do the two
  /// above it take its value at the top edge — so the top-left macroblock predicts from zero rather
  /// than from whatever the arrays happen to hold. Reversing the two leaves the first macroblock of
  /// every picture reading vectors that were never coded.
  /// </remarks>
  private int _PredictVector(int address, bool horizontal) {
    var vectors = horizontal ? this._vectorX : this._vectorY;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    var atLeftEdge = column == 0;
    var atRightEdge = column == this._macroblockWidth - 1;

    // The macroblocks above are unavailable at the top of the picture, and at the top of a group of
    // blocks only when that group opened with a header of its own — an encoder that leaves the
    // headers out is one whose prediction crosses the boundary.
    var atTop = row == 0
                || (row % this._header.MacroblockRowsPerGroup == 0 && this._groupHasHeader[row]);

    // A macroblock coded in an earlier run of this picture is not this run's to predict from. For an
    // H.263 picture the run begins at nought and these tests can never fire, which is why the
    // behaviour measured against ffmpeg for that codec is untouched.
    atTop = atTop || address - this._macroblockWidth < this._runStart;

    var left = atLeftEdge || address - 1 < this._runStart ? 0 : vectors[address - 1];
    var above = atTop ? left : vectors[address - this._macroblockWidth];
    var aboveRight = atTop
      ? left
      : atRightEdge ? 0 : vectors[address - this._macroblockWidth + 1];

    // Rule 4 comes after rule 3, so a macroblock in the top row at the right edge takes the left
    // candidate for the one above it and zero for the one above and to the right.
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

  // ============================================================================================
  // Reconstruction — ITU-T H.263, 6.2
  // ============================================================================================

  private void _ReconstructIntra(ref H263BitReader reader, int address, int pattern) {
    Span<int> block = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      H263BlockDecoder.ReadIntra(ref reader, block, this._quantiser, _IsCoded(pattern, index), this._header.HasWideEscapeLevel);
      this._Store(address, index, block);
    }
  }

  private void _ReconstructInter(ref H263BitReader reader, int address, int pattern, int vectorX, int vectorY) {
    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, address, index, vectorX, vectorY);

      if (_IsCoded(pattern, index)) {
        H263BlockDecoder.ReadInter(ref reader, block, this._quantiser, this._header.HasWideEscapeLevel);
        for (var i = 0; i < 64; ++i)
          block[i] += prediction[i];
      } else {
        prediction.CopyTo(block);
      }

      this._Store(address, index, block);
    }
  }

  /// <summary>
  /// Copies a macroblock nothing was coded for out of the reference picture.
  /// </summary>
  /// <remarks>
  /// A macroblock whose COD bit is set is the co-located one of the reference with a zero vector —
  /// there is no residual and nothing to interpolate, so this is a copy and not a prediction.
  /// </remarks>
  private void _CopyFromReference(int address) {
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;

    Span<int> prediction = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, address, index, 0, 0);
      this._Store(address, index, prediction);
    }
  }

  private void _Predict(Span<int> prediction, int address, int index, int vectorX, int vectorY) {
    var reference = this._reference
      ?? throw new InvalidDataException(
        $"Macroblock {address} of this H.263 picture is predicted, but the picture holds no reference to predict "
        + "from. Decoding must begin at an intra picture.");

    var isChroma = index >= 4;
    if (isChroma) {
      vectorX = H263MotionCompensation.ToChroma(vectorX);
      vectorY = H263MotionCompensation.ToChroma(vectorY);
    }

    var (referencePlane, planeWidth) = isChroma
      ? (index == 4 ? reference.Cb : reference.Cr, reference.ChromaWidth)
      : (reference.Luma, reference.LumaWidth);

    var (left, top) = this._BlockOrigin(address, index);
    if (H263MotionCompensation.TryPredict(
          prediction, referencePlane, planeWidth, left, top, vectorX, vectorY,
          this._header.AllowsVectorsOutsidePicture))
      return;

    throw new InvalidDataException(
      $"Block {index} of macroblock {address} (column {address % this._macroblockWidth}, row "
      + $"{address / this._macroblockWidth}) of this H.263 picture has a motion vector of ({vectorX}, {vectorY}) "
      + $"half-pixels from ({left}, {top}), which reads outside the {planeWidth}x"
      + $"{referencePlane.Length / planeWidth} reference plane. ITU-T H.263 6.1.1 permits a vector outside the "
      + "picture only in the Unrestricted Motion Vector mode of Annex D, which this picture does not use.");
  }

  private void _Store(int address, int index, ReadOnlySpan<int> samples) {
    var (plane, width, _) = this._target.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  /// <summary>
  /// Whether one of a macroblock's six blocks carries coefficients.
  /// </summary>
  /// <remarks>
  /// The pattern is CBPY's four bits above CBPC's two, and in both of them the leftmost bit is the
  /// lowest-numbered block of ITU-T H.263 Figure 5 — the top-left luminance quadrant for CBPY, and Cb
  /// for CBPC.
  /// </remarks>
  private static bool _IsCoded(int pattern, int index) => (pattern & (1 << (5 - index))) != 0;

  /// <summary>
  /// Where one of a macroblock's six blocks sits in its plane (ITU-T H.263, Figure 5).
  /// </summary>
  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
