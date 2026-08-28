using System;
using System.IO;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Decodes the tiles of one frame: the recursive partition of every superblock, the mode info of
/// every block, the coefficients, the prediction and the reconstruction (specification 6.4).
/// </summary>
/// <remarks>
/// The order of work inside a block is fixed by the format. Mode info first, for the whole block;
/// then, plane by plane, the inter prediction of the whole block at once — because a motion vector
/// covers the block and not a transform of it — followed by transform block by transform block the
/// intra prediction, the coefficients and the reconstruction. Intra prediction reads the
/// reconstruction of the transform blocks before it, so the three cannot be separated into passes.
/// <para/>
/// A good deal of state is held in fields rather than passed along, and that is deliberate: the
/// specification writes these as globals visible to every syntax structure, and a dozen of them are
/// read by the context derivations of section 9.3.2 from several levels away. Threading them through
/// as parameters would mean the same dozen names in twenty signatures, and one of them quietly stale.
/// <para/>
/// The class is split across several files by subject. This one holds the state, the tile and
/// partition walk, and the residual; the others hold the mode info syntax, the motion vector
/// prediction, the probability contexts and the coefficient tokens.
/// </remarks>
internal sealed partial class Vp9FrameDecoder {

  private readonly Vp9FrameHeader _header;
  private readonly Vp9Probabilities _probabilities;
  private readonly Vp9Counts _counts;
  private readonly Vp9InverseTransform _transform = new();
  private readonly Vp9InterPrediction _interPrediction = new();

  private Vp9ModeInfoGrid _grid = null!;
  private Vp9Frame _frame = null!;
  private Vp9Frame?[] _slots = null!;

  private byte[] _packet = [];
  private Vp9BoolDecoder _reader;

  // --------------------------------------------------------------------------------------------
  // Contexts that live for a tile or a superblock row
  // --------------------------------------------------------------------------------------------

  private readonly byte[][] _aboveNonzero = [[], [], []];
  private readonly byte[][] _leftNonzero = [[], [], []];
  private byte[] _abovePartition = [];
  private byte[] _leftPartition = [];
  private byte[] _aboveSegmentPrediction = [];
  private byte[] _leftSegmentPrediction = [];

  private int _miRowStart;
  private int _miRowEnd;
  private int _miColStart;
  private int _miColEnd;

  // --------------------------------------------------------------------------------------------
  // The block being decoded
  // --------------------------------------------------------------------------------------------

  private int _miRow;
  private int _miCol;
  private int _miSize;
  private bool _availableAbove;
  private bool _availableLeft;

  private int _segmentId;
  private bool _skip;
  private bool _isInter;
  private int _transformSize;
  private int _yMode;
  private int _uvMode;
  private int _interpolationFilter;
  private int _eobTotal;

  private readonly byte[] _subModes = new byte[4];
  private readonly sbyte[] _referenceFrame = new sbyte[2];

  /// <summary>Four motion vectors per reference list, two components each (<c>BlockMvs</c>).</summary>
  private readonly short[] _blockMotionVectors = new short[2 * 4 * 2];

  private readonly int[] _leftReferenceFrame = new int[2];
  private readonly int[] _aboveReferenceFrame = new int[2];
  private bool _leftIntra;
  private bool _aboveIntra;
  private bool _leftSingle;
  private bool _aboveSingle;

  /// <summary>The coefficients of one transform block, in raster order within it.</summary>
  private readonly int[] _tokens = new int[32 * 32];

  private readonly byte[] _tokenCache = new byte[32 * 32];

  /// <summary>Which pair of one-dimensional transforms the current transform block uses.</summary>
  private int _transformType;

  internal Vp9FrameDecoder(Vp9FrameHeader header, Vp9Probabilities probabilities, Vp9Counts counts) {
    this._header = header;
    this._probabilities = probabilities;
    this._counts = counts;
  }

