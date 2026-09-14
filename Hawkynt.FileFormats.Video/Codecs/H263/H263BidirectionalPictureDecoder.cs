using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>Decodes the Annex O macroblock layer of a temporal B-picture.</summary>
internal sealed class H263BidirectionalPictureDecoder {

  private const int _DIRECT = 0;
  private const int _DIRECT_Q = 1;
  private const int _FORWARD_NO_TEXTURE = 2;
  private const int _FORWARD = 3;
  private const int _FORWARD_Q = 4;
  private const int _BACKWARD_NO_TEXTURE = 5;
  private const int _BACKWARD = 6;
  private const int _BACKWARD_Q = 7;
  private const int _BIDIRECTIONAL_NO_TEXTURE = 8;
  private const int _BIDIRECTIONAL = 9;
  private const int _BIDIRECTIONAL_Q = 10;
  private const int _INTRA = 11;
  private const int _INTRA_Q = 12;
  private const int _STUFFING = 13;

  private readonly H263PictureHeader _header;
  private readonly H263Frame _target;
  private readonly H263Frame _past;
  private readonly H263Frame _future;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly short[] _forwardX;
  private readonly short[] _forwardY;
  private readonly short[] _backwardX;
  private readonly short[] _backwardY;
  private readonly bool[] _hasForward;
  private readonly bool[] _hasBackward;
  private readonly bool[] _groupHasHeader;
  private int _quantiser;

  internal H263BidirectionalPictureDecoder(
    H263PictureHeader header,
    H263Frame target,
    H263Frame past,
    H263Frame future) {
    this._header = header ?? throw new ArgumentNullException(nameof(header));
    this._target = target ?? throw new ArgumentNullException(nameof(target));
    this._past = past ?? throw new ArgumentNullException(nameof(past));
    this._future = future ?? throw new ArgumentNullException(nameof(future));

    if (!header.IsBidirectional)
      throw new ArgumentException("The Annex O B-picture decoder requires a B-picture header.", nameof(header));
    if (past.LumaWidth != target.LumaWidth || past.LumaHeight != target.LumaHeight
        || future.LumaWidth != target.LumaWidth || future.LumaHeight != target.LumaHeight)
      throw new InvalidDataException("An Annex O B-picture and both temporal references must have the same coded geometry.");

    this._macroblockWidth = header.MacroblockWidth;
    this._macroblockHeight = header.MacroblockHeight;
    var macroblocks = this._macroblockWidth * this._macroblockHeight;
    this._forwardX = new short[macroblocks];
    this._forwardY = new short[macroblocks];
    this._backwardX = new short[macroblocks];
    this._backwardY = new short[macroblocks];
    this._hasForward = new bool[macroblocks];
    this._hasBackward = new bool[macroblocks];
    this._groupHasHeader = new bool[this._macroblockHeight];
    this._quantiser = header.Quantiser;
    target.TemporalReference = header.TemporalReference;
  }

  internal H263Frame Target => this._target;

  internal void DecodePicture(ref H263BitReader reader) {
    var count = this._macroblockWidth * this._macroblockHeight;
    var groupRows = this._header.MacroblockRowsPerGroup;

    for (var address = 0; address < count; ++address) {
      var row = address / this._macroblockWidth;
      var isGroupStart = address % this._macroblockWidth == 0 && row % groupRows == 0;
      if (isGroupStart && row != 0 && reader.AtStartCode())
        this._ReadGroupHeader(ref reader, row / groupRows, row);

      this._DecodeMacroblock(ref reader, address);
    }
  }

  private void _ReadGroupHeader(ref H263BitReader reader, int expectedGroupNumber, int row) {
    reader.ConsumeStartCode();
    var groupNumber = reader.ReadBits(5);
    if (groupNumber != expectedGroupNumber)
      throw new InvalidDataException(
        $"An Annex O B-picture GOB states group number {groupNumber} where {expectedGroupNumber} was due.");

    reader.ReadBits(2); // GFID
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException("An Annex O B-picture GOB states GQUANT 0; QUANT must be 1 through 31.");

    this._quantiser = quantiser;
    this._groupHasHeader[row] = true;
  }

