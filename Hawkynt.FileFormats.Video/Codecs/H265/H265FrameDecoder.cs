using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Decodes one HEVC picture in coding-tree-block tile-scan order, reconstructing coding units,
/// transform units and prediction units as their syntax is consumed.
/// </summary>
internal sealed class H265FrameDecoder {

  private const int _LOG2_MIN_BLOCK = 2;
  private const int _MIN_BLOCK = 1 << _LOG2_MIN_BLOCK;

  private readonly H265SequenceParameterSet _sps;
  private readonly H265PictureParameterSet _pps;
  private readonly H265Picture _picture;
  private readonly H265TileLayout _tiles;

  private readonly int _blocksAcross;
  private readonly int _blocksDown;

  private readonly short[] _sliceIndex;
  private readonly byte[] _predictionMode;
  private readonly byte[] _intraPredModeY;
  private readonly byte[] _codingTreeDepth;
  private readonly bool[] _transquantBypass;
  private readonly bool[] _pulseCodeModulated;
  private readonly bool[] _skipped;
  private readonly bool[] _hasCodedResidual;
  private readonly bool[] _transformEdgeVertical;
  private readonly bool[] _transformEdgeHorizontal;
  private readonly bool[] _predictionEdgeVertical;
  private readonly bool[] _predictionEdgeHorizontal;
  private readonly sbyte[] _blockQp;
  private readonly int _log2QuantisationGroupSize;
  private readonly byte[] _saoTypeIdx;
  private readonly sbyte[] _saoOffsets;
  private readonly byte[] _saoBandOrClass;

  private readonly List<bool> _sliceLoopFilterAcross = [];
  private readonly List<(bool Disabled, int Beta, int Tc)> _sliceDeblocking = [];

  private readonly int _log2CtbSize;
  private readonly int _ctbSize;
  private readonly int _minBlocksPerCtbSide;

  private H265SliceHeader _header = null!;
  private H265CabacEngine _cabac;
  private readonly byte[] _contexts = new byte[H265CabacContexts.COUNT];
  private readonly byte[] _wavefrontContexts = new byte[H265CabacContexts.COUNT];
  private bool _wavefrontContextsStored;
  private int _sliceOrdinal = -1;
  private int _segmentStartTs;
  private IReadOnlyList<H265Picture>[] _referenceLists = [[], []];

  private int _previousQuantisationGroupQp;
  private int _currentQuantisationGroupIndex;
  private int _quantisationGroupPredictedQp;
  private int _currentCodingUnitQp;
  private bool _quantiserDeltaCoded;
  private int _quantiserDelta;
  private int _quantisationGroupX;
  private int _quantisationGroupY;

  private int _cuX;
  private int _cuY;
  private int _cuLog2Size;
  private H265PredictionMode _cuPredictionMode;
  private H265PartitionMode _cuPartitionMode;
  private bool _cuTransquantBypass;
  private int _cuMaxTransformDepth;
  private bool _cuIntraSplit;

  private readonly int[] _reference = new int[4 * 32 + 1];
  private readonly bool[] _referenceAvailable = new bool[4 * 32 + 1];
  private readonly int[] _prediction = new int[32 * 32];
  private readonly int[] _coefficients = new int[32 * 32];

  internal H265FrameDecoder(H265SequenceParameterSet sps, H265PictureParameterSet pps) {
    this._sps = sps;
    this._pps = pps;
    this._picture = new(sps.Width, sps.Height, _LOG2_MIN_BLOCK);
    this._tiles = new(sps, pps);

    this._log2CtbSize = sps.CtbLog2SizeY;
    this._ctbSize = sps.CtbSizeY;
    this._minBlocksPerCtbSide = this._ctbSize >> _LOG2_MIN_BLOCK;

    this._blocksAcross = (sps.Width + _MIN_BLOCK - 1) >> _LOG2_MIN_BLOCK;
    this._blocksDown = (sps.Height + _MIN_BLOCK - 1) >> _LOG2_MIN_BLOCK;
    var blocks = this._blocksAcross * this._blocksDown;

    this._sliceIndex = new short[blocks];
    Array.Fill(this._sliceIndex, (short)-1);

    this._predictionMode = new byte[blocks];
    this._intraPredModeY = new byte[blocks];
    this._codingTreeDepth = new byte[blocks];
    this._transquantBypass = new bool[blocks];
    this._pulseCodeModulated = new bool[blocks];
    this._skipped = new bool[blocks];
    this._hasCodedResidual = new bool[blocks];
    this._transformEdgeVertical = new bool[blocks];
    this._transformEdgeHorizontal = new bool[blocks];
    this._predictionEdgeVertical = new bool[blocks];
    this._predictionEdgeHorizontal = new bool[blocks];

    this._log2QuantisationGroupSize = sps.CtbLog2SizeY - pps.DiffCuQpDeltaDepth;
    this._blockQp = new sbyte[blocks];

    this._saoTypeIdx = new byte[sps.PicSizeInCtbsY * 3];
    this._saoOffsets = new sbyte[sps.PicSizeInCtbsY * 3 * 5];
    this._saoBandOrClass = new byte[sps.PicSizeInCtbsY * 3];
  }

  internal H265Picture Picture => this._picture;
  internal H265SequenceParameterSet Sps => this._sps;
  internal H265PictureParameterSet Pps => this._pps;

  private int _decodedCtbs;

  /// <summary>
  /// Decodes one slice segment. Independent segments open a slice; dependent segments retain that
  /// slice's context variables and slice identity while starting a new arithmetic-coded NAL payload.
  /// CTBs are advanced by CtbAddrInTs, with raster addresses used only for picture coordinates.
  /// </summary>
  internal void DecodeSliceSegment(H265SliceHeader header, IReadOnlyList<H265Picture>[] referenceLists) {
    this._header = header;
    this._referenceLists = referenceLists;

    if (!header.DependentSliceSegment) {
      this._sliceOrdinal = this._sliceLoopFilterAcross.Count;
      this._sliceLoopFilterAcross.Add(header.LoopFilterAcrossSlicesEnabled);
      this._sliceDeblocking.Add((header.DeblockingFilterDisabled, header.BetaOffsetDiv2, header.TcOffsetDiv2));
      this._wavefrontContextsStored = false;
    } else if (this._sliceOrdinal < 0)
      throw new InvalidDataException(
        "An H.265 dependent slice segment reached the picture decoder without an independent segment first.");

    this._cabac = new(header.Nal.Payload, this._contexts);

    var width = this._sps.PicWidthInCtbsY;
    var total = this._sps.PicSizeInCtbsY;
    var startTs = this._tiles.ToTileScan(header.SegmentAddress);
    this._segmentStartTs = startTs;
    var substream = 0;

    var initType = header.SliceType switch {
      H265SliceType.I => 0,
      H265SliceType.P => header.CabacInitFlag ? 2 : 1,
      _ => header.CabacInitFlag ? 1 : 2,
    };

    for (var ts = startTs; ts < total; ++ts) {
      var ctb = this._tiles.ToRasterScan(ts);
      var column = ctb % width;
      var row = ctb / width;
      var first = ts == startTs;
      var tileStart = this._pps.TilesEnabled && this._tiles.IsTileStart(ctb);
      var wavefrontStart = this._IsWavefrontRowStart(ctb);
      var startsSubstream = first || (!first && (tileStart || wavefrontStart));

      if (startsSubstream) {
        this._StartSubstream(
          header, initType, ctb, first, tileStart, wavefrontStart, ref substream);

        // QPY_PREV is reset at independent slice starts and entropy-substream boundaries. A dependent
        // segment beginning in the middle of a subset is the exception: it continues the same slice
        // and therefore the previous quantisation state as well as the CABAC contexts.
        if (!first || !header.DependentSliceSegment || tileStart || wavefrontStart)
          this._ResetQuantisationState(header.SliceQpY);
      }

      this._DecodeCodingTreeUnit(ctb, column, row);
      ++this._decodedCtbs;

      // WPP stores the state after the second CTB of each tile-local row. The next row of that same
      // tile can start from it; a one-CTB-wide tile has no such synchronisation point.
      if (this._pps.EntropyCodingSyncEnabled
          && column == this._tiles.TileColumnStart(ctb) + 1) {
        Array.Copy(this._contexts, this._wavefrontContexts, H265CabacContexts.COUNT);
        this._wavefrontContextsStored = true;
      }

      // end_of_slice_segment_flag is distinct from end_of_subset_one_bit. The old WPP path consumed
      // only one terminate bin and therefore mistook every row boundary for the end of the slice.
      if (this._cabac.DecodeTerminate() != 0) {
        if (substream != header.SubstreamOffsets.Length)
          throw new InvalidDataException(
            $"An H.265 slice segment ended after consuming {substream} of its "
            + $"{header.SubstreamOffsets.Length} entry-point offsets.");
        return;
      }

      if (ts + 1 < total) {
        var next = this._tiles.ToRasterScan(ts + 1);
        if (this._StartsNewSubset(next) && this._cabac.DecodeTerminate() == 0)
          throw new InvalidDataException(
            "An H.265 entropy-coded subset ended without end_of_subset_one_bit being set. The CABAC decoder is "
            + "out of step with the tile/WPP boundary.");
      }
    }

    throw new InvalidDataException(
      $"An H.265 {header.SliceType} slice segment at picture order count {header.PicOrderCntLsb} ran past the last "
      + "coding tree block of the picture without its end_of_slice_segment_flag being set. The entropy decoder is "
      + "out of step with the bitstream.");
  }