  /// <summary>Sizes the per-frame context arrays, which depend only on the picture size.</summary>
  internal void Resize(Vp9ModeInfoGrid grid) {
    this._grid = grid;

    var aboveLength = grid.Columns * 2 + 32;
    var leftLength = grid.Rows * 2 + 32;

    for (var plane = 0; plane < 3; ++plane) {
      this._aboveNonzero[plane] = new byte[aboveLength];
      this._leftNonzero[plane] = new byte[leftLength];
    }

    this._abovePartition = new byte[grid.Columns];
    this._leftPartition = new byte[grid.Rows];
    this._aboveSegmentPrediction = new byte[grid.Columns];
    this._leftSegmentPrediction = new byte[grid.Rows];
  }

  // ============================================================================================
  // Tiles (specification 6.4)
  // ============================================================================================

  internal void DecodeTiles(byte[] packet, int at, int length, Vp9Frame frame, Vp9Frame?[] slots) {
    this._packet = packet;
    this._frame = frame;
    this._slots = slots;

    var tileColumns = 1 << this._header.TileColsLog2;
    var tileRows = 1 << this._header.TileRowsLog2;

    foreach (var plane in this._aboveNonzero)
      Array.Clear(plane);

    Array.Clear(this._abovePartition);
    Array.Clear(this._aboveSegmentPrediction);

    var remaining = length;
    for (var tileRow = 0; tileRow < tileRows; ++tileRow)
    for (var tileColumn = 0; tileColumn < tileColumns; ++tileColumn) {
      var isLast = tileRow == tileRows - 1 && tileColumn == tileColumns - 1;

      int size;
      if (isLast)
        size = remaining;
      else {
        if (remaining < 4)
          throw new InvalidDataException(
            "This VP9 frame ends where a tile size should be. The frame states more tiles than its data holds.");

        size = Vp9BitReader.ReadUnsigned32(packet.AsSpan(at, 4));
        at += 4;
        remaining -= 4;
      }

      if (size < 0 || size > remaining)
        throw new InvalidDataException(
          $"This VP9 frame states a tile of {(uint)size} byte(s) where only {remaining} remain. The packet is "
          + "truncated.");

      this._miRowStart = _TileOffset(tileRow, this._header.MiRows, this._header.TileRowsLog2);
      this._miRowEnd = _TileOffset(tileRow + 1, this._header.MiRows, this._header.TileRowsLog2);
      this._miColStart = _TileOffset(tileColumn, this._header.MiCols, this._header.TileColsLog2);
      this._miColEnd = _TileOffset(tileColumn + 1, this._header.MiCols, this._header.TileColsLog2);

      this._reader = new(packet, at, size);
      this._reader.ReadMarker();
      this._DecodeTile();

      at += size;
      remaining -= size;
    }
  }

  private static int _TileOffset(int tileNumber, int modeInfos, int log2)
    => Math.Min(((tileNumber * ((modeInfos + 7) >> 3)) >> log2) << 3, modeInfos);

  private void _DecodeTile() {
    for (var row = this._miRowStart; row < this._miRowEnd; row += 8) {
      foreach (var plane in this._leftNonzero)
        Array.Clear(plane);

      Array.Clear(this._leftPartition);
      Array.Clear(this._leftSegmentPrediction);

      for (var column = this._miColStart; column < this._miColEnd; column += 8)
        this._DecodePartition(row, column, BLOCK_64X64);
    }
  }

  // ============================================================================================
  // The partition tree (specification 6.4.3)
  // ============================================================================================

  private void _DecodePartition(int row, int column, int size) {
    if (row >= this._header.MiRows || column >= this._header.MiCols)
      return;

    var blocks = Vp9Tables.Blocks8x8Wide[size];
    var half = blocks >> 1;
    var hasRows = row + half < this._header.MiRows;
    var hasColumns = column + half < this._header.MiCols;

    var partition = this._ReadPartition(row, column, size, blocks, hasRows, hasColumns);
    var subsize = Vp9Tables.SubsizeLookup[partition * BLOCK_SIZES + size];

    if (subsize < BLOCK_8X8 || partition == PARTITION_NONE)
      this._DecodeBlock(row, column, subsize);
    else
      switch (partition) {
        case PARTITION_HORZ:
          this._DecodeBlock(row, column, subsize);
          if (hasRows)
            this._DecodeBlock(row + half, column, subsize);
          break;
        case PARTITION_VERT:
          this._DecodeBlock(row, column, subsize);
          if (hasColumns)
            this._DecodeBlock(row, column + half, subsize);
          break;
        default:
          this._DecodePartition(row, column, subsize);
          this._DecodePartition(row, column + half, subsize);
          this._DecodePartition(row + half, column, subsize);
          this._DecodePartition(row + half, column + half, subsize);
          break;
      }

    if (size != BLOCK_8X8 && partition == PARTITION_SPLIT)
      return;

    var aboveMark = (byte)(15 >> Vp9Tables.BlockWidthLog2[subsize]);
    var leftMark = (byte)(15 >> Vp9Tables.BlockHeightLog2[subsize]);
    for (var i = 0; i < blocks; ++i) {
      this._abovePartition[column + i] = aboveMark;
      this._leftPartition[row + i] = leftMark;
    }
  }