  private void _DecodeMacroblock(ref H263BitReader reader, int address) {
    for (;;) {
      if (reader.ReadBit() == 1) {
        this._DecodeDirect(ref reader, address, pattern: 0);
        return;
      }

      var type = H263AnnexOVlc.BidirectionalMacroblockType.Read(ref reader);
      if (type == _STUFFING)
        continue;

      var isIntra = type is _INTRA or _INTRA_Q;
      var hasTexture = type is _DIRECT or _DIRECT_Q
        or _FORWARD or _FORWARD_Q
        or _BACKWARD or _BACKWARD_Q
        or _BIDIRECTIONAL or _BIDIRECTIONAL_Q
        or _INTRA or _INTRA_Q;
      var hasForward = type is _FORWARD_NO_TEXTURE or _FORWARD or _FORWARD_Q
        or _BIDIRECTIONAL_NO_TEXTURE or _BIDIRECTIONAL or _BIDIRECTIONAL_Q;
      var hasBackward = type is _BACKWARD_NO_TEXTURE or _BACKWARD or _BACKWARD_Q
        or _BIDIRECTIONAL_NO_TEXTURE or _BIDIRECTIONAL or _BIDIRECTIONAL_Q;
      var hasDquant = type is _DIRECT_Q or _FORWARD_Q or _BACKWARD_Q or _BIDIRECTIONAL_Q or _INTRA_Q;

      var chromaPattern = hasTexture ? H263AnnexOVlc.ChromaPattern.Read(ref reader) : 0;
      var luminancePattern = hasTexture ? H263VlcTables.LuminancePattern.Read(ref reader) : 0;
      if (hasTexture && !isIntra)
        luminancePattern ^= 0xF;

      if (hasDquant)
        this._ApplyQuantiserDifference(ref reader);

      var forwardX = 0;
      var forwardY = 0;
      if (hasForward) {
        forwardX = this._ReadVector(ref reader, address, forward: true, horizontal: true);
        forwardY = this._ReadVector(ref reader, address, forward: true, horizontal: false);
      }

      var backwardX = 0;
      var backwardY = 0;
      if (hasBackward) {
        backwardX = this._ReadVector(ref reader, address, forward: false, horizontal: true);
        backwardY = this._ReadVector(ref reader, address, forward: false, horizontal: false);
      }

      this._hasForward[address] = hasForward;
      this._hasBackward[address] = hasBackward;
      this._forwardX[address] = (short)forwardX;
      this._forwardY[address] = (short)forwardY;
      this._backwardX[address] = (short)backwardX;
      this._backwardY[address] = (short)backwardY;

      var pattern = (luminancePattern << 2) | chromaPattern;
      switch (type) {
        case _DIRECT:
        case _DIRECT_Q:
          this._DecodeDirect(ref reader, address, pattern);
          return;
        case _FORWARD_NO_TEXTURE:
        case _FORWARD:
        case _FORWARD_Q:
          this._ReconstructInter(ref reader, address, pattern, forwardX, forwardY, null);
          return;
        case _BACKWARD_NO_TEXTURE:
        case _BACKWARD:
        case _BACKWARD_Q:
          this._ReconstructInter(ref reader, address, pattern, null, null, (backwardX, backwardY));
          return;
        case _BIDIRECTIONAL_NO_TEXTURE:
        case _BIDIRECTIONAL:
        case _BIDIRECTIONAL_Q:
          this._ReconstructInter(ref reader, address, pattern, forwardX, forwardY, (backwardX, backwardY));
          return;
        case _INTRA:
        case _INTRA_Q:
          this._ReconstructIntra(ref reader, address, pattern);
          return;
        default:
          throw new InvalidDataException($"Unknown Annex O B-picture macroblock type {type}.");
      }
    }
  }

  private void _DecodeDirect(ref H263BitReader reader, int address, int pattern) {
    // Direct vectors do not enter the same-direction predictor grids (O.5.1/O.5.2).
    this._hasForward[address] = false;
    this._hasBackward[address] = false;

    var futureVectorX = this._future.HasMotion[address] ? this._future.MotionX[address] : 0;
    var futureVectorY = this._future.HasMotion[address] ? this._future.MotionY[address] : 0;

    var trd = _TemporalDistance(this._past.TemporalReference, this._future.TemporalReference);
    var trb = _TemporalDistance(this._past.TemporalReference, this._header.TemporalReference);
    if (trd == 0 || trb == 0 || trb >= trd)
      throw new InvalidDataException(
        $"The Annex O direct-mode temporal references are inconsistent: past={this._past.TemporalReference}, B={this._header.TemporalReference}, future={this._future.TemporalReference}.");

    var forwardX = trb * futureVectorX / trd;
    var forwardY = trb * futureVectorY / trd;
    var backwardX = (trb - trd) * futureVectorX / trd;
    var backwardY = (trb - trd) * futureVectorY / trd;

    this._ReconstructInter(ref reader, address, pattern, forwardX, forwardY, (backwardX, backwardY));
  }