  internal void RefuseIfIncomplete() {
    if (this._decodedCtbs >= this._sps.PicSizeInCtbsY)
      return;

    throw new InvalidDataException(
      $"An H.265 picture was left {this._sps.PicSizeInCtbsY - this._decodedCtbs} of its "
      + $"{this._sps.PicSizeInCtbsY} coding tree blocks undecoded: its slices do not cover it. Handing back a "
      + "picture with holes in it would be handing back a picture that was never coded.");
  }

  private bool _StartsNewSubset(int ctb)
    => (this._pps.TilesEnabled && this._tiles.IsTileStart(ctb)) || this._IsWavefrontRowStart(ctb);

  private bool _IsWavefrontRowStart(int ctb) {
    if (!this._pps.EntropyCodingSyncEnabled)
      return false;

    var width = this._sps.PicWidthInCtbsY;
    var column = ctb % width;
    var row = ctb / width;
    return column == this._tiles.TileColumnStart(ctb) && row > this._tiles.TileRowStart(ctb);
  }

  private void _StartSubstream(
    H265SliceHeader header,
    int initType,
    int ctb,
    bool first,
    bool tileStart,
    bool wavefrontStart,
    ref int substream) {
    if (tileStart) {
      H265CabacContexts.Initialize(this._contexts, initType, header.SliceQpY);
      this._wavefrontContextsStored = false;
    } else if (wavefrontStart) {
      if (this._wavefrontContextsStored && (!first || header.DependentSliceSegment))
        Array.Copy(this._wavefrontContexts, this._contexts, H265CabacContexts.COUNT);
      else
        H265CabacContexts.Initialize(this._contexts, initType, header.SliceQpY);
    } else if (!(first && header.DependentSliceSegment))
      H265CabacContexts.Initialize(this._contexts, initType, header.SliceQpY);

    var offset = first
      ? header.DataOffset
      : header.SubstreamOffsets.Length > substream
        ? header.SubstreamOffsets[substream++]
        : throw new InvalidDataException(
          "An H.265 slice segment enables tiles or entropy coding synchronisation but states fewer entry-point "
          + "offsets than its entropy-coded subsets require.");

    this._cabac.Start(offset);
  }

  private void _ResetQuantisationState(int sliceQp) {
    this._previousQuantisationGroupQp = sliceQp;
    this._currentCodingUnitQp = sliceQp;
    this._currentQuantisationGroupIndex = -1;
    this._quantiserDeltaCoded = false;
    this._quantiserDelta = 0;
  }

  private void _DecodeCodingTreeUnit(int ctb, int column, int row) {
    var x = column << this._log2CtbSize;
    var y = row << this._log2CtbSize;

    if (this._header.SaoLuma || this._header.SaoChroma)
      this._DecodeSampleAdaptiveOffset(ctb, column, row);

    this._DecodeCodingQuadtree(x, y, this._log2CtbSize, 0);
  }

  private void _DecodeSampleAdaptiveOffset(int ctb, int column, int row) {
    var mergeLeft = false;
    var mergeUp = false;
    var width = this._sps.PicWidthInCtbsY;

    if (column > 0) {
      var left = ctb - 1;
      if (this._tiles.SameTile(ctb, left) && this._tiles.ToTileScan(left) >= this._segmentStartTs)
        mergeLeft = this._cabac.DecodeBin(H265CabacContexts.SAO_MERGE) != 0;
    }

    if (!mergeLeft && row > 0) {
      var up = ctb - width;
      if (this._tiles.SameTile(ctb, up) && this._tiles.ToTileScan(up) >= this._segmentStartTs)
        mergeUp = this._cabac.DecodeBin(H265CabacContexts.SAO_MERGE) != 0;
    }

    if (mergeLeft || mergeUp) {
      var source = mergeLeft ? ctb - 1 : ctb - width;
      for (var component = 0; component < 3; ++component) {
        this._saoTypeIdx[ctb * 3 + component] = this._saoTypeIdx[source * 3 + component];
        this._saoBandOrClass[ctb * 3 + component] = this._saoBandOrClass[source * 3 + component];
        Array.Copy(this._saoOffsets, (source * 3 + component) * 5,
          this._saoOffsets, (ctb * 3 + component) * 5, 5);
      }
      return;
    }

    for (var component = 0; component < 3; ++component) {
      var slot = ctb * 3 + component;
      this._saoTypeIdx[slot] = 0;
      Array.Clear(this._saoOffsets, slot * 5, 5);

      if (component == 0 ? !this._header.SaoLuma : !this._header.SaoChroma)
        continue;

      if (component < 2) {
        var type = 0;
        if (this._cabac.DecodeBin(H265CabacContexts.SAO_TYPE_IDX) != 0)
          type = this._cabac.DecodeBypass() != 0 ? 2 : 1;
        this._saoTypeIdx[slot] = (byte)type;
      } else
        this._saoTypeIdx[slot] = this._saoTypeIdx[ctb * 3 + 1];

      if (this._saoTypeIdx[slot] == 0)
        continue;

      var magnitudes = new int[4];
      var depth = component == 0 ? this._sps.BitDepthLuma : this._sps.BitDepthChroma;
      var maximum = (1 << (Math.Min(depth, 10) - 5)) - 1;
      for (var i = 0; i < 4; ++i) {
        var value = 0;
        while (value < maximum && this._cabac.DecodeBypass() != 0)
          ++value;
        magnitudes[i] = value;
      }

      if (this._saoTypeIdx[slot] == 1) {
        for (var i = 0; i < 4; ++i)
          if (magnitudes[i] != 0 && this._cabac.DecodeBypass() != 0)
            magnitudes[i] = -magnitudes[i];
        this._saoBandOrClass[slot] = (byte)this._cabac.DecodeBypassBits(5);
      } else {
        magnitudes[2] = -magnitudes[2];
        magnitudes[3] = -magnitudes[3];
        this._saoBandOrClass[slot] = component < 2
          ? (byte)this._cabac.DecodeBypassBits(2)
          : this._saoBandOrClass[ctb * 3 + 1];
      }

      for (var i = 0; i < 4; ++i)
        this._saoOffsets[slot * 5 + i + 1] = (sbyte)magnitudes[i];
    }
  }