  // ============================================================================================
  // One block (specification 6.4.4)
  // ============================================================================================

  private void _DecodeBlock(int row, int column, int size) {
    this._miRow = row;
    this._miCol = column;
    this._miSize = size;
    this._availableAbove = row > 0;
    this._availableLeft = column > this._miColStart;

    this._ReadModeInfo();

    this._eobTotal = 0;
    this._Residual();

    if (this._isInter && size >= BLOCK_8X8 && this._eobTotal == 0)
      this._skip = true;

    this._grid.Fill(
      row, column, size, this._yMode, this._segmentId, this._transformSize, this._skip, this._isInter,
      this._interpolationFilter, this._referenceFrame, this._subModes, this._blockMotionVectors);
  }

  // ============================================================================================
  // Prediction and residual (specification 6.4.21)
  // ============================================================================================

  private void _Residual() {
    var size = Math.Max(this._miSize, BLOCK_8X8);

    for (var plane = 0; plane < 3; ++plane) {
      var transformSize = plane > 0 ? this._ChromaTransformSize() : this._transformSize;
      var step = 1 << transformSize;
      var planeSize = _PlaneBlockSize(size, plane);
      var wide = Vp9Tables.Blocks4x4Wide[planeSize];
      var high = Vp9Tables.Blocks4x4High[planeSize];
      var subX = plane > 0 ? this._header.SubsamplingX : 0;
      var subY = plane > 0 ? this._header.SubsamplingY : 0;
      var baseX = (this._miCol * 8) >> subX;
      var baseY = (this._miRow * 8) >> subY;

      if (this._isInter)
        if (this._miSize < BLOCK_8X8)
          for (var y = 0; y < high; ++y)
          for (var x = 0; x < wide; ++x)
            this._PredictInter(plane, baseX + 4 * x, baseY + 4 * y, 4, 4, y * wide + x);
        else
          this._PredictInter(plane, baseX, baseY, wide * 4, high * 4, 0);

      var maxX = (this._header.MiCols * 8) >> subX;
      var maxY = (this._header.MiRows * 8) >> subY;
      var samples = this._frame.Plane(plane);
      var stride = this._frame.Stride(plane);

      var blockIndex = 0;
      for (var y = 0; y < high; y += step)
      for (var x = 0; x < wide; x += step) {
        var startX = baseX + 4 * x;
        var startY = baseY + 4 * y;
        var nonzero = (byte)0;

        if (startX < maxX && startY < maxY) {
          if (!this._isInter) {
            var mode = plane > 0
              ? this._uvMode
              : this._miSize >= BLOCK_8X8
                ? this._yMode
                : this._subModes[blockIndex];

            Vp9IntraPrediction.Predict(
              samples, stride, startX, startY, transformSize + 2, mode,
              this._availableLeft || x > 0, this._availableAbove || y > 0, x + step < wide,
              maxX - 1, maxY - 1, this._header.BitDepth);
          }

          if (!this._skip) {
            nonzero = this._ReadTokens(plane, startX, startY, transformSize, blockIndex);
            this._Reconstruct(plane, startX, startY, transformSize, samples, stride);
          }
        }

        for (var i = 0; i < step; ++i) {
          this._aboveNonzero[plane][(startX >> 2) + i] = nonzero;
          this._leftNonzero[plane][(startY >> 2) + i] = nonzero;
        }

        ++blockIndex;
      }
    }
  }

