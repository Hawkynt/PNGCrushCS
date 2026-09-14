using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>Decodes one ordinary H.263 I- or P-picture from the GOB layer down.</summary>
internal sealed class H263PictureDecoder {

  private const int _INTER = 0;
  private const int _INTER_WITH_QUANTISER = 1;
  private const int _INTER_FOUR_VECTORS = 2;
  private const int _INTRA = 3;
  private const int _INTRA_WITH_QUANTISER = 4;
  private const int _INTER_FOUR_VECTORS_WITH_QUANTISER = 5;

  private const int _PICTURE_GROUP_NUMBER = 0;
  private const int _END_OF_SEQUENCE = 31;
  private const int _END_OF_SUB_BITSTREAM = 30;

  private readonly H263PictureHeader _header;
  private readonly H263Frame _target;
  private readonly H263Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly short[] _vectorX;
  private readonly short[] _vectorY;
  private readonly bool[] _groupHasHeader;

  private int _quantiser;
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
    target.TemporalReference = header.TemporalReference;
  }

  internal H263Frame Target => this._target;

  internal static H263PictureDecoder BeginPicture(H263PictureHeader header, H263Frame target, H263Frame? reference) {
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(target);

    if (header.IsBidirectional)
      throw new ArgumentException("An Annex O B-picture must be decoded by the bidirectional picture decoder.", nameof(header));

    if (!header.IsIntra && reference == null)
      throw new InvalidDataException(
        "An H.263 predicted picture arrived before any intra picture, so there is nothing for it to be predicted from. Decoding must begin at an intra picture.");

    return new(header, target, header.IsIntra ? null : reference);
  }

  // ============================================================================================
  // Group of blocks layer — 5.2
  // ============================================================================================

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

  /// <summary>Decodes an independently coded macroblock run used by RealVideo's H.263-derived layer.</summary>
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
          $"A picture start code was reached at macroblock row {row} while this H.263 picture still has rows to decode.");
      case _END_OF_SEQUENCE:
      case _END_OF_SUB_BITSTREAM:
        throw new InvalidDataException(
          $"An end code (group number {groupNumber}) was reached at macroblock row {row} before this picture ended.");
      default:
        if (groupNumber != expectedGroupNumber)
          throw new InvalidDataException(
            $"An H.263 GOB states group number {groupNumber} where {expectedGroupNumber} was due.");
        break;
    }

    reader.ReadBits(2); // GFID
    this._quantiser = _ReadQuantiser(ref reader);
    this._groupHasHeader[row] = true;
  }

  private static int _ReadQuantiser(ref H263BitReader reader) {
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException("An H.263 GOB states GQUANT 0; QUANT must be 1 through 31.");
    return quantiser;
  }

  // ============================================================================================
  // Macroblock layer — 5.3
  // ============================================================================================

  private void _DecodeMacroblock(ref H263BitReader reader, int address) {
    int macroblockType;
    int chromaPattern;

    for (;;) {
      if (!this._header.IsIntra && reader.ReadBit() == 1) {
        this._CopyFromReference(address);
        return;
      }

      var mcbpc = (this._header.IsIntra
        ? H263VlcTables.IntraMacroblockType
        : H263VlcTables.PredictedMacroblockType).Read(ref reader);

      if (mcbpc == H263VlcTables.McbpcStuffing)
        continue;

      macroblockType = H263VlcTables.TypeOf(mcbpc);
      chromaPattern = H263VlcTables.ChromaPatternOf(mcbpc);
      break;
    }

    if (macroblockType is _INTER_FOUR_VECTORS or _INTER_FOUR_VECTORS_WITH_QUANTISER)
      throw new NotSupportedException(
        $"Macroblock {address} states INTER4V, the Advanced Prediction mode of H.263 Annex F, which is not implemented.");

    var isIntra = macroblockType is _INTRA or _INTRA_WITH_QUANTISER;
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

    this._vectorX[address] = (short)(isIntra ? 0 : vectorX);
    this._vectorY[address] = (short)(isIntra ? 0 : vectorY);
    this._target.MotionX[address] = this._vectorX[address];
    this._target.MotionY[address] = this._vectorY[address];
    this._target.HasMotion[address] = !isIntra;

    var pattern = (luminancePattern << 2) | chromaPattern;
    if (isIntra)
      this._ReconstructIntra(ref reader, address, pattern);
    else
      this._ReconstructInter(ref reader, address, pattern, vectorX, vectorY);
  }

  private void _ApplyQuantiserDifference(ref H263BitReader reader) {
    var difference = reader.ReadBits(2) switch { 0 => -1, 1 => -2, 2 => 1, _ => 2 };
    this._quantiser = Math.Clamp(this._quantiser + difference, 1, 31);
  }

  // ============================================================================================
  // Motion vectors — 6.1.1
  // ============================================================================================

  private int _ReadVector(ref H263BitReader reader, int address, bool horizontal) {
    var predictor = this._PredictVector(address, horizontal);
    var vector = predictor + H263VlcTables.MotionVectorDifference.Read(ref reader);
    if (vector < -32)
      vector += 64;
    else if (vector > 31)
      vector -= 64;
    return vector;
  }

  private int _PredictVector(int address, bool horizontal) {
    var vectors = horizontal ? this._vectorX : this._vectorY;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var atLeftEdge = column == 0;
    var atRightEdge = column == this._macroblockWidth - 1;
    var atTop = row == 0
                || (row % this._header.MacroblockRowsPerGroup == 0 && this._groupHasHeader[row])
                || address - this._macroblockWidth < this._runStart;

    var left = atLeftEdge || address - 1 < this._runStart ? 0 : vectors[address - 1];
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

  // ============================================================================================
  // Reconstruction — 6.2
  // ============================================================================================

  private void _ReconstructIntra(ref H263BitReader reader, int address, int pattern) {
    Span<int> block = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      H263BlockDecoder.ReadIntra(
        ref reader, block, this._quantiser, _IsCoded(pattern, index), this._header.HasWideEscapeLevel);
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

  private void _CopyFromReference(int address) {
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;
    this._target.MotionX[address] = 0;
    this._target.MotionY[address] = 0;
    this._target.HasMotion[address] = true;

    Span<int> prediction = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, address, index, 0, 0);
      this._Store(address, index, prediction);
    }
  }

  private void _Predict(Span<int> prediction, int address, int index, int vectorX, int vectorY) {
    var reference = this._reference
      ?? throw new InvalidDataException($"Macroblock {address} is predicted but no reference picture is available.");

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
          prediction,
          referencePlane,
          planeWidth,
          left,
          top,
          vectorX,
          vectorY,
          this._header.AllowsVectorsOutsidePicture,
          this._header.RoundingType))
      return;

    throw new InvalidDataException(
      $"Block {index} of macroblock {address} has vector ({vectorX}, {vectorY}) half-pixels, which reads outside its reference plane without Annex D.");
  }

  private void _Store(int address, int index, ReadOnlySpan<int> samples) {
    var (plane, width, _) = this._target.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x)
        plane[row + x] = (byte)Math.Clamp(samples[y * 8 + x], 0, 255);
    }
  }

  private static bool _IsCoded(int pattern, int index) => (pattern & (1 << (5 - index))) != 0;

  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }
}
