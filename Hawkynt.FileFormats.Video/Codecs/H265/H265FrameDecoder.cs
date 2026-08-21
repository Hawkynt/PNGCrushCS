using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Decodes one picture: the coding tree units of each slice segment, and the state they leave behind
/// for the loop filters — ITU-T H.265, clauses 7.3.8 and 8.4 to 8.7.
/// </summary>
/// <remarks>
/// <b>Three nested quadtrees, not one.</b> A coding tree unit is split into coding units, each of
/// which chooses intra or inter; a coding unit is cut into prediction units by its partition mode,
/// which is not a quadtree at all but one of eight shapes; and a coding unit carries a residual
/// quadtree of transform units, which splits independently of the other two. Nothing about the shape
/// of one determines the shape of another, except where the standard forces it — a quartered intra
/// coding unit must split its transform tree once, because its four prediction blocks each need their
/// own reference samples.
/// <para/>
/// <b>Parsing and reconstruction are one pass here, and that is a decision.</b> The standard
/// describes them as two: a syntax that produces arrays, and a decoding process that consumes them.
/// Doing both at once works because intra prediction reads only samples whose transform blocks come
/// earlier in the same order the syntax is parsed in — the z-scan — so a block's neighbours are
/// always finished before it is reached. It saves holding a picture's worth of coefficients, and it
/// means a stream that goes wrong stops at the block where it went wrong rather than after the whole
/// picture has been read.
/// <para/>
/// <b>What is available is a question about position, not about progress.</b> Whether a neighbouring
/// sample may be predicted from is decided by comparing the two blocks' addresses in the z-scan,
/// exactly as clause 6.4.1 does, rather than by asking whether the neighbour has been written yet.
/// The two agree for every stream — the z-scan is the decoding order — but the positional test is the
/// one that stays right when the order changes, and it is the one that gives the same answer for a
/// chroma block whose luma counterparts were finished several transform units ago.
/// </remarks>
internal sealed class H265FrameDecoder {

  /// <summary>The finest grid any of the per-block state is tracked at: four luma samples.</summary>
  private const int _LOG2_MIN_BLOCK = 2;

  private const int _MIN_BLOCK = 1 << _LOG2_MIN_BLOCK;

  private readonly H265SequenceParameterSet _sps;
  private readonly H265PictureParameterSet _pps;
  private readonly H265Picture _picture;

  private readonly int _blocksAcross;
  private readonly int _blocksDown;

  /// <summary>Which slice each smallest block belongs to, or -1 where none has covered it yet.</summary>
  private readonly short[] _sliceIndex;

  private readonly byte[] _predictionMode;
  private readonly byte[] _intraPredModeY;
  private readonly byte[] _codingTreeDepth;
  private readonly bool[] _transquantBypass;
  private readonly bool[] _pulseCodeModulated;
  private readonly bool[] _skipped;

  /// <summary>Whether the transform block covering each smallest block carried any coefficient.</summary>
  private readonly bool[] _hasCodedResidual;

  private readonly bool[] _transformEdgeVertical;
  private readonly bool[] _transformEdgeHorizontal;
  private readonly bool[] _predictionEdgeVertical;
  private readonly bool[] _predictionEdgeHorizontal;

  /// <summary>The quantiser each coding unit was decoded with, at the finest block grid.</summary>
  private readonly sbyte[] _blockQp;

  private readonly int _log2QuantisationGroupSize;

  private readonly byte[] _saoTypeIdx;
  private readonly sbyte[] _saoOffsets;
  private readonly byte[] _saoBandOrClass;

  /// <summary>Whether the loop filters may cross into a neighbouring slice, per slice.</summary>
  private readonly List<bool> _sliceLoopFilterAcross = [];

  /// <summary>What each slice asked the deblocking filter for.</summary>
  private readonly List<(bool Disabled, int Beta, int Tc)> _sliceDeblocking = [];

  private readonly int _log2CtbSize;
  private readonly int _ctbSize;
  private readonly int _minBlocksPerCtbSide;