  private int _PlaneBlockSize(int size, int plane) {
    var subX = plane > 0 ? this._header.SubsamplingX : 0;
    var subY = plane > 0 ? this._header.SubsamplingY : 0;
    return Vp9Tables.SubsampledSizeLookup[(size * 2 + subX) * 2 + subY];
  }

  private int _ChromaTransformSize()
    => this._miSize < BLOCK_8X8
      ? TX_4X4
      : Math.Min(this._transformSize, Vp9Tables.MaxTransformSize[this._PlaneBlockSize(this._miSize, 1)]);

  private void _PredictInter(int plane, int x, int y, int width, int height, int blockIndex) {
    Span<Vp9Frame?> references = [null, null];
    Span<int> motionVectors = [0, 0, 0, 0];

    var lists = this._referenceFrame[1] > INTRA_FRAME ? 2 : 1;
    for (var list = 0; list < lists; ++list) {
      references[list] = this._Reference(this._referenceFrame[list]);
      Vp9InterPrediction.SelectAndClamp(
        plane, list, blockIndex, this._miSize, this._blockMotionVectors,
        this._miRow, this._miCol, this._header.MiRows, this._header.MiCols,
        this._header.SubsamplingX, this._header.SubsamplingY,
        motionVectors.Slice(list * 2, 2));
    }

    this._interPrediction.Predict(
      this._frame.Plane(plane), this._frame.Stride(plane), x, y, width, height, plane,
      references, motionVectors, this._interpolationFilter,
      this._header.SubsamplingX, this._header.SubsamplingY,
      this._header.FrameWidth, this._header.FrameHeight, this._header.BitDepth);
  }

  private Vp9Frame _Reference(int referenceFrame) {
    var slot = this._header.ReferenceFrameIndex[referenceFrame - LAST_FRAME];
    return this._slots[slot]
           ?? throw new InvalidDataException(
             $"A VP9 block predicts from reference slot {slot}, which no frame of this stream has written. "
             + "Specification 8.2 requires an earlier frame to have filled it.");
  }

  // ============================================================================================
  // Dequantisation and reconstruction (specification 8.6)
  // ============================================================================================

  private void _Reconstruct(int plane, int x, int y, int transformSize, ushort[] samples, int stride) {
    var denominator = transformSize == TX_32X32 ? 2 : 1;
    var sizeLog2 = 2 + transformSize;
    var size = 1 << sizeLog2;
    var count = size * size;

    var alternating = this._Quantiser(plane, false);
    var direct = this._Quantiser(plane, true);

    var block = this._tokens.AsSpan(0, count);
    var directCurrent = block[0];
    for (var i = 0; i < count; ++i)
      block[i] = block[i] * alternating / denominator;

    block[0] = directCurrent * direct / denominator;

    this._transform.Apply(block, sizeLog2, this._transformType, this._header.Lossless);

    var maxSample = (1 << this._header.BitDepth) - 1;
    for (var row = 0; row < size; ++row) {
      var at = (y + row) * stride + x;
      var from = row * size;
      for (var column = 0; column < size; ++column)
        samples[at + column] = (ushort)Clip3(0, maxSample, samples[at + column] + block[from + column]);
    }
  }

  private int _QuantiserIndex() {
    if (!this._header.IsFeatureActive(this._segmentId, SEG_LVL_ALT_Q))
      return this._header.BaseQIndex;

    var data = this._header.Feature(this._segmentId, SEG_LVL_ALT_Q);
    return Clip3(0, 255, this._header.SegmentationAbsoluteValues ? data : this._header.BaseQIndex + data);
  }

  private int _Quantiser(int plane, bool isDirectCurrent) {
    var index = this._QuantiserIndex();
    if (isDirectCurrent)
      return Vp9QuantiserTables.Dc(
        index, plane == 0 ? this._header.DeltaQYDc : this._header.DeltaQUvDc, this._header.BitDepth);

    return Vp9QuantiserTables.Ac(index, plane == 0 ? 0 : this._header.DeltaQUvAc, this._header.BitDepth);
  }
}