  private void _DecodeCodingQuadtree(int x0, int y0, int log2CbSize, int depth) {
    var size = 1 << log2CbSize;

    var split = log2CbSize > this._sps.MinCbLog2SizeY;
    if (x0 + size <= this._sps.Width && y0 + size <= this._sps.Height && log2CbSize > this._sps.MinCbLog2SizeY)
      split = this._cabac.DecodeBin(
        H265CabacContexts.SPLIT_CU_FLAG + this._SplitContext(x0, y0, depth)) != 0;

    if (log2CbSize >= this._log2QuantisationGroupSize)
      this._BeginQuantisationGroup(x0, y0);

    if (!split) {
      this._DecodeCodingUnit(x0, y0, log2CbSize, depth);
      return;
    }

    var half = size >> 1;
    var x1 = x0 + half;
    var y1 = y0 + half;

    this._DecodeCodingQuadtree(x0, y0, log2CbSize - 1, depth + 1);
    if (x1 < this._sps.Width)
      this._DecodeCodingQuadtree(x1, y0, log2CbSize - 1, depth + 1);
    if (y1 < this._sps.Height)
      this._DecodeCodingQuadtree(x0, y1, log2CbSize - 1, depth + 1);
    if (x1 < this._sps.Width && y1 < this._sps.Height)
      this._DecodeCodingQuadtree(x1, y1, log2CbSize - 1, depth + 1);
  }

  private int _SplitContext(int x0, int y0, int depth) {
    var context = 0;
    if (this._IsAvailable(x0, y0, x0 - 1, y0) && this._codingTreeDepth[this._BlockIndex(x0 - 1, y0)] > depth)
      ++context;
    if (this._IsAvailable(x0, y0, x0, y0 - 1) && this._codingTreeDepth[this._BlockIndex(x0, y0 - 1)] > depth)
      ++context;
    return context;
  }

  private void _BeginQuantisationGroup(int x0, int y0) {
    var index = this._QuantisationGroupIndex(x0, y0);
    if (this._currentQuantisationGroupIndex >= 0 && index != this._currentQuantisationGroupIndex)
      this._previousQuantisationGroupQp = this._currentCodingUnitQp;

    this._quantiserDeltaCoded = false;
    this._quantiserDelta = 0;
    this._quantisationGroupX = x0;
    this._quantisationGroupY = y0;
    this._currentQuantisationGroupIndex = index;
    this._quantisationGroupPredictedQp = this._PredictQuantiser();
  }

  private int _QuantisationGroupIndex(int x, int y)
    => ((y >> this._log2QuantisationGroupSize) << 16) | (x >> this._log2QuantisationGroupSize);

  private int _PredictQuantiser() {
    var x = this._quantisationGroupX;
    var y = this._quantisationGroupY;
    var previous = this._previousQuantisationGroupQp;

    var left = previous;
    if (x > 0 && (x - 1) >> this._log2CtbSize == x >> this._log2CtbSize && this._IsInSameSlice(x, y, x - 1, y))
      left = this._blockQp[this._BlockIndex(x - 1, y)];

    var above = previous;
    if (y > 0 && (y - 1) >> this._log2CtbSize == y >> this._log2CtbSize && this._IsInSameSlice(x, y, x, y - 1))
      above = this._blockQp[this._BlockIndex(x, y - 1)];

    return (left + above + 1) >> 1;
  }

  private void _SetQuantiser(int delta) {
    this._quantiserDelta = delta;
    this._currentCodingUnitQp = this._WrapQuantiser(this._quantisationGroupPredictedQp + delta);
    this._RecordCodingUnitQuantiser();
  }

  private int _WrapQuantiser(int qp) {
    var offset = this._sps.QpBdOffsetLuma;
    return ((qp + 52 + 2 * offset) % (52 + offset)) - offset;
  }

  private void _RecordCodingUnitQuantiser()
    => this._FillBlocks(
      this._blockQp, this._cuX, this._cuY, 1 << this._cuLog2Size, 1 << this._cuLog2Size,
      (sbyte)this._currentCodingUnitQp);

  private void _DecodeCodingUnit(int x0, int y0, int log2CbSize, int depth) {
    var size = 1 << log2CbSize;

    this._cuX = x0;
    this._cuY = y0;
    this._cuLog2Size = log2CbSize;
    this._cuTransquantBypass = false;
    this._cuPredictionMode = H265PredictionMode.Intra;
    this._cuPartitionMode = H265PartitionMode.Square;
    this._cuIntraSplit = false;

    this._MarkBlocks(x0, y0, size, size, depth);

    this._currentCodingUnitQp = this._WrapQuantiser(this._quantisationGroupPredictedQp + this._quantiserDelta);
    this._RecordCodingUnitQuantiser();

    if (this._pps.TransquantBypassEnabled)
      this._cuTransquantBypass = this._cabac.DecodeBin(H265CabacContexts.CU_TRANSQUANT_BYPASS_FLAG) != 0;

    var skipped = false;
    if (!this._header.IsIntra)
      skipped = this._cabac.DecodeBin(H265CabacContexts.CU_SKIP_FLAG + this._SkipContext(x0, y0)) != 0;

    this._FillBlocks(this._skipped, x0, y0, size, size, skipped);
    this._FillBlocks(this._transquantBypass, x0, y0, size, size, this._cuTransquantBypass);

    if (skipped) {
      this._cuPredictionMode = H265PredictionMode.Inter;
      this._FillBlocks(this._predictionMode, x0, y0, size, size, (byte)H265PredictionMode.Inter);
      this._MarkPredictionEdges(x0, y0, size, size);
      this._MarkTransformEdges(x0, y0, size, size);
      this._DecodePredictionUnit(x0, y0, size, size, 0, true);
      return;
    }

    if (!this._header.IsIntra)
      this._cuPredictionMode = this._cabac.DecodeBin(H265CabacContexts.PRED_MODE_FLAG) != 0
        ? H265PredictionMode.Intra
        : H265PredictionMode.Inter;

    this._FillBlocks(this._predictionMode, x0, y0, size, size, (byte)this._cuPredictionMode);

    if (this._cuPredictionMode != H265PredictionMode.Intra || log2CbSize == this._sps.MinCbLog2SizeY)
      this._cuPartitionMode = this._DecodePartitionMode(log2CbSize);

    this._cuIntraSplit = this._cuPredictionMode == H265PredictionMode.Intra
                         && this._cuPartitionMode == H265PartitionMode.Quarters;

    this._cuMaxTransformDepth = this._cuPredictionMode == H265PredictionMode.Intra
      ? this._sps.MaxTransformHierarchyDepthIntra + (this._cuIntraSplit ? 1 : 0)
      : this._sps.MaxTransformHierarchyDepthInter;

    if (this._cuPredictionMode == H265PredictionMode.Intra) {
      if (this._DecodeIntraCodingUnit(x0, y0, log2CbSize))
        return;
    } else
      this._DecodeInterCodingUnit(x0, y0, log2CbSize);

    var rootHasResidual = true;
    if (this._cuPredictionMode != H265PredictionMode.Intra
        && !(this._cuPartitionMode == H265PartitionMode.Square && this._cuMergedWholeUnit))
      rootHasResidual = this._cabac.DecodeBin(H265CabacContexts.RQT_ROOT_CBF) != 0;

    if (rootHasResidual)
      this._DecodeTransformTree(x0, y0, x0, y0, log2CbSize, 0, 0, true, true);
    else
      this._MarkTransformEdges(x0, y0, size, size);
  }