  // The state one slice segment is decoded with. Fields rather than parameters because every level
  // of the coding tree recursion would otherwise carry all of them.
  private H265SliceHeader _header = null!;
  private H265CabacEngine _cabac;
  private byte[] _contexts = null!;
  private byte[] _wavefrontContexts = null!;
  private bool _wavefrontContextsStored;
  private int _sliceOrdinal;
  private int _sliceStartCtb;
  private IReadOnlyList<H265Picture>[] _referenceLists = [[], []];

  private int _previousQuantisationGroupQp;
  private int _currentQuantisationGroupIndex;
  private int _quantisationGroupPredictedQp;
  private int _currentCodingUnitQp;
  private bool _quantiserDeltaCoded;
  private int _quantiserDelta;
  private int _quantisationGroupX;
  private int _quantisationGroupY;

  // The state of the coding unit being parsed.
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

  /// <summary>How many coding tree blocks have been decoded, so that a missing slice can be noticed.</summary>
  private int _decodedCtbs;

  /// <summary>
  /// Decodes one slice segment's coding tree units.
  /// </summary>
  /// <param name="header">The segment's header, already parsed to the byte its data begins on.</param>
  /// <param name="referenceLists">The two reference picture lists this slice predicts from.</param>
  internal void DecodeSliceSegment(H265SliceHeader header, IReadOnlyList<H265Picture>[] referenceLists) {
    this._header = header;
    this._referenceLists = referenceLists;
    this._sliceOrdinal = this._sliceLoopFilterAcross.Count;
    this._sliceLoopFilterAcross.Add(header.LoopFilterAcrossSlicesEnabled);
    this._sliceDeblocking.Add((header.DeblockingFilterDisabled, header.BetaOffsetDiv2, header.TcOffsetDiv2));
    this._sliceStartCtb = header.SegmentAddress;

    this._contexts = new byte[H265CabacContexts.COUNT];
    this._wavefrontContexts = new byte[H265CabacContexts.COUNT];
    this._wavefrontContextsStored = false;

    this._cabac = new(header.Nal.Payload, this._contexts);

    var width = this._sps.PicWidthInCtbsY;
    var total = this._sps.PicSizeInCtbsY;
    var substream = 0;

    var initType = header.SliceType switch {
      H265SliceType.I => 0,
      H265SliceType.P => header.CabacInitFlag ? 2 : 1,
      _ => header.CabacInitFlag ? 1 : 2,
    };

    this._previousQuantisationGroupQp = header.SliceQpY;
    this._currentCodingUnitQp = header.SliceQpY;
    this._currentQuantisationGroupIndex = -1;

    for (var ctb = header.SegmentAddress; ctb < total; ++ctb) {
      var column = ctb % width;
      var row = ctb / width;

      var startsSubstream = ctb == header.SegmentAddress
                            || (this._pps.EntropyCodingSyncEnabled && column == 0);

      if (startsSubstream) {
        this._StartSubstream(header, initType, ctb, column, row, ref substream);

        // The first quantisation group of a slice segment, and of every row when the entropy coder
        // is synchronised across rows, predicts from the slice's own quantiser rather than from
        // wherever the previous group left off — a row that may be decoded in parallel cannot
        // depend on the row before it having finished.
        this._previousQuantisationGroupQp = header.SliceQpY;
        this._currentCodingUnitQp = header.SliceQpY;
        this._currentQuantisationGroupIndex = -1;
      }

      this._DecodeCodingTreeUnit(ctb, column, row);
      ++this._decodedCtbs;

      // The entropy coder hands its state to the row below after the second block of a row, which is
      // exactly as far as the row below's first block can already have looked when it starts.
      if (this._pps.EntropyCodingSyncEnabled && column == 1) {
        Array.Copy(this._contexts, this._wavefrontContexts, H265CabacContexts.COUNT);
        this._wavefrontContextsStored = true;
      }

      if (this._cabac.DecodeTerminate() != 0)
        return;
    }

    throw new InvalidDataException(
      $"An H.265 {header.SliceType} slice segment at picture order count {header.PicOrderCntLsb} ran past the last "
      + "coding tree block of the picture without its end_of_slice_segment_flag being set. The entropy decoder is "
      + "out of step with the bitstream.");
  }

