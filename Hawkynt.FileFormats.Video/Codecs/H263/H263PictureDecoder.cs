using System;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Decodes one H.263-family coded picture: groups, macroblocks, motion prediction and blocks.
/// </summary>
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

  private enum MacroblockKind : byte {
    Unknown,
    Skipped,
    Intra,
    Inter,
  }

  private readonly record struct MacroblockPreview(
    MacroblockKind Kind,
    int X0, int Y0, int X1, int Y1, int X2, int Y2, int X3, int Y3) {

    internal (int X, int Y) Vector(int block) => block switch {
      0 => (this.X0, this.Y0),
      1 => (this.X1, this.Y1),
      2 => (this.X2, this.Y2),
      3 => (this.X3, this.Y3),
      _ => throw new ArgumentOutOfRangeException(nameof(block)),
    };
  }

  private readonly H263PictureHeader _header;
  private readonly H263Frame _target;
  private readonly H263Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _motionWidth;

  // One vector per 8x8 luminance block. A 16x16 macroblock simply repeats its one vector four times.
  // Storing the actual Annex-F grid means the four-vector predictor and OBMC neighbour lookup share
  // the same state instead of maintaining a second representation beside the baseline path.
  private readonly short[] _vectorX;
  private readonly short[] _vectorY;
  private readonly MacroblockKind[] _macroblockKind;

  private readonly bool[] _groupHasHeader;
  private int _quantiser;
  private int _runStart;
  private int _runEnd;

  private H263PictureDecoder(H263PictureHeader header, H263Frame target, H263Frame? reference) {
    this._header = header;
    this._target = target;
    this._reference = reference;
    this._macroblockWidth = header.MacroblockWidth;
    this._macroblockHeight = header.MacroblockHeight;
    this._motionWidth = this._macroblockWidth * 2;
    this._vectorX = new short[this._motionWidth * this._macroblockHeight * 2];
    this._vectorY = new short[this._vectorX.Length];
    this._macroblockKind = new MacroblockKind[this._macroblockWidth * this._macroblockHeight];
    this._groupHasHeader = new bool[this._macroblockHeight];
    this._quantiser = header.Quantiser;
  }

  internal H263Frame Target => this._target;

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
  // Group / RealVideo run layer
  // ============================================================================================

  internal void DecodePicture(ref H263BitReader reader) {
    var count = this._macroblockWidth * this._macroblockHeight;
    var groupRows = this._header.MacroblockRowsPerGroup;
    this._runStart = 0;
    this._runEnd = count;

    for (var address = 0; address < count; ++address) {
      var row = address / this._macroblockWidth;
      var isGroupStart = address % this._macroblockWidth == 0 && row % groupRows == 0;

      if (this._header.HasGroupLayer && isGroupStart && row != 0 && reader.AtStartCode())
        this._ReadGroupHeader(ref reader, row / groupRows, row);

      this._DecodeMacroblock(ref reader, address);
    }
  }

  internal void DecodeRun(ref H263BitReader reader, int firstAddress, int count, int quantiser) {
    var total = this._macroblockWidth * this._macroblockHeight;
    if (firstAddress < 0 || count < 0 || firstAddress > total - count)
      throw new InvalidDataException(
        $"A run of {count} macroblock(s) beginning at {firstAddress} does not fit in a picture of {total}.");

    this._quantiser = quantiser;
    this._runStart = firstAddress;
    this._runEnd = firstAddress + count;

    for (var address = firstAddress; address < this._runEnd; ++address)
      this._DecodeMacroblock(ref reader, address);
  }

  private void _ReadGroupHeader(ref H263BitReader reader, int expectedGroupNumber, int row) {
    reader.ConsumeStartCode();
    var groupNumber = reader.ReadBits(5);

    switch (groupNumber) {
      case _PICTURE_GROUP_NUMBER:
        throw new InvalidDataException(
          $"A picture start code was reached at macroblock row {row} of an H.263 picture that still has "
          + $"{this._macroblockHeight - row} row(s) to decode.");

      case _END_OF_SEQUENCE:
      case _END_OF_SUB_BITSTREAM:
        throw new InvalidDataException(
          $"An end-of-sequence code (group number {groupNumber}) was reached at macroblock row {row} of an H.263 "
          + "picture that has not finished.");

      default:
        if (groupNumber != expectedGroupNumber)
          throw new InvalidDataException(
            $"An H.263 group of blocks states group number {groupNumber} where {expectedGroupNumber} was due.");
        break;
    }

    reader.ReadBits(2); // GFID
    this._quantiser = _ReadQuantiser(ref reader);
    this._groupHasHeader[row] = true;
  }

  private static int _ReadQuantiser(ref H263BitReader reader) {
    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "An H.263 group of blocks states GQUANT 0. ITU-T H.263 gives QUANT the range 1 to 31.");

    return quantiser;
  }

  // ============================================================================================
  // Macroblock layer
  // ============================================================================================

  private void _DecodeMacroblock(ref H263BitReader reader, int address) {
    int macroblockType;
    int chromaPattern;

    for (; ; ) {
      if (!this._header.IsIntra && reader.ReadBit() == 1) {
        this._macroblockKind[address] = MacroblockKind.Skipped;
        this._SetMacroblockVector(address, 0, 0);
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

    var fourVectors = macroblockType is _INTER_FOUR_VECTORS or _INTER_FOUR_VECTORS_WITH_QUANTISER;
    if (fourVectors && !this._header.UsesAdvancedPrediction)
      throw new InvalidDataException(
        $"Macroblock {address} uses INTER4V although this picture did not enable H.263 Annex F Advanced Prediction.");

    var isIntra = macroblockType is _INTRA or _INTRA_WITH_QUANTISER;
    var luminancePattern = H263VlcTables.LuminancePattern.Read(ref reader);
    if (!isIntra)
      luminancePattern ^= 0xF;

    if (macroblockType is _INTER_WITH_QUANTISER or _INTRA_WITH_QUANTISER or _INTER_FOUR_VECTORS_WITH_QUANTISER)
      this._ApplyQuantiserDifference(ref reader);

    Span<int> vectorX = stackalloc int[4];
    Span<int> vectorY = stackalloc int[4];

    if (isIntra) {
      this._macroblockKind[address] = MacroblockKind.Intra;
      this._SetMacroblockVector(address, 0, 0);
    } else if (fourVectors) {
      this._macroblockKind[address] = MacroblockKind.Inter;
      for (var block = 0; block < 4; ++block) {
        vectorX[block] = this._ReadVector(ref reader, address, block, horizontal: true);
        vectorY[block] = this._ReadVector(ref reader, address, block, horizontal: false);
        this._SetBlockVector(address, block, vectorX[block], vectorY[block]);
      }
    } else {
      this._macroblockKind[address] = MacroblockKind.Inter;
      vectorX[0] = this._ReadVector(ref reader, address, 0, horizontal: true);
      vectorY[0] = this._ReadVector(ref reader, address, 0, horizontal: false);
      vectorX[1] = vectorX[2] = vectorX[3] = vectorX[0];
      vectorY[1] = vectorY[2] = vectorY[3] = vectorY[0];
      this._SetMacroblockVector(address, vectorX[0], vectorY[0]);
    }

    var pattern = (luminancePattern << 2) | chromaPattern;
    if (isIntra) {
      this._ReconstructIntra(ref reader, address, pattern);
      return;
    }

    if (this._header.UsesAdvancedPrediction)
      this._ReconstructAdvancedInter(ref reader, address, pattern, vectorX, vectorY);
    else
      this._ReconstructInter(ref reader, address, pattern, vectorX[0], vectorY[0]);
  }

  private void _ApplyQuantiserDifference(ref H263BitReader reader) {
    var difference = reader.ReadBits(2) switch { 0 => -1, 1 => -2, 2 => 1, _ => 2 };
    this._quantiser = Math.Clamp(this._quantiser + difference, 1, 31);
  }

  // ============================================================================================
  // Motion vectors — baseline, Annex D.2 and Annex F
  // ============================================================================================

  private int _ReadVector(ref H263BitReader reader, int address, int block, bool horizontal) {
    var predictor = this._PredictVector(address, block, horizontal);
    var difference = H263VlcTables.MotionVectorDifference.Read(ref reader);
    return ReconstructVectorComponent(predictor, difference, this._header.UsesExtendedMotionVectorRange);
  }

  /// <summary>
  /// Chooses the member of an H.263 MVD pair for the active component range.
  /// </summary>
  /// <remarks>
  /// Baseline wraps every component into -32..31 half-pixels. Annex D.2 instead permits -63..63;
  /// when the predictor itself is outside the baseline interval, only a result that crosses the far
  /// extended boundary wraps by sixty-four. This is the asymmetric boundary rule in Annex D.2, not
  /// a clamp and not a wider sign extension.
  /// </remarks>
  internal static int ReconstructVectorComponent(int predictor, int difference, bool extendedRange) {
    var vector = predictor + difference;

    if (!extendedRange) {
      if (vector < -32)
        vector += 64;
      else if (vector > 31)
        vector -= 64;
      return vector;
    }

    if (predictor < -31 && vector < -63)
      vector += 64;
    else if (predictor > 32 && vector > 63)
      vector -= 64;

    return vector;
  }

  private int _PredictVector(int address, int block, bool horizontal) {
    var mbX = address % this._macroblockWidth;
    var mbY = address / this._macroblockWidth;
    var gridX = mbX * 2 + (block & 1);
    var gridY = mbY * 2 + (block >> 1);

    var left = this._VectorAt(gridX - 1, gridY, horizontal);

    // Annex F applies the same first-line substitution as 6.1.1 to the two top blocks. A RealVideo
    // run is a resynchronisation boundary for exactly the same reason as a GOB header: vectors before
    // it must not influence a run that can be decoded independently.
    var topBoundary = block < 2 && this._AboveMacroblockUnavailable(address);
    int above, diagonal;
    if (topBoundary) {
      above = left;
      diagonal = left;
    } else {
      above = this._VectorAt(gridX, gridY - 1, horizontal);
      var diagonalX = block switch {
        0 => gridX + 2,
        1 or 2 => gridX + 1,
        _ => gridX - 1,
      };
      diagonal = this._VectorAt(diagonalX, gridY - 1, horizontal);
    }

    // The above-right candidate outside the right picture boundary is zero. Apply this after the
    // first-line substitution, as clause 6.1.1 does for the 16x16 case.
    if ((block is 0 or 1) && (block == 0 ? gridX + 2 : gridX + 1) >= this._motionWidth)
      diagonal = 0;

    return _Median(left, above, diagonal);
  }

  private bool _AboveMacroblockUnavailable(int address) {
    var row = address / this._macroblockWidth;
    return row == 0
           || (row % this._header.MacroblockRowsPerGroup == 0 && this._groupHasHeader[row])
           || address - this._macroblockWidth < this._runStart;
  }

  private int _VectorAt(int gridX, int gridY, bool horizontal) {
    if ((uint)gridX >= (uint)this._motionWidth || (uint)gridY >= (uint)(this._macroblockHeight * 2))
      return 0;

    var candidateAddress = (gridY >> 1) * this._macroblockWidth + (gridX >> 1);
    if (candidateAddress < this._runStart)
      return 0;

    var vectors = horizontal ? this._vectorX : this._vectorY;
    return vectors[gridY * this._motionWidth + gridX];
  }

  private void _SetMacroblockVector(int address, int x, int y) {
    for (var block = 0; block < 4; ++block)
      this._SetBlockVector(address, block, x, y);
  }

  private void _SetBlockVector(int address, int block, int x, int y) {
    var mbX = address % this._macroblockWidth;
    var mbY = address / this._macroblockWidth;
    var gridX = mbX * 2 + (block & 1);
    var gridY = mbY * 2 + (block >> 1);
    var at = gridY * this._motionWidth + gridX;
    this._vectorX[at] = checked((short)x);
    this._vectorY[at] = checked((short)y);
  }

  private (int X, int Y) _BlockVector(int address, int block) {
    var mbX = address % this._macroblockWidth;
    var mbY = address / this._macroblockWidth;
    var at = (mbY * 2 + (block >> 1)) * this._motionWidth + mbX * 2 + (block & 1);
    return (this._vectorX[at], this._vectorY[at]);
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    if (b > c)
      b = c;
    return a > b ? a : b;
  }

  // ============================================================================================
  // Reconstruction
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

  private void _ReconstructAdvancedInter(
    ref H263BitReader reader, int address, int pattern, scoped ReadOnlySpan<int> vectorX, scoped ReadOnlySpan<int> vectorY) {
    var reference = this._reference
      ?? throw new InvalidDataException("An Advanced Prediction macroblock has no reference picture.");

    Span<int> residual = stackalloc int[6 * 64];
    residual.Clear();
    for (var index = 0; index < 6; ++index)
      if (_IsCoded(pattern, index))
        H263BlockDecoder.ReadInter(
          ref reader, residual.Slice(index * 64, 64), this._quantiser, this._header.HasWideEscapeLevel);

    var right = this._PreviewRightMacroblock(reader, address);
    Span<int> prediction = stackalloc int[64];

    for (var block = 0; block < 4; ++block) {
      var current = (X: vectorX[block], Y: vectorY[block]);
      var top = block >= 2
        ? (X: vectorX[block - 2], Y: vectorY[block - 2])
        : this._RemoteVector(address - this._macroblockWidth, block + 2, current);
      var left = (block & 1) != 0
        ? (X: vectorX[block - 1], Y: vectorY[block - 1])
        : this._RemoteVector(address - 1, block + 1, current);
      var rightVector = (block & 1) == 0
        ? (X: vectorX[block + 1], Y: vectorY[block + 1])
        : _RemoteRightVector(right, block - 1, current);
      var bottom = block < 2
        ? (X: vectorX[block + 2], Y: vectorY[block + 2])
        : current;

      var (originX, originY) = this._BlockOrigin(address, block);
      if (!H263MotionCompensation.TryPredictOverlapped(
            prediction, reference.Luma, reference.LumaWidth, originX, originY,
            current.X, current.Y,
            top.X, top.Y, left.X, left.Y, rightVector.X, rightVector.Y, bottom.X, bottom.Y,
            this._header.AllowsVectorsOutsidePicture))
        throw new InvalidDataException(
          $"Advanced Prediction block {block} of macroblock {address} reaches outside its reference picture.");

      var blockData = residual.Slice(block * 64, 64);
      for (var i = 0; i < 64; ++i)
        blockData[i] += prediction[i];
      this._Store(address, block, blockData);
    }

    var sumX = vectorX[0] + vectorX[1] + vectorX[2] + vectorX[3];
    var sumY = vectorY[0] + vectorY[1] + vectorY[2] + vectorY[3];
    var chromaX = H263MotionCompensation.FourVectorChroma(sumX);
    var chromaY = H263MotionCompensation.FourVectorChroma(sumY);

    for (var index = 4; index < 6; ++index) {
      var (plane, width, _) = index == 4
        ? (reference.Cb, reference.ChromaWidth, reference.ChromaHeight)
        : (reference.Cr, reference.ChromaWidth, reference.ChromaHeight);
      var (originX, originY) = this._BlockOrigin(address, index);
      if (!H263MotionCompensation.TryPredict(
            prediction, plane, width, originX, originY, chromaX, chromaY,
            this._header.AllowsVectorsOutsidePicture))
        throw new InvalidDataException(
          $"Advanced Prediction chroma block {index} of macroblock {address} reaches outside its reference picture.");

      var blockData = residual.Slice(index * 64, 64);
      for (var i = 0; i < 64; ++i)
        blockData[i] += prediction[i];
      this._Store(address, index, blockData);
    }
  }

  /// <summary>
  /// Peeks only the next macroblock's type and motion vectors. Annex F needs those right-neighbour
  /// vectors while reconstructing the current block; coefficients remain untouched and the real
  /// reader is not advanced. This is the bitstream-level reason OBMC cannot simply be bolted onto a
  /// completed baseline prediction after the fact.
  /// </summary>
  private MacroblockPreview? _PreviewRightMacroblock(H263BitReader reader, int address) {
    if (address + 1 >= this._runEnd || address % this._macroblockWidth + 1 >= this._macroblockWidth)
      return null;

    var next = address + 1;
    Span<(int X, int Y)> saved = stackalloc (int X, int Y)[4];
    for (var block = 0; block < 4; ++block)
      saved[block] = this._BlockVector(next, block);

    try {
      int macroblockType;
      for (; ; ) {
        if (reader.ReadBit() == 1)
          return new(MacroblockKind.Skipped, 0, 0, 0, 0, 0, 0, 0, 0);

        var mcbpc = H263VlcTables.PredictedMacroblockType.Read(ref reader);
        if (mcbpc == H263VlcTables.McbpcStuffing)
          continue;

        macroblockType = H263VlcTables.TypeOf(mcbpc);
        break;
      }

      if (macroblockType is _INTRA or _INTRA_WITH_QUANTISER)
        return new(MacroblockKind.Intra, 0, 0, 0, 0, 0, 0, 0, 0);

      _ = H263VlcTables.LuminancePattern.Read(ref reader);
      if (macroblockType is _INTER_WITH_QUANTISER or _INTER_FOUR_VECTORS_WITH_QUANTISER)
        reader.ReadBits(2);

      Span<int> x = stackalloc int[4];
      Span<int> y = stackalloc int[4];
      if (macroblockType is _INTER_FOUR_VECTORS or _INTER_FOUR_VECTORS_WITH_QUANTISER) {
        for (var block = 0; block < 4; ++block) {
          x[block] = this._ReadVector(ref reader, next, block, horizontal: true);
          y[block] = this._ReadVector(ref reader, next, block, horizontal: false);
          this._SetBlockVector(next, block, x[block], y[block]);
        }
      } else {
        x[0] = this._ReadVector(ref reader, next, 0, horizontal: true);
        y[0] = this._ReadVector(ref reader, next, 0, horizontal: false);
        x[1] = x[2] = x[3] = x[0];
        y[1] = y[2] = y[3] = y[0];
        this._SetMacroblockVector(next, x[0], y[0]);
      }

      return new(MacroblockKind.Inter, x[0], y[0], x[1], y[1], x[2], y[2], x[3], y[3]);
    } finally {
      for (var block = 0; block < 4; ++block)
        this._SetBlockVector(next, block, saved[block].X, saved[block].Y);
    }
  }

  private (int X, int Y) _RemoteVector(int remoteAddress, int remoteBlock, (int X, int Y) current) {
    if (remoteAddress < this._runStart || remoteAddress < 0 || remoteAddress >= this._macroblockKind.Length)
      return current;

    var currentRow = (remoteAddress + (remoteAddress < this._runStart ? 0 : this._macroblockWidth)) / this._macroblockWidth;
    _ = currentRow;

    return this._macroblockKind[remoteAddress] switch {
      MacroblockKind.Skipped => (0, 0),
      MacroblockKind.Inter => this._BlockVector(remoteAddress, remoteBlock),
      _ => current,
    };
  }

  private static (int X, int Y) _RemoteRightVector(
    MacroblockPreview? preview, int block, (int X, int Y) current) {
    if (preview is not { } remote)
      return current;

    return remote.Kind switch {
      MacroblockKind.Skipped => (0, 0),
      MacroblockKind.Inter => remote.Vector(block),
      _ => current,
    };
  }

  private void _CopyFromReference(int address) {
    Span<int> prediction = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, address, index, 0, 0);
      this._Store(address, index, prediction);
    }
  }

  private void _Predict(Span<int> prediction, int address, int index, int vectorX, int vectorY) {
    var reference = this._reference
      ?? throw new InvalidDataException(
        $"Macroblock {address} is predicted, but the picture holds no reference to predict from.");

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
        plane[row + x] = (byte)Math.Clamp(value, 0, 255);
      }
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