  private bool _cuMergedWholeUnit;

  private int _SkipContext(int x0, int y0) {
    var context = 0;
    if (this._IsAvailable(x0, y0, x0 - 1, y0) && this._skipped[this._BlockIndex(x0 - 1, y0)])
      ++context;
    if (this._IsAvailable(x0, y0, x0, y0 - 1) && this._skipped[this._BlockIndex(x0, y0 - 1)])
      ++context;
    return context;
  }

  private H265PartitionMode _DecodePartitionMode(int log2CbSize) {
    if (this._cuPredictionMode == H265PredictionMode.Intra)
      return this._cabac.DecodeBin(H265CabacContexts.PART_MODE) != 0
        ? H265PartitionMode.Square
        : H265PartitionMode.Quarters;

    if (this._cabac.DecodeBin(H265CabacContexts.PART_MODE) != 0)
      return H265PartitionMode.Square;

    var horizontal = this._cabac.DecodeBin(H265CabacContexts.PART_MODE + 1) != 0;

    if (log2CbSize > this._sps.MinCbLog2SizeY) {
      if (!this._sps.AmpEnabled)
        return horizontal ? H265PartitionMode.HorizontalHalves : H265PartitionMode.VerticalHalves;

      if (this._cabac.DecodeBin(H265CabacContexts.PART_MODE + 3) != 0)
        return horizontal ? H265PartitionMode.HorizontalHalves : H265PartitionMode.VerticalHalves;

      var second = this._cabac.DecodeBypass() != 0;
      return horizontal
        ? second ? H265PartitionMode.HorizontalQuarterBottom : H265PartitionMode.HorizontalQuarterTop
        : second ? H265PartitionMode.VerticalQuarterRight : H265PartitionMode.VerticalQuarterLeft;
    }

    if (horizontal)
      return H265PartitionMode.HorizontalHalves;
    if (log2CbSize == 3)
      return H265PartitionMode.VerticalHalves;

    return this._cabac.DecodeBin(H265CabacContexts.PART_MODE + 2) != 0
      ? H265PartitionMode.VerticalHalves
      : H265PartitionMode.Quarters;
  }

  private bool _DecodeIntraCodingUnit(int x0, int y0, int log2CbSize) {
    var size = 1 << log2CbSize;

    if (this._cuPartitionMode == H265PartitionMode.Square
        && this._sps.PcmEnabled
        && log2CbSize >= this._sps.Log2MinPcmCbSizeY
        && log2CbSize <= this._sps.Log2MaxPcmCbSizeY
        && this._cabac.DecodeTerminate() != 0) {
      this._DecodePulseCodeModulatedBlock(x0, y0, log2CbSize);
      return true;
    }

    var parts = this._cuPartitionMode == H265PartitionMode.Quarters ? 2 : 1;
    var step = size / parts;
    var useMostProbable = new bool[4];
    for (var i = 0; i < parts * parts; ++i)
      useMostProbable[i] = this._cabac.DecodeBin(H265CabacContexts.PREV_INTRA_LUMA_PRED_FLAG) != 0;

    var modes = new int[4];
    for (var i = 0; i < parts * parts; ++i) {
      var x = x0 + (i % parts) * step;
      var y = y0 + (i / parts) * step;
      var candidates = this._MostProbableModes(x, y);

      if (useMostProbable[i]) {
        var index = this._cabac.DecodeBypass();
        if (index != 0)
          index += this._cabac.DecodeBypass();
        modes[i] = candidates[index];
      } else {
        var remaining = this._cabac.DecodeBypassBits(5);
        Array.Sort(candidates);
        var mode = remaining;
        for (var k = 0; k < 3; ++k)
          if (mode >= candidates[k])
            ++mode;
        modes[i] = mode;
      }

      this._FillBlocks(this._intraPredModeY, x, y, step, step, (byte)modes[i]);
    }

    this._chromaPredMode = this._DecodeChromaMode(modes[0]);
    return false;
  }

  private int _chromaPredMode;

  private int[] _MostProbableModes(int x, int y) {
    var left = this._NeighbourIntraMode(x, y, x - 1, y, false);
    var above = this._NeighbourIntraMode(x, y, x, y - 1, true);

    if (left == above) {
      if (left < 2)
        return [H265IntraPrediction.PLANAR, H265IntraPrediction.DC, H265IntraPrediction.VERTICAL];
      return [left, 2 + ((left + 29) % 32), 2 + ((left - 2 + 1) % 32)];
    }

    var third = H265IntraPrediction.PLANAR;
    if (left != H265IntraPrediction.PLANAR && above != H265IntraPrediction.PLANAR)
      third = H265IntraPrediction.PLANAR;
    else if (left != H265IntraPrediction.DC && above != H265IntraPrediction.DC)
      third = H265IntraPrediction.DC;
    else
      third = H265IntraPrediction.VERTICAL;
    return [left, above, third];
  }

  private int _NeighbourIntraMode(int x, int y, int nx, int ny, bool above) {
    if (!this._IsAvailable(x, y, nx, ny))
      return H265IntraPrediction.DC;
    if (above && ny >> this._log2CtbSize != y >> this._log2CtbSize)
      return H265IntraPrediction.DC;

    var index = this._BlockIndex(nx, ny);
    if (this._predictionMode[index] != (byte)H265PredictionMode.Intra || this._pulseCodeModulated[index])
      return H265IntraPrediction.DC;
    return this._intraPredModeY[index];
  }

  private int _DecodeChromaMode(int lumaMode) {
    if (this._cabac.DecodeBin(H265CabacContexts.INTRA_CHROMA_PRED_MODE) == 0)
      return lumaMode;

    var index = this._cabac.DecodeBypassBits(2);
    int[] candidates = [
      H265IntraPrediction.PLANAR, H265IntraPrediction.VERTICAL, H265IntraPrediction.HORIZONTAL,
      H265IntraPrediction.DC,
    ];
    return candidates[index] == lumaMode ? 34 : candidates[index];
  }

  private void _DecodePulseCodeModulatedBlock(int x0, int y0, int log2CbSize) {
    throw new NotSupportedException(
      "This H.265 stream carries a coding unit whose samples were sent uncompressed (pcm_flag, clause 7.3.8.7). "
      + "Reading one means leaving the arithmetic decoder, taking the samples as raw bits at the sequence's own "
      + $"depth and restarting the decoder afterwards; that is not implemented. The block is at ({x0}, {y0}), "
      + $"{1 << log2CbSize} samples across.");
  }