  /// <summary>Whether every coding tree block of the picture has been decoded by some slice.</summary>
  internal void RefuseIfIncomplete() {
    if (this._decodedCtbs >= this._sps.PicSizeInCtbsY)
      return;

    throw new InvalidDataException(
      $"An H.265 picture was left {this._sps.PicSizeInCtbsY - this._decodedCtbs} of its "
      + $"{this._sps.PicSizeInCtbsY} coding tree blocks undecoded: its slices do not cover it. Handing back a "
      + "picture with holes in it would be handing back a picture that was never coded.");
  }

  /// <summary>
  /// Starts a new entropy-coded substream — clause 9.3.1.
  /// </summary>
  /// <remarks>
  /// A row of coding tree blocks is its own substream when the entropy coder is synchronised across
  /// rows, which is what lets a decoder work on several rows at once: each starts at a byte the slice
  /// header points at, with an arithmetic decoder of its own. What it does <em>not</em> start with is
  /// fresh statistics — it inherits the context states the row above had after its second block, so
  /// the coder is still adapting to the picture rather than relearning it eight times over.
  /// </remarks>
  private void _StartSubstream(
    H265SliceHeader header, int initType, int ctb, int column, int row, ref int substream) {
    var inheritFromRowAbove = false;

    if (this._pps.EntropyCodingSyncEnabled && column == 0 && row > 0) {
      // The block whose state would be inherited is the second of the row above; it has to exist,
      // be in this slice, and have been decoded.
      var above = ctb - this._sps.PicWidthInCtbsY + 1;
      inheritFromRowAbove = this._sps.PicWidthInCtbsY > 1
                            && above >= this._sliceStartCtb
                            && this._wavefrontContextsStored;
    }

    if (inheritFromRowAbove)
      Array.Copy(this._wavefrontContexts, this._contexts, H265CabacContexts.COUNT);
    else
      H265CabacContexts.Initialize(this._contexts, initType, header.SliceQpY);

    var offset = ctb == header.SegmentAddress
      ? header.DataOffset
      : header.SubstreamOffsets.Length > substream
        ? header.SubstreamOffsets[substream++]
        : throw new InvalidDataException(
          "An H.265 slice segment enables entropy coding synchronisation but states fewer entry point offsets than "
          + "it has rows of coding tree blocks. Its substreams cannot be located.");

    this._cabac.Start(offset);
  }

  private void _DecodeCodingTreeUnit(int ctb, int column, int row) {
    var x = column << this._log2CtbSize;
    var y = row << this._log2CtbSize;

    if (this._header.SaoLuma || this._header.SaoChroma)
      this._DecodeSampleAdaptiveOffset(ctb, column, row);

    this._DecodeCodingQuadtree(x, y, this._log2CtbSize, 0);
  }

  // ================================================================================================
  // Sample adaptive offset parameters — clause 7.3.8.3
  // ================================================================================================