  private static int _TemporalDistance(int from, int to) => (to - from) & 0xFF;

  private void _ApplyQuantiserDifference(ref H263BitReader reader) {
    var difference = reader.ReadBits(2) switch { 0 => -1, 1 => -2, 2 => 1, _ => 2 };
    this._quantiser = Math.Clamp(this._quantiser + difference, 1, 31);
  }

  private int _ReadVector(ref H263BitReader reader, int address, bool forward, bool horizontal) {
    var predictor = this._PredictVector(address, forward, horizontal);
    var vector = predictor + H263VlcTables.MotionVectorDifference.Read(ref reader);
    if (vector < -32)
      vector += 64;
    else if (vector > 31)
      vector -= 64;
    return vector;
  }

  /// <summary>
  /// Annex O keeps independent predictor fields for forward and backward vectors. A neighbour that
  /// does not carry a vector in the requested direction contributes zero; direct vectors are not fed
  /// back into either field.
  /// </summary>
  private int _PredictVector(int address, bool forward, bool horizontal) {
    var vectors = forward
      ? horizontal ? this._forwardX : this._forwardY
      : horizontal ? this._backwardX : this._backwardY;
    var present = forward ? this._hasForward : this._hasBackward;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;
    var atLeftEdge = column == 0;
    var atRightEdge = column == this._macroblockWidth - 1;
    var atTop = row == 0
                || (row % this._header.MacroblockRowsPerGroup == 0 && this._groupHasHeader[row]);

    var leftAddress = address - 1;
    var aboveAddress = address - this._macroblockWidth;
    var aboveRightAddress = aboveAddress + 1;

    var left = atLeftEdge || !present[leftAddress] ? 0 : vectors[leftAddress];
    var above = atTop || !present[aboveAddress] ? 0 : vectors[aboveAddress];
    var aboveRight = atTop || atRightEdge || !present[aboveRightAddress] ? 0 : vectors[aboveRightAddress];
    return _Median(left, above, aboveRight);
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    if (b > c)
      b = c;
    return a > b ? a : b;
  }

  private void _ReconstructIntra(ref H263BitReader reader, int address, int pattern) {
    Span<int> block = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      H263BlockDecoder.ReadIntra(ref reader, block, this._quantiser, _IsCoded(pattern, index), wideEscapeLevel: false);
      this._Store(address, index, block);
    }
  }

  private void _ReconstructInter(
    ref H263BitReader reader,
    int address,
    int pattern,
    int? forwardX,
    int? forwardY,
    (int X, int Y)? backward) {
    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];
    Span<int> second = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      if (forwardX.HasValue) {
        this._Predict(prediction, this._past, address, index, forwardX.Value, forwardY!.Value);
        if (backward.HasValue) {
          this._Predict(second, this._future, address, index, backward.Value.X, backward.Value.Y);
          for (var i = 0; i < 64; ++i)
            prediction[i] = (prediction[i] + second[i]) >> 1;
        }
      } else if (backward.HasValue) {
        this._Predict(prediction, this._future, address, index, backward.Value.X, backward.Value.Y);
      } else {
        throw new InvalidOperationException("An Annex O inter macroblock needs at least one prediction direction.");
      }

      if (_IsCoded(pattern, index)) {
        H263BlockDecoder.ReadInter(ref reader, block, this._quantiser, wideEscapeLevel: false);
        for (var i = 0; i < 64; ++i)
          block[i] += prediction[i];
      } else {
        prediction.CopyTo(block);
      }

      this._Store(address, index, block);
    }
  }

  private void _Predict(
    Span<int> prediction,
    H263Frame reference,
    int address,
    int index,
    int vectorX,
    int vectorY) {
    var isChroma = index >= 4;
    if (isChroma) {
      vectorX = H263MotionCompensation.ToChroma(vectorX);
      vectorY = H263MotionCompensation.ToChroma(vectorY);
    }

    var (plane, width, _) = reference.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);
    // O.5 permits B-picture vectors to extend outside their references and uses Annex D.1 edge samples.
    H263MotionCompensation.TryPredict(
      prediction, plane, width, left, top, vectorX, vectorY, clampToEdge: true, roundingControl: 0);
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