  private void _DecodeInterCodingUnit(int x0, int y0, int log2CbSize) {
    var size = 1 << log2CbSize;
    var half = size >> 1;
    var quarter = size >> 2;
    this._cuMergedWholeUnit = false;

    switch (this._cuPartitionMode) {
      case H265PartitionMode.Square:
        this._MarkPredictionEdges(x0, y0, size, size);
        this._cuMergedWholeUnit = this._DecodePredictionUnit(x0, y0, size, size, 0, false);
        return;
      case H265PartitionMode.HorizontalHalves:
        this._MarkPredictionEdges(x0, y0, size, half);
        this._MarkPredictionEdges(x0, y0 + half, size, half);
        this._DecodePredictionUnit(x0, y0, size, half, 0, false);
        this._DecodePredictionUnit(x0, y0 + half, size, half, 1, false);
        return;
      case H265PartitionMode.VerticalHalves:
        this._MarkPredictionEdges(x0, y0, half, size);
        this._MarkPredictionEdges(x0 + half, y0, half, size);
        this._DecodePredictionUnit(x0, y0, half, size, 0, false);
        this._DecodePredictionUnit(x0 + half, y0, half, size, 1, false);
        return;
      case H265PartitionMode.HorizontalQuarterTop:
        this._MarkPredictionEdges(x0, y0, size, quarter);
        this._MarkPredictionEdges(x0, y0 + quarter, size, size - quarter);
        this._DecodePredictionUnit(x0, y0, size, quarter, 0, false);
        this._DecodePredictionUnit(x0, y0 + quarter, size, size - quarter, 1, false);
        return;
      case H265PartitionMode.HorizontalQuarterBottom:
        this._MarkPredictionEdges(x0, y0, size, size - quarter);
        this._MarkPredictionEdges(x0, y0 + size - quarter, size, quarter);
        this._DecodePredictionUnit(x0, y0, size, size - quarter, 0, false);
        this._DecodePredictionUnit(x0, y0 + size - quarter, size, quarter, 1, false);
        return;
      case H265PartitionMode.VerticalQuarterLeft:
        this._MarkPredictionEdges(x0, y0, quarter, size);
        this._MarkPredictionEdges(x0 + quarter, y0, size - quarter, size);
        this._DecodePredictionUnit(x0, y0, quarter, size, 0, false);
        this._DecodePredictionUnit(x0 + quarter, y0, size - quarter, size, 1, false);
        return;
      case H265PartitionMode.VerticalQuarterRight:
        this._MarkPredictionEdges(x0, y0, size - quarter, size);
        this._MarkPredictionEdges(x0 + size - quarter, y0, quarter, size);
        this._DecodePredictionUnit(x0, y0, size - quarter, size, 0, false);
        this._DecodePredictionUnit(x0 + size - quarter, y0, quarter, size, 1, false);
        return;
      default:
        for (var i = 0; i < 4; ++i) {
          var x = x0 + (i & 1) * half;
          var y = y0 + (i >> 1) * half;
          this._MarkPredictionEdges(x, y, half, half);
          this._DecodePredictionUnit(x, y, half, half, i, false);
        }
        return;
    }
  }

  private bool _DecodePredictionUnit(int x, int y, int width, int height, int partIdx, bool skipped) {
    var merged = skipped;
    var mergeIndex = 0;
    var motion = H265MotionInfo.None;

    if (skipped)
      mergeIndex = this._DecodeMergeIndex();
    else {
      merged = this._cabac.DecodeBin(H265CabacContexts.MERGE_FLAG) != 0;
      if (merged)
        mergeIndex = this._DecodeMergeIndex();
    }

    if (merged)
      motion = H265MotionPrediction.DeriveMerge(this, x, y, width, height, partIdx, mergeIndex);
    else
      motion = this._DecodeExplicitMotion(x, y, width, height, partIdx);

    this._StoreMotion(x, y, width, height, motion);
    H265MotionCompensation.Predict(this, x, y, width, height, motion);
    return merged;
  }

  private int _DecodeMergeIndex() {
    if (this._header.MaxNumMergeCand <= 1)
      return 0;
    if (this._cabac.DecodeBin(H265CabacContexts.MERGE_IDX) == 0)
      return 0;

    var index = 1;
    while (index < this._header.MaxNumMergeCand - 1 && this._cabac.DecodeBypass() != 0)
      ++index;
    return index;
  }

  private H265MotionInfo _DecodeExplicitMotion(int x, int y, int width, int height, int partIdx) {
    var motion = H265MotionInfo.None;
    var direction = 0;
    if (this._header.SliceType == H265SliceType.B)
      direction = this._DecodeInterPredictionDirection(width, height);

    for (var list = 0; list < 2; ++list) {
      if (direction == 1 - list)
        continue;

      var active = list == 0 ? this._header.NumRefIdxL0Active : this._header.NumRefIdxL1Active;
      var refIdx = active > 1 ? this._DecodeReferenceIndex(active) : 0;
      var mvdX = 0;
      var mvdY = 0;
      if (!(list == 1 && this._header.MvdL1Zero && direction == 2))
        this._DecodeMotionVectorDifference(out mvdX, out mvdY);

      var predictorFlag = this._cabac.DecodeBin(H265CabacContexts.MVP_FLAG);
      var predictor = H265MotionPrediction.DerivePredictor(
        this, x, y, width, height, partIdx, list, refIdx, predictorFlag);

      motion.Set(list, true, refIdx,
        _WrapMotionVector(predictor.X + mvdX), _WrapMotionVector(predictor.Y + mvdY));
    }

    return motion;
  }

  private static int _WrapMotionVector(int value) => (short)value;

  private int _DecodeInterPredictionDirection(int width, int height) {
    if (width + height != 12
        && this._cabac.DecodeBin(H265CabacContexts.INTER_PRED_IDC + this._codingTreeDepthOfCurrentUnit) != 0)
      return 2;
    return this._cabac.DecodeBin(H265CabacContexts.INTER_PRED_IDC + 4);
  }

  private int _codingTreeDepthOfCurrentUnit;

  private int _DecodeReferenceIndex(int active) {
    if (this._cabac.DecodeBin(H265CabacContexts.REF_IDX) == 0)
      return 0;
    if (active == 2 || this._cabac.DecodeBin(H265CabacContexts.REF_IDX + 1) == 0)
      return 1;

    var index = 2;
    while (index < active - 1 && this._cabac.DecodeBypass() != 0)
      ++index;
    return index;
  }

  private void _DecodeMotionVectorDifference(out int mvdX, out int mvdY) {
    var greaterX = this._cabac.DecodeBin(H265CabacContexts.ABS_MVD_GREATER0_FLAG) != 0;
    var greaterY = this._cabac.DecodeBin(H265CabacContexts.ABS_MVD_GREATER0_FLAG) != 0;
    var muchGreaterX = greaterX && this._cabac.DecodeBin(H265CabacContexts.ABS_MVD_GREATER1_FLAG) != 0;
    var muchGreaterY = greaterY && this._cabac.DecodeBin(H265CabacContexts.ABS_MVD_GREATER1_FLAG) != 0;
    mvdX = this._DecodeMotionVectorDifferenceComponent(greaterX, muchGreaterX);
    mvdY = this._DecodeMotionVectorDifferenceComponent(greaterY, muchGreaterY);
  }

  private int _DecodeMotionVectorDifferenceComponent(bool greater, bool muchGreater) {
    if (!greater)
      return 0;
    var magnitude = 1;
    if (muchGreater)
      magnitude = 2 + _DecodeExponentialGolomb(ref this._cabac, 1);
    return this._cabac.DecodeBypass() != 0 ? -magnitude : magnitude;
  }

  private static int _DecodeExponentialGolomb(ref H265CabacEngine cabac, int order) {
    var value = 0;
    var k = order;
    while (cabac.DecodeBypass() != 0) {
      value += 1 << k;
      ++k;
      if (k > 30)
        throw new InvalidDataException(
          "An H.265 exponential-Golomb code exceeded 30 bits of prefix, which no conforming stream contains.");
    }
    return value + cabac.DecodeBypassBits(k);
  }

  private void _StoreMotion(int x, int y, int width, int height, in H265MotionInfo motion) {
    for (var by = y >> _LOG2_MIN_BLOCK; by < (y + height) >> _LOG2_MIN_BLOCK; ++by)
      for (var bx = x >> _LOG2_MIN_BLOCK; bx < (x + width) >> _LOG2_MIN_BLOCK; ++bx) {
        var index = by * this._blocksAcross + bx;
        this._picture.Motion.Set(0, index, motion.PredictL0, motion.RefIdxL0, motion.MvL0X, motion.MvL0Y);
        this._picture.Motion.Set(1, index, motion.PredictL1, motion.RefIdxL1, motion.MvL1X, motion.MvL1Y);
        this._picture.IsIntraBlock[index] = false;
      }
  }