  private void _DecodeSampleAdaptiveOffset(int ctb, int column, int row) {
    var mergeLeft = false;
    var mergeUp = false;

    if (column > 0 && ctb - 1 >= this._sliceStartCtb)
      mergeLeft = this._cabac.DecodeBin(H265CabacContexts.SAO_MERGE) != 0;

    if (!mergeLeft && row > 0 && ctb - this._sps.PicWidthInCtbsY >= this._sliceStartCtb)
      mergeUp = this._cabac.DecodeBin(H265CabacContexts.SAO_MERGE) != 0;

    if (mergeLeft || mergeUp) {
      var source = mergeLeft ? ctb - 1 : ctb - this._sps.PicWidthInCtbsY;
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

      // Cb and Cr share one type and one edge class; only Cb states them.
      if (component < 2) {
        var type = 0;
        if (this._cabac.DecodeBin(H265CabacContexts.SAO_TYPE_IDX) != 0)
          type = this._cabac.DecodeBypass() != 0 ? 2 : 1;

        this._saoTypeIdx[slot] = (byte)type;
      } else
        this._saoTypeIdx[slot] = this._saoTypeIdx[ctb * 3 + 1];

      if (this._saoTypeIdx[slot] == 0)
        continue;

      // The magnitudes are bounded by the sample depth: at eight bits an offset may not exceed
      // seven, which is as far as a band or an edge correction ever needs to reach.
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
        // Band offset: four consecutive bands are corrected, each with its own sign.
        for (var i = 0; i < 4; ++i)
          if (magnitudes[i] != 0 && this._cabac.DecodeBypass() != 0)
            magnitudes[i] = -magnitudes[i];

        this._saoBandOrClass[slot] = (byte)this._cabac.DecodeBypassBits(5);
      } else {
        // Edge offset: the sign is fixed by which of the four shapes a sample turned out to be. A
        // sample at a local minimum is raised and one at a local maximum is lowered, always.
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

  // ================================================================================================
  // The coding quadtree — clause 7.3.8.4
  // ================================================================================================

  private void _DecodeCodingQuadtree(int x0, int y0, int log2CbSize, int depth) {
    var size = 1 << log2CbSize;

    var split = log2CbSize > this._sps.MinCbLog2SizeY;
    if (x0 + size <= this._sps.Width && y0 + size <= this._sps.Height && log2CbSize > this._sps.MinCbLog2SizeY)
      split = this._cabac.DecodeBin(
        H265CabacContexts.SPLIT_CU_FLAG + this._SplitContext(x0, y0, depth)) != 0;

    // A quantisation group opens here when this node is at or above the size the picture parameter
    // set chose. Every coding unit inside it shares one quantiser, whether or not any of them codes
    // the delta that sets it — which is why the quantiser is kept per group rather than per coding
    // unit: the group's value is only known once one of its units has coded the delta, and it then
    // applies to the ones before it as well.
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

  /// <summary>
  /// The context for <c>split_cu_flag</c> — clause 9.3.4.2.2: whether the neighbours went deeper.
  /// </summary>
  private int _SplitContext(int x0, int y0, int depth) {
    var context = 0;

    if (this._IsAvailable(x0, y0, x0 - 1, y0) && this._codingTreeDepth[this._BlockIndex(x0 - 1, y0)] > depth)
      ++context;

    if (this._IsAvailable(x0, y0, x0, y0 - 1) && this._codingTreeDepth[this._BlockIndex(x0, y0 - 1)] > depth)
      ++context;

    return context;
  }

  /// <summary>
  /// Opens a quantisation group at a node of the coding quadtree — clauses 7.3.8.4 and 8.6.1.
  /// </summary>
  /// <remarks>
  /// A node of the quadtree may reopen a group its own parent already opened, because both are at or
  /// above the group size. Only a move to a different group ends the previous one — treating the
  /// second visit as a new group would take the quantiser the next group predicts from out of the
  /// middle of the current one rather than from the end of the last.
  /// </remarks>
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

  /// <summary>Which group of the picture-wide grid a position falls in.</summary>
  private int _QuantisationGroupIndex(int x, int y)
    => ((y >> this._log2QuantisationGroupSize) << 16) | (x >> this._log2QuantisationGroupSize);

  /// <summary>
  /// The quantiser a group starts from — clause 8.6.1.
  /// </summary>
  /// <remarks>
  /// The average of the group to the left and the group above, each falling back to whatever the
  /// previous group in decoding order ended on when it is outside the coding tree block. Confining
  /// the two neighbours to the current coding tree block is what makes the prediction independent of
  /// how the picture was cut into slices, at the cost of a slightly worse prediction at each block's
  /// top and left edge.
  /// </remarks>
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

  /// <summary>
  /// Fixes the quantiser of the coding unit being decoded — clause 8.6.1.
  /// </summary>
  /// <remarks>
  /// <b>Per coding unit, not per quantisation group,</b> and that distinction is the whole of this
  /// method. A group's delta is transmitted in the first of its coding units that carries a
  /// coefficient, which may not be the first of its coding units at all — and the units decoded
  /// before it took their quantiser as the prediction alone, because that is what the delta was at
  /// the time. Those units have no residual to dequantise, so the difference never shows in their own
  /// samples; it shows in the <em>next</em> group, which predicts from whichever of them happens to
  /// sit above or to the left of it, and in the deblocking filter, which takes its thresholds from
  /// the quantiser on each side of an edge.
  /// </remarks>
  private void _SetQuantiser(int delta) {
    this._quantiserDelta = delta;
    this._currentCodingUnitQp = this._WrapQuantiser(this._quantisationGroupPredictedQp + delta);
    this._RecordCodingUnitQuantiser();
  }

  /// <summary>
  /// Keeps a quantiser inside the range the sample depth gives it, wrapping rather than clamping.
  /// </summary>
  /// <remarks>
  /// The wrap is the standard's, and it is what lets a delta reach the far end of the range in one
  /// step from either end of it — a coding unit at quantiser 51 can be taken to 0 by a delta of 1.
  /// </remarks>
  private int _WrapQuantiser(int qp) {
    var offset = this._sps.QpBdOffsetLuma;
    return ((qp + 52 + 2 * offset) % (52 + offset)) - offset;
  }

  private void _RecordCodingUnitQuantiser()
    => this._FillBlocks(
      this._blockQp, this._cuX, this._cuY, 1 << this._cuLog2Size, 1 << this._cuLog2Size,
      (sbyte)this._currentCodingUnitQp);

  // ================================================================================================
  // The coding unit — clause 7.3.8.5
  // ================================================================================================

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

    // The quantiser this unit uses, from its group's prediction and whatever delta has been read so
    // far — which is zero for every unit of a group that precedes the one carrying it.
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

  /// <summary>Whether the whole coding unit took its motion from a neighbour without a residual flag.</summary>
  private bool _cuMergedWholeUnit;

  /// <summary>The context for <c>cu_skip_flag</c> — clause 9.3.4.2.2.</summary>
  private int _SkipContext(int x0, int y0) {
    var context = 0;

    if (this._IsAvailable(x0, y0, x0 - 1, y0) && this._skipped[this._BlockIndex(x0 - 1, y0)])
      ++context;

    if (this._IsAvailable(x0, y0, x0, y0 - 1) && this._skipped[this._BlockIndex(x0, y0 - 1)])
      ++context;

    return context;
  }

  /// <summary>Reads <c>part_mode</c> — Table 9-34 and clause 9.3.4.2.</summary>
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

      // The last bin says which end of the block the quarter is at, and it is bypassed: an
      // asymmetric split is as likely to be one way round as the other.
      var second = this._cabac.DecodeBypass() != 0;
      return horizontal
        ? second ? H265PartitionMode.HorizontalQuarterBottom : H265PartitionMode.HorizontalQuarterTop
        : second ? H265PartitionMode.VerticalQuarterRight : H265PartitionMode.VerticalQuarterLeft;
    }

    if (horizontal)
      return H265PartitionMode.HorizontalHalves;

    // A coding block already at the smallest size may quarter itself, but not an 8x8 one: four 4x4
    // inter prediction blocks would each need their own motion, which the standard forbids for the
    // memory bandwidth it would cost.
    if (log2CbSize == 3)
      return H265PartitionMode.VerticalHalves;

    return this._cabac.DecodeBin(H265CabacContexts.PART_MODE + 2) != 0
      ? H265PartitionMode.VerticalHalves
      : H265PartitionMode.Quarters;
  }

  // ================================================================================================
  // Intra coding units — clauses 7.3.8.5 and 8.4
  // ================================================================================================

  /// <returns>Whether the coding unit was fully handled, which is the case for raw sample blocks.</returns>
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

    // Both loops run over all the prediction blocks before either reads a mode, because the flags
    // for every block precede the values for every block.
    var useMostProbable = new bool[4];
    for (var i = 0; i < parts * parts; ++i)
      useMostProbable[i] = this._cabac.DecodeBin(H265CabacContexts.PREV_INTRA_LUMA_PRED_FLAG) != 0;

    var modes = new int[4];
    for (var i = 0; i < parts * parts; ++i) {
      var x = x0 + (i % parts) * step;
      var y = y0 + (i / parts) * step;

      var candidates = this._MostProbableModes(x, y);

      if (useMostProbable[i]) {
        // Two bypassed bins, truncated: the first of three candidates costs one bin and the other
        // two cost two, because the first is much the likeliest.
        var index = this._cabac.DecodeBypass();
        if (index != 0)
          index += this._cabac.DecodeBypass();

        modes[i] = candidates[index];
      } else {
        var remaining = this._cabac.DecodeBypassBits(5);

        // The three candidates are removed from the numbering rather than coded around, so the
        // remaining thirty-two modes fit exactly in five bits.
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

  /// <summary>
  /// The three modes a luma block may name in one or two bins — clause 8.4.2.
  /// </summary>
  /// <remarks>
  /// Two of them come from the neighbours, on the reasoning that a block's texture usually runs the
  /// same way as the texture beside it. The third fills whichever of planar, direct current and
  /// vertical the first two leave out — and when both neighbours agree on a direction, the other two
  /// are that direction's neighbours, which is what makes the list useful for a texture that curves.
  /// <para/>
  /// A neighbour above the current coding tree block counts as unavailable even when it is decoded.
  /// That is deliberate: reading it would mean keeping a row of modes for the whole picture width
  /// rather than for one block, and the standard chose the memory.
  /// </remarks>
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

  /// <summary>
  /// The chroma prediction mode — Table 8-2 and Table 8-3.
  /// </summary>
  /// <remarks>
  /// Four directions plus "the same as luma", and the four are the ones worth having: planar, direct
  /// current, horizontal and vertical. Where one of the four is what luma already chose, the mode
  /// means the 45-degree diagonal instead — so no bin string is ever spent saying the same thing
  /// twice.
  /// </remarks>
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

  /// <summary>
  /// Reads a coding unit whose samples were sent raw — clause 7.3.8.7.
  /// </summary>
  /// <remarks>
  /// The escape hatch: a block the transform would code worse than sending it outright, which happens
  /// for noise and for synthetic images with hard edges. The samples are read straight out of the
  /// bitstream at whatever depth the sequence parameter set chose, byte alignment first, and the
  /// arithmetic coder is restarted afterwards because raw bits cannot be read through it.
  /// </remarks>
  private void _DecodePulseCodeModulatedBlock(int x0, int y0, int log2CbSize) {
    throw new NotSupportedException(
      "This H.265 stream carries a coding unit whose samples were sent uncompressed (pcm_flag, clause 7.3.8.7). "
      + "Reading one means leaving the arithmetic decoder, taking the samples as raw bits at the sequence's own "
      + $"depth and restarting the decoder afterwards; that is not implemented. The block is at ({x0}, {y0}), "
      + $"{1 << log2CbSize} samples across.");
  }

  // ================================================================================================
  // Inter coding units — clauses 7.3.8.6 and 8.5
  // ================================================================================================

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

  /// <summary>
  /// Reads one prediction unit's motion and predicts its samples — clauses 7.3.8.6 and 8.5.3.
  /// </summary>
  /// <returns>Whether the unit took its motion whole from a neighbour.</returns>
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

    // Only the first candidate's index is worth a context; the rest are as likely as each other and
    // are read as a truncated unary string of bypassed bins.
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
      // Direction 0 is list zero alone, 1 is list one alone, 2 is both.
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

  /// <summary>
  /// Keeps a motion vector inside the sixteen bits clause 7.4.9.9 gives it.
  /// </summary>
  /// <remarks>
  /// The sum wraps rather than saturating, and the standard says so in as many words: the vector is
  /// defined modulo sixteen bits. An encoder may legitimately code a difference whose sum with the
  /// predictor runs past the end, and a decoder that clamped instead would point somewhere else.
  /// </remarks>
  private static int _WrapMotionVector(int value) => (short)value;

  /// <summary>
  /// Which reference lists a prediction block uses — Table 9-36 and clause 9.3.4.2.
  /// </summary>
  /// <remarks>
  /// A block of eight samples' area may not use both lists. Two references for so few samples costs
  /// more memory bandwidth than the bits it saves, so the standard removes the choice and with it the
  /// bin that would have stated it.
  /// </remarks>
  /// <returns>0 for the first list alone, 1 for the second alone, 2 for both.</returns>
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

    // Past the second index the value is a truncated unary string of bypassed bins.
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

  /// <summary>An exponential-Golomb code of the given order, entirely in bypassed bins — clause 9.3.3.3.</summary>
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

  // ================================================================================================
  // The transform tree — clauses 7.3.8.8 and 7.3.8.10
  // ================================================================================================

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

    // A 4x4 luma transform block has no chroma block of its own: the four that share an 8x8 parent
    // share one 4x4 chroma block each way, read with the last of them.
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

  /// <summary>Reads <c>cu_qp_delta_abs</c> and its sign — clauses 7.3.8.10 and 9.3.3.10.</summary>
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

  // ================================================================================================
  // Reconstruction
  // ================================================================================================

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
      this._picture.Luma, this._picture.Width, x0, y0, log2Size, hasResidual, transformSkip, qp, 0,
      this._sps.BitDepthLuma, mode);
  }

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
      this._picture.Luma, this._picture.Width, x0, y0, log2Size, transformSkip, qp, 0,
      this._sps.BitDepthLuma, false);
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