  private void _DecodeTransformTree(
    int x0, int y0, int xBase, int yBase, int log2TrafoSize, int trafoDepth, int blockIdx,
    bool parentCbfCb, bool parentCbfCr) {
    var split = log2TrafoSize > this._sps.MaxTbLog2SizeY
                || (this._cuIntraSplit && trafoDepth == 0)
                || (this._sps.MaxTransformHierarchyDepthInter == 0
                    && this._cuPredictionMode == H265PredictionMode.Inter
                    && this._cuPartitionMode != H265PartitionMode.Square
                    && trafoDepth == 0);

    if (log2TrafoSize <= this._sps.MaxTbLog2SizeY
        && log2TrafoSize > this._sps.MinTbLog2SizeY
        && trafoDepth < this._cuMaxTransformDepth
        && !(this._cuIntraSplit && trafoDepth == 0))
      split = this._cabac.DecodeBin(
        H265CabacContexts.SPLIT_TRANSFORM_FLAG + 5 - log2TrafoSize) != 0;

    var cbfCb = false;
    var cbfCr = false;
    if (log2TrafoSize > 2) {
      if (trafoDepth == 0 || parentCbfCb)
        cbfCb = this._cabac.DecodeBin(H265CabacContexts.CBF_CHROMA + trafoDepth) != 0;
      if (trafoDepth == 0 || parentCbfCr)
        cbfCr = this._cabac.DecodeBin(H265CabacContexts.CBF_CHROMA + trafoDepth) != 0;
    } else {
      cbfCb = parentCbfCb;
      cbfCr = parentCbfCr;
    }

    if (split) {
      var half = 1 << (log2TrafoSize - 1);
      this._DecodeTransformTree(x0, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 0, cbfCb, cbfCr);
      this._DecodeTransformTree(x0 + half, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 1, cbfCb, cbfCr);
      this._DecodeTransformTree(x0, y0 + half, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 2, cbfCb, cbfCr);
      this._DecodeTransformTree(x0 + half, y0 + half, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 3, cbfCb, cbfCr);
      return;
    }

    var cbfLuma = true;
    if (this._cuPredictionMode == H265PredictionMode.Intra || trafoDepth != 0 || cbfCb || cbfCr)
      cbfLuma = this._cabac.DecodeBin(
        H265CabacContexts.CBF_LUMA + (trafoDepth == 0 ? 1 : 0)) != 0;

    this._DecodeTransformUnit(x0, y0, xBase, yBase, log2TrafoSize, trafoDepth, blockIdx, cbfLuma, cbfCb, cbfCr);
  }

  private void _DecodeTransformUnit(
    int x0, int y0, int xBase, int yBase, int log2TrafoSize, int trafoDepth, int blockIdx,
    bool cbfLuma, bool cbfCb, bool cbfCr) {
    var size = 1 << log2TrafoSize;
    this._MarkTransformEdges(x0, y0, size, size);
    this._FillBlocks(this._hasCodedResidual, x0, y0, size, size, cbfLuma);

    var chromaAtParent = log2TrafoSize == 2;
    var anyChroma = chromaAtParent ? blockIdx == 3 && (cbfCb || cbfCr) : cbfCb || cbfCr;

    if ((cbfLuma || cbfCb || cbfCr)
        && this._pps.CuQpDeltaEnabled && !this._quantiserDeltaCoded) {
      this._quantiserDeltaCoded = true;
      this._SetQuantiser(this._DecodeQuantiserDelta());
    }

    var qp = this._QuantiserAt(x0, y0);
    if (this._cuPredictionMode == H265PredictionMode.Intra)
      this._ReconstructIntraLuma(x0, y0, log2TrafoSize, cbfLuma, qp);
    else if (cbfLuma)
      this._AddLumaResidual(x0, y0, log2TrafoSize, qp);

    if (chromaAtParent) {
      if (blockIdx != 3)
        return;
      if (this._cuPredictionMode == H265PredictionMode.Intra)
        this._ReconstructIntraChroma(xBase, yBase, 2, cbfCb, cbfCr, qp);
      else if (anyChroma)
        this._AddChromaResidual(xBase, yBase, 2, cbfCb, cbfCr, qp);
      return;
    }

    if (this._cuPredictionMode == H265PredictionMode.Intra)
      this._ReconstructIntraChroma(x0, y0, log2TrafoSize - 1, cbfCb, cbfCr, qp);
    else if (anyChroma)
      this._AddChromaResidual(x0, y0, log2TrafoSize - 1, cbfCb, cbfCr, qp);
  }

  private int _DecodeQuantiserDelta() {
    var prefix = 0;
    while (prefix < 5
           && this._cabac.DecodeBin(H265CabacContexts.CU_QP_DELTA_ABS + (prefix == 0 ? 0 : 1)) != 0)
      ++prefix;

    var magnitude = prefix;
    if (prefix == 5)
      magnitude += _DecodeExponentialGolomb(ref this._cabac, 0);
    if (magnitude == 0)
      return 0;
    return this._cabac.DecodeBypass() != 0 ? -magnitude : magnitude;
  }

  private int _QuantiserAt(int x, int y) => this._blockQp[this._BlockIndex(x, y)];

  private void _ReconstructIntraLuma(int x0, int y0, int log2Size, bool hasResidual, int qp) {
    var size = 1 << log2Size;
    var mode = this._intraPredModeY[this._BlockIndex(x0, y0)];
    this._GatherReference(x0, y0, size, mode);
    H265IntraPrediction.Predict(this._prediction, this._reference, size, mode, true, this._sps.BitDepthLuma);

    var transformSkip = false;
    if (hasResidual)
      transformSkip = H265Residual.Decode(
        ref this._cabac, this._coefficients, log2Size, 0, mode, this._pps, this._cuTransquantBypass);

    this._Reconstruct(
      this._picture.Luma, this._picture.Width, x0, y0, log2Size, hasResidual, transformSkip,
      this._LumaQuantiser(qp), 0, this._sps.BitDepthLuma, mode);
  }

  /// <summary>
  /// <c>Qp′Y</c>: the quantiser the dequantiser of clause 8.6.2 works with. <c>QpY</c> itself runs
  /// down to <c>−QpBdOffsetY</c> — twelve below zero for a ten-bit sequence — and the scale table is
  /// indexed from zero, so the offset goes back in here. It stays out of everything that compares
  /// quantisers to each other, which is what the deblocking filter and the chroma table do.
  /// </summary>
  private int _LumaQuantiser(int qpY) => qpY + this._sps.QpBdOffsetLuma;

  private void _ReconstructIntraChroma(int x0, int y0, int log2Size, bool cbfCb, bool cbfCr, int qp) {
    var chromaX = x0 >> 1;
    var chromaY = y0 >> 1;
    var size = 1 << log2Size;
    var mode = this._chromaPredMode;

    for (var component = 1; component <= 2; ++component) {
      var hasResidual = component == 1 ? cbfCb : cbfCr;
      var plane = this._picture.Chroma(component - 1);
      this._GatherReferenceChroma(chromaX, chromaY, size, component);
      H265IntraPrediction.Predict(this._prediction, this._reference, size, mode, false, this._sps.BitDepthChroma);

      var transformSkip = false;
      if (hasResidual)
        transformSkip = H265Residual.Decode(
          ref this._cabac, this._coefficients, log2Size, component, mode, this._pps, this._cuTransquantBypass);

      this._Reconstruct(
        plane, this._picture.ChromaWidth, chromaX, chromaY, log2Size, hasResidual, transformSkip,
        this._ChromaQuantiser(qp, component), component, this._sps.BitDepthChroma, -1);
    }
  }

  private void _AddLumaResidual(int x0, int y0, int log2Size, int qp) {
    var transformSkip = H265Residual.Decode(
      ref this._cabac, this._coefficients, log2Size, 0, -1, this._pps, this._cuTransquantBypass);
    this._AddResidual(
      this._picture.Luma, this._picture.Width, x0, y0, log2Size, transformSkip,
      this._LumaQuantiser(qp), 0, this._sps.BitDepthLuma, false);
  }

  private void _AddChromaResidual(int x0, int y0, int log2Size, bool cbfCb, bool cbfCr, int qp) {
    var chromaX = x0 >> 1;
    var chromaY = y0 >> 1;
    for (var component = 1; component <= 2; ++component) {
      if (component == 1 ? !cbfCb : !cbfCr)
        continue;
      var transformSkip = H265Residual.Decode(
        ref this._cabac, this._coefficients, log2Size, component, -1, this._pps, this._cuTransquantBypass);
      this._AddResidual(
        this._picture.Chroma(component - 1), this._picture.ChromaWidth, chromaX, chromaY, log2Size,
        transformSkip, this._ChromaQuantiser(qp, component), component, this._sps.BitDepthChroma, false);
    }
  }

  private int _ChromaQuantiser(int lumaQp, int component) {
    var offset = component == 1
      ? this._pps.CbQpOffset + this._header.SliceCbQpOffset
      : this._pps.CrQpOffset + this._header.SliceCrQpOffset;
    var index = Math.Clamp(lumaQp + offset, -this._sps.QpBdOffsetChroma, 57);
    return H265Dequantiser.ChromaQp(index) + this._sps.QpBdOffsetChroma;
  }

  private void _Reconstruct(
    ushort[] plane, int stride, int x0, int y0, int log2Size, bool hasResidual, bool transformSkip,
    int qp, int component, int bitDepth, int intraMode) {
    var size = 1 << log2Size;
    var maximum = (1 << bitDepth) - 1;
    if (hasResidual)
      this._TransformResidual(log2Size, transformSkip, qp, component, bitDepth, intraMode);

    for (var y = 0; y < size; ++y) {
      var row = (y0 + y) * stride + x0;
      for (var x = 0; x < size; ++x) {
        var value = this._prediction[(y << log2Size) + x];
        if (hasResidual)
          value += this._coefficients[(y << log2Size) + x];
        plane[row + x] = (ushort)Math.Clamp(value, 0, maximum);
      }
    }
  }

  private void _AddResidual(
    ushort[] plane, int stride, int x0, int y0, int log2Size, bool transformSkip, int qp, int component,
    int bitDepth, bool intra) {
    var size = 1 << log2Size;
    var maximum = (1 << bitDepth) - 1;
    this._TransformResidual(log2Size, transformSkip, qp, component, bitDepth, intra ? 0 : -1);

    for (var y = 0; y < size; ++y) {
      var row = (y0 + y) * stride + x0;
      for (var x = 0; x < size; ++x)
        plane[row + x] = (ushort)Math.Clamp(plane[row + x] + this._coefficients[(y << log2Size) + x], 0, maximum);
    }
  }

  private void _TransformResidual(
    int log2Size, bool transformSkip, int qp, int component, int bitDepth, int intraMode) {
    if (this._cuTransquantBypass)
      return;

    var scalingList = this._sps.ScalingListEnabled
      ? this._pps.ScalingList ?? this._sps.ScalingList
      : null;
    if (transformSkip && log2Size > 2)
      scalingList = null;

    var matrixId = component + (this._cuPredictionMode == H265PredictionMode.Intra ? 0 : 3);
    H265Dequantiser.Scale(this._coefficients, log2Size, qp, bitDepth, scalingList, matrixId);
    if (transformSkip) {
      H265Transform.Skip(this._coefficients, log2Size, bitDepth);
      return;
    }

    var sine = log2Size == 2 && component == 0 && this._cuPredictionMode == H265PredictionMode.Intra;
    _ = intraMode;
    H265Transform.Inverse(this._coefficients, log2Size, sine, bitDepth);
  }

  private void _GatherReference(int x0, int y0, int size, int mode) {
    var count = H265IntraPrediction.ReferenceCount(size);
    Array.Clear(this._referenceAvailable, 0, count);
    var plane = this._picture.Luma;
    var stride = this._picture.Width;
    var width = this._picture.Width;
    var height = this._picture.Height;

    this._referenceAvailable[H265IntraPrediction.CornerIndex(size)] =
      this._TakeReference(plane, stride, width, height, x0, y0, x0 - 1, y0 - 1,
        H265IntraPrediction.CornerIndex(size));

    for (var i = 0; i < size << 1; ++i) {
      this._referenceAvailable[H265IntraPrediction.AboveIndex(size, i)] =
        this._TakeReference(plane, stride, width, height, x0, y0, x0 + i, y0 - 1,
          H265IntraPrediction.AboveIndex(size, i));
      this._referenceAvailable[H265IntraPrediction.LeftIndex(size, i)] =
        this._TakeReference(plane, stride, width, height, x0, y0, x0 - 1, y0 + i,
          H265IntraPrediction.LeftIndex(size, i));
    }

    H265IntraPrediction.Substitute(this._reference, this._referenceAvailable, size, this._sps.BitDepthLuma);
    if (H265IntraPrediction.FilterReference(mode, size))
      H265IntraPrediction.Filter(
        this._reference, size, this._sps.StrongIntraSmoothingEnabled, this._sps.BitDepthLuma);
  }

  private void _GatherReferenceChroma(int x0, int y0, int size, int component) {
    var count = H265IntraPrediction.ReferenceCount(size);
    Array.Clear(this._referenceAvailable, 0, count);
    var plane = this._picture.Chroma(component - 1);
    var stride = this._picture.ChromaWidth;
    var width = this._picture.ChromaWidth;
    var height = this._picture.ChromaHeight;

    this._referenceAvailable[H265IntraPrediction.CornerIndex(size)] =
      this._TakeReferenceChroma(plane, stride, width, height, x0, y0, x0 - 1, y0 - 1,
        H265IntraPrediction.CornerIndex(size));

    for (var i = 0; i < size << 1; ++i) {
      this._referenceAvailable[H265IntraPrediction.AboveIndex(size, i)] =
        this._TakeReferenceChroma(plane, stride, width, height, x0, y0, x0 + i, y0 - 1,
          H265IntraPrediction.AboveIndex(size, i));
      this._referenceAvailable[H265IntraPrediction.LeftIndex(size, i)] =
        this._TakeReferenceChroma(plane, stride, width, height, x0, y0, x0 - 1, y0 + i,
          H265IntraPrediction.LeftIndex(size, i));
    }

    H265IntraPrediction.Substitute(this._reference, this._referenceAvailable, size, this._sps.BitDepthChroma);
  }

  private bool _TakeReference(
    ushort[] plane, int stride, int width, int height, int x0, int y0, int x, int y, int slot) {
    if (x < 0 || y < 0 || x >= width || y >= height)
      return false;
    if (!this._IsPredictable(x0, y0, x, y))
      return false;
    this._reference[slot] = plane[y * stride + x];
    return true;
  }

  private bool _TakeReferenceChroma(
    ushort[] plane, int stride, int width, int height, int x0, int y0, int x, int y, int slot) {
    if (x < 0 || y < 0 || x >= width || y >= height)
      return false;
    if (!this._IsPredictable(x0 << 1, y0 << 1, x << 1, y << 1))
      return false;
    this._reference[slot] = plane[y * stride + x];
    return true;
  }