  /// <summary>The chroma quantiser a luma one implies — clause 8.6.1.</summary>
  private int _ChromaQuantiser(int lumaQp, int component) {
    var offset = component == 1
      ? this._pps.CbQpOffset + this._header.SliceCbQpOffset
      : this._pps.CrQpOffset + this._header.SliceCrQpOffset;

    var index = Math.Clamp(lumaQp + offset, -this._sps.QpBdOffsetChroma, 57);
    return H265Dequantiser.ChromaQp(index) + this._sps.QpBdOffsetChroma;
  }

  private void _Reconstruct(
    byte[] plane, int stride, int x0, int y0, int log2Size, bool hasResidual, bool transformSkip,
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

        plane[row + x] = (byte)Math.Clamp(value, 0, maximum);
      }
    }
  }

  private void _AddResidual(
    byte[] plane, int stride, int x0, int y0, int log2Size, bool transformSkip, int qp, int component,
    int bitDepth, bool intra) {
    var size = 1 << log2Size;
    var maximum = (1 << bitDepth) - 1;

    this._TransformResidual(log2Size, transformSkip, qp, component, bitDepth, intra ? 0 : -1);

    for (var y = 0; y < size; ++y) {
      var row = (y0 + y) * stride + x0;
      for (var x = 0; x < size; ++x)
        plane[row + x] = (byte)Math.Clamp(plane[row + x] + this._coefficients[(y << log2Size) + x], 0, maximum);
    }
  }

  /// <summary>Dequantises and inverse-transforms the block sitting in the coefficient buffer.</summary>
  private void _TransformResidual(
    int log2Size, bool transformSkip, int qp, int component, int bitDepth, int intraMode) {
    if (this._cuTransquantBypass)
      // Lossless: the levels are the residual. No quantiser was applied, so none is undone.
      return;

    var scalingList = this._sps.ScalingListEnabled
      ? this._pps.ScalingList ?? this._sps.ScalingList
      : null;

    // A skipped transform of more than four samples across is never weighted, because the matrices
    // weight by frequency and a skipped block has no frequencies.
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

  // ================================================================================================
  // Reference samples for intra prediction — clause 8.4.4.2
  // ================================================================================================

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

  /// <summary>
  /// The reference samples for a chroma block, which are never smoothed in this chroma format.
  /// </summary>
  /// <remarks>
  /// Clause 8.4.4.2.1 invokes the filtering only for luma, or for chroma coded at full resolution.
  /// At 4:2:0 the chroma planes are already half the size, which is the same low-pass the filter
  /// would apply — doing it twice would blur a colour edge the encoder chose a direction for.
  /// </remarks>
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
    byte[] plane, int stride, int width, int height, int x0, int y0, int x, int y, int slot) {
    if (x < 0 || y < 0 || x >= width || y >= height)
      return false;

    if (!this._IsPredictable(x0, y0, x, y))
      return false;

    this._reference[slot] = plane[y * stride + x];
    return true;
  }

  private bool _TakeReferenceChroma(
    byte[] plane, int stride, int width, int height, int x0, int y0, int x, int y, int slot) {
    if (x < 0 || y < 0 || x >= width || y >= height)
      return false;

    if (!this._IsPredictable(x0 << 1, y0 << 1, x << 1, y << 1))
      return false;

    this._reference[slot] = plane[y * stride + x];
    return true;
  }

  /// <summary>Whether a neighbouring sample may be predicted from — clause 6.4.1 plus constrained intra.</summary>
  private bool _IsPredictable(int x0, int y0, int x, int y) {
    if (!this._IsAvailable(x0, y0, x, y))
      return false;

    if (!this._pps.ConstrainedIntraPred)
      return true;

    // With constrained intra prediction an intra block may only predict from intra neighbours, so
    // that a picture whose predicted blocks were lost still decodes its intra ones exactly.
    return this._predictionMode[this._BlockIndex(x, y)] == (byte)H265PredictionMode.Intra;
  }

  // ================================================================================================
  // Availability and the per-block state
  // ================================================================================================

  /// <summary>
  /// Whether a block is decoded and in the same slice — clause 6.4.1.
  /// </summary>
  /// <remarks>
  /// Decided by comparing the two blocks' z-scan addresses rather than by any record of what has
  /// been written. The z-scan is the order the picture is decoded in, so a lower address means
  /// already decoded, and the comparison gives the right answer for a chroma block whose position
  /// maps back to a luma block finished several transform units ago.
  /// </remarks>
  internal bool _IsAvailable(int x0, int y0, int x, int y) {
    if (x < 0 || y < 0 || x >= this._sps.Width || y >= this._sps.Height)
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

  /// <summary>
  /// Where a position falls in the picture's z-scan — clause 6.5.2.
  /// </summary>
  /// <remarks>
  /// Coding tree blocks in raster order, and within one, the interleaved bits of the position — which
  /// is the quadtree's own traversal written as a number, because descending a quadtree in the order
  /// top-left, top-right, bottom-left, bottom-right is exactly taking one bit of each coordinate in
  /// turn from the top.
  /// </remarks>
  private long _ZScanAddress(int x, int y) {
    var ctb = (long)(y >> this._log2CtbSize) * this._sps.PicWidthInCtbsY + (x >> this._log2CtbSize);

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

  // ================================================================================================
  // What the loop filters and the motion prediction need to see
  // ================================================================================================

  internal int BlocksAcross => this._blocksAcross;

  internal int BlocksDown => this._blocksDown;

  internal H265SliceHeader Header => this._header;

  internal IReadOnlyList<H265Picture> ReferenceList(int list) => this._referenceLists[list];

  internal int BlockIndexAt(int x, int y) => this._BlockIndex(x, y);

  /// <summary>The coding block being decoded, which the prediction block availability rules refer to.</summary>
  internal int CodingBlockX => this._cuX;

  internal int CodingBlockY => this._cuY;

  internal int CodingBlockSize => 1 << this._cuLog2Size;

  internal H265PartitionMode CodingBlockPartitionMode => this._cuPartitionMode;

  /// <summary>The motion of one smallest block of the picture being decoded.</summary>
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

  /// <summary>
  /// The picture a slice borrows motion from, or <c>null</c> where it borrows none.
  /// </summary>
  /// <remarks>
  /// Named by the slice header rather than chosen here, and it has to be: every slice of a picture
  /// must name the same one, because the motion a block borrows would otherwise depend on which slice
  /// the block happened to fall in.
  /// </remarks>
  internal H265Picture? CollocatedPicture {
    get {
      if (!this._header.TemporalMvpEnabled)
        return null;

      var list = this._referenceLists[this._header.CollocatedFromL0 ? 0 : 1];
      return this._header.CollocatedRefIdx < list.Count ? list[this._header.CollocatedRefIdx] : null;
    }
  }

  internal bool IsAvailableAt(int x0, int y0, int x, int y) => this._IsAvailable(x0, y0, x, y);

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

  /// <summary>
  /// The deblocking offsets each slice chose, so that an edge is filtered with its own slice's.
  /// </summary>
  /// <remarks>
  /// Kept per slice rather than taken from whichever header was parsed last. A picture cut into
  /// slices may set different offsets in each, and the filter runs after all of them have been
  /// decoded — so by the time an edge is reached, "the current header" is the last slice's and means
  /// nothing to an edge in the first.
  /// </remarks>
  internal (bool Disabled, int Beta, int Tc) DeblockingParameters(int slice)
    => slice >= 0 && slice < this._sliceDeblocking.Count
      ? this._sliceDeblocking[slice]
      : (true, 0, 0);
}