  private bool _IsPredictable(int x0, int y0, int x, int y) {
    if (!this._IsAvailable(x0, y0, x, y))
      return false;
    if (!this._pps.ConstrainedIntraPred)
      return true;
    return this._predictionMode[this._BlockIndex(x, y)] == (byte)H265PredictionMode.Intra;
  }

  internal bool _IsAvailable(int x0, int y0, int x, int y) {
    if (x < 0 || y < 0 || x >= this._sps.Width || y >= this._sps.Height)
      return false;

    var currentCtb = this._CtbRasterAddress(x0, y0);
    var neighbourCtb = this._CtbRasterAddress(x, y);
    if (!this._tiles.SameTile(currentCtb, neighbourCtb))
      return false;

    if (this._ZScanAddress(x, y) >= this._ZScanAddress(x0, y0))
      return false;

    return this._IsInSameSlice(x0, y0, x, y);
  }

  private bool _IsInSameSlice(int x0, int y0, int x, int y) {
    var neighbour = this._sliceIndex[this._BlockIndex(x, y)];
    if (neighbour < 0)
      return false;
    var current = this._sliceIndex[this._BlockIndex(x0, y0)];
    return current < 0 ? neighbour == this._sliceOrdinal : neighbour == current;
  }

  private int _CtbRasterAddress(int x, int y)
    => (y >> this._log2CtbSize) * this._sps.PicWidthInCtbsY + (x >> this._log2CtbSize);

  private long _ZScanAddress(int x, int y) {
    var rs = this._CtbRasterAddress(x, y);
    var ctb = (long)this._tiles.ToTileScan(rs);
    var withinX = (x >> _LOG2_MIN_BLOCK) & (this._minBlocksPerCtbSide - 1);
    var withinY = (y >> _LOG2_MIN_BLOCK) & (this._minBlocksPerCtbSide - 1);

    var morton = 0L;
    for (var bit = 0; (1 << bit) < this._minBlocksPerCtbSide; ++bit)
      morton |= (long)((withinX >> bit) & 1) << (bit << 1)
                | (long)((withinY >> bit) & 1) << ((bit << 1) + 1);

    return ctb * this._minBlocksPerCtbSide * this._minBlocksPerCtbSide + morton;
  }

  private int _BlockIndex(int x, int y) => (y >> _LOG2_MIN_BLOCK) * this._blocksAcross + (x >> _LOG2_MIN_BLOCK);

  private void _MarkBlocks(int x0, int y0, int width, int height, int depth) {
    this._codingTreeDepthOfCurrentUnit = Math.Min(depth, 4);
    for (var y = y0; y < Math.Min(y0 + height, this._sps.Height); y += _MIN_BLOCK)
      for (var x = x0; x < Math.Min(x0 + width, this._sps.Width); x += _MIN_BLOCK) {
        var index = this._BlockIndex(x, y);
        this._sliceIndex[index] = (short)this._sliceOrdinal;
        this._codingTreeDepth[index] = (byte)depth;
        this._picture.IsIntraBlock[index] = true;
      }
  }

  private void _FillBlocks<T>(T[] target, int x0, int y0, int width, int height, T value) {
    for (var y = y0; y < Math.Min(y0 + height, this._sps.Height); y += _MIN_BLOCK)
      for (var x = x0; x < Math.Min(x0 + width, this._sps.Width); x += _MIN_BLOCK)
        target[this._BlockIndex(x, y)] = value;
  }

  private void _MarkTransformEdges(int x0, int y0, int width, int height) {
    for (var y = y0; y < Math.Min(y0 + height, this._sps.Height); y += _MIN_BLOCK)
      this._transformEdgeVertical[this._BlockIndex(x0, y)] = true;
    for (var x = x0; x < Math.Min(x0 + width, this._sps.Width); x += _MIN_BLOCK)
      this._transformEdgeHorizontal[this._BlockIndex(x, y0)] = true;
  }

  private void _MarkPredictionEdges(int x0, int y0, int width, int height) {
    for (var y = y0; y < Math.Min(y0 + height, this._sps.Height); y += _MIN_BLOCK)
      this._predictionEdgeVertical[this._BlockIndex(x0, y)] = true;
    for (var x = x0; x < Math.Min(x0 + width, this._sps.Width); x += _MIN_BLOCK)
      this._predictionEdgeHorizontal[this._BlockIndex(x, y0)] = true;
  }

  internal int BlocksAcross => this._blocksAcross;
  internal int BlocksDown => this._blocksDown;
  internal H265SliceHeader Header => this._header;
  internal IReadOnlyList<H265Picture> ReferenceList(int list) => this._referenceLists[list];
  internal int BlockIndexAt(int x, int y) => this._BlockIndex(x, y);
  internal int CodingBlockX => this._cuX;
  internal int CodingBlockY => this._cuY;
  internal int CodingBlockSize => 1 << this._cuLog2Size;
  internal H265PartitionMode CodingBlockPartitionMode => this._cuPartitionMode;

  internal H265MotionInfo MotionAt(int index) {
    var motion = this._picture.Motion;
    return new() {
      PredictL0 = motion.PredictionFlagL0[index],
      PredictL1 = motion.PredictionFlagL1[index],
      RefIdxL0 = motion.RefIdxL0[index],
      RefIdxL1 = motion.RefIdxL1[index],
      MvL0X = motion.MvL0X[index],
      MvL0Y = motion.MvL0Y[index],
      MvL1X = motion.MvL1X[index],
      MvL1Y = motion.MvL1Y[index],
    };
  }

  internal H265Picture? CollocatedPicture {
    get {
      if (!this._header.TemporalMvpEnabled)
        return null;
      var list = this._referenceLists[this._header.CollocatedFromL0 ? 0 : 1];
      return this._header.CollocatedRefIdx < list.Count ? list[this._header.CollocatedRefIdx] : null;
    }
  }

  internal bool IsAvailableAt(int x0, int y0, int x, int y) => this._IsAvailable(x0, y0, x, y);
  internal bool SameTileAt(int x0, int y0, int x, int y)
    => this._tiles.SameTile(this._CtbRasterAddress(x0, y0), this._CtbRasterAddress(x, y));
  internal bool IsIntraAt(int index) => this._predictionMode[index] == (byte)H265PredictionMode.Intra;
  internal bool IsPulseCodeModulatedAt(int index) => this._pulseCodeModulated[index];
  internal bool IsTransquantBypassAt(int index) => this._transquantBypass[index];
  internal bool HasCodedResidualAt(int index) => this._hasCodedResidual[index];
  internal bool IsTransformEdgeVertical(int index) => this._transformEdgeVertical[index];
  internal bool IsTransformEdgeHorizontal(int index) => this._transformEdgeHorizontal[index];
  internal bool IsPredictionEdgeVertical(int index) => this._predictionEdgeVertical[index];
  internal bool IsPredictionEdgeHorizontal(int index) => this._predictionEdgeHorizontal[index];
  internal int SliceOfBlock(int index) => this._sliceIndex[index];
  internal bool LoopFilterAcrossSlices(int slice)
    => slice >= 0 && slice < this._sliceLoopFilterAcross.Count && this._sliceLoopFilterAcross[slice];
  internal int QuantiserAt(int x, int y) => this._QuantiserAt(x, y);
  internal int SaoTypeAt(int ctb, int component) => this._saoTypeIdx[ctb * 3 + component];
  internal int SaoOffsetAt(int ctb, int component, int index) => this._saoOffsets[(ctb * 3 + component) * 5 + index];
  internal int SaoBandOrClassAt(int ctb, int component) => this._saoBandOrClass[ctb * 3 + component];

  internal (bool Disabled, int Beta, int Tc) DeblockingParameters(int slice)
    => slice >= 0 && slice < this._sliceDeblocking.Count
      ? this._sliceDeblocking[slice]
      : (true, 0, 0);
}
