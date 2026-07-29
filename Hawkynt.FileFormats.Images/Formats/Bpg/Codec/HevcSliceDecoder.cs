using System;

namespace FileFormat.Bpg.Codec;

/// <summary>HEVC I-slice decoder: CTU parsing, recursive CU/PU/TU quad-tree splitting, residual decoding.</summary>
internal sealed class HevcSliceDecoder {

  private readonly HevcNalParser.Sps _sps;
  private readonly HevcNalParser.Pps _pps;
  private readonly HevcNalParser.SliceHeader _sliceHeader;
  private readonly int _bitDepthY;
  private readonly int _bitDepthC;
  private readonly int _chromaFormatIdc;

  // Frame buffers (sample planes)
  private readonly int[] _yPlane;
  private readonly int[] _cbPlane;
  private readonly int[] _crPlane;
  private readonly int _yStride;
  private readonly int _cStride;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;

  // CABAC decoder
  private HevcCabacDecoder? _cabac;

  // Derived values
  private readonly int _ctbSizeY;
  private readonly int _minCbSizeY;
  private readonly int _minTbSizeY;
  private readonly int _maxTbSizeY;
  private readonly int _maxTrDepthIntra;
  private readonly int _qp;

  // SAO parameters per CTU
  private readonly HevcSaoFilter.SaoParams[,,] _saoParams; // [ctuX, ctuY, component]

  public HevcSliceDecoder(HevcNalParser.Sps sps, HevcNalParser.Pps pps, HevcNalParser.SliceHeader sliceHeader) {
    _sps = sps;
    _pps = pps;
    _sliceHeader = sliceHeader;

    _bitDepthY = sps.BitDepthLumaMinus8 + 8;
    _bitDepthC = sps.BitDepthChromaMinus8 + 8;
    _chromaFormatIdc = sps.ChromaFormatIdc;

    _ctbSizeY = sps.CtbSizeY;
    _minCbSizeY = sps.MinCbSizeY;
    _minTbSizeY = 1 << (sps.Log2MinTransformBlockSizeMinus2 + 2);
    _maxTbSizeY = _minTbSizeY << sps.Log2DiffMaxMinTransformBlockSize;
    _maxTrDepthIntra = sps.MaxTransformHierarchyDepthIntra;
    _qp = sliceHeader.SliceQp;

    var width = sps.PicWidthInLumaSamples;
    var height = sps.PicHeightInLumaSamples;

    _yStride = width;
    _yPlane = new int[width * height];

    // Chroma dimensions based on format
    int chromaSubX, chromaSubY;
    switch (_chromaFormatIdc) {
      case 1: // 4:2:0
        chromaSubX = 2;
        chromaSubY = 2;
        break;
      case 2: // 4:2:2
        chromaSubX = 2;
        chromaSubY = 1;
        break;
      case 3: // 4:4:4
        chromaSubX = 1;
        chromaSubY = 1;
        break;
      default: // Monochrome
        chromaSubX = 1;
        chromaSubY = 1;
        break;
    }

    _chromaWidth = (width + chromaSubX - 1) / chromaSubX;
    _chromaHeight = (height + chromaSubY - 1) / chromaSubY;
    _cStride = _chromaWidth;

    if (_chromaFormatIdc > 0) {
      _cbPlane = new int[_chromaWidth * _chromaHeight];
      _crPlane = new int[_chromaWidth * _chromaHeight];
    } else {
      _cbPlane = [];
      _crPlane = [];
    }

    _saoParams = new HevcSaoFilter.SaoParams[sps.PicWidthInCtbs, sps.PicHeightInCtbs, 3];
    for (var cx = 0; cx < sps.PicWidthInCtbs; ++cx)
      for (var cy = 0; cy < sps.PicHeightInCtbs; ++cy)
        for (var comp = 0; comp < 3; ++comp)
          _saoParams[cx, cy, comp] = new() { Type = HevcSaoFilter.SaoType.None };
  }

  /// <summary>Decodes the I-slice and returns the decoded planes.</summary>
  /// <returns>Tuple of (yPlane, cbPlane, crPlane, yStride, cStride).</returns>
  public (int[] yPlane, int[] cbPlane, int[] crPlane, int yStride, int cStride) Decode(byte[] sliceData) {
    // Initialize CABAC from the point after the slice header
    _cabac = new HevcCabacDecoder(sliceData, _sliceHeader.CabacInitByteOffset);
    _cabac.InitContextModels(_qp);

    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    // Decode CTUs in raster scan order
    for (var ctuY = 0; ctuY < _sps.PicHeightInCtbs; ++ctuY)
      for (var ctuX = 0; ctuX < _sps.PicWidthInCtbs; ++ctuX) {
        var x0 = ctuX * _ctbSizeY;
        var y0 = ctuY * _ctbSizeY;

        // Parse SAO parameters for this CTU
        if (_sps.SaoEnabled)
          _ParseSaoParams(ctuX, ctuY);

        // Decode the coding tree unit
        _DecodeCtu(x0, y0, _ctbSizeY);

        // Check for end_of_slice_segment_flag
        if (_cabac!.DecodeTerminate() != 0)
          break;
      }

    // Apply in-loop filters
    _ApplyInLoopFilters();

    return (_yPlane, _cbPlane, _crPlane, _yStride, _cStride);
  }

  private void _ParseSaoParams(int ctuX, int ctuY) {
    // SAO merge flags
    var mergeLeft = ctuX > 0 && _cabac!.DecodeBin(143) != 0; // sao_merge_left_flag
    if (mergeLeft) {
      for (var comp = 0; comp < (_chromaFormatIdc > 0 ? 3 : 1); ++comp)
        _saoParams[ctuX, ctuY, comp] = _saoParams[ctuX - 1, ctuY, comp];
      return;
    }

    var mergeUp = ctuY > 0 && _cabac!.DecodeBin(143) != 0; // sao_merge_up_flag
    if (mergeUp) {
      for (var comp = 0; comp < (_chromaFormatIdc > 0 ? 3 : 1); ++comp)
        _saoParams[ctuX, ctuY, comp] = _saoParams[ctuX, ctuY - 1, comp];
      return;
    }

    // Parse individual SAO parameters per component
    for (var comp = 0; comp < (_chromaFormatIdc > 0 ? 3 : 1); ++comp) {
      if ((comp == 0 && !_sliceHeader.SaoLumaFlag) || (comp > 0 && !_sliceHeader.SaoChromaFlag)) {
        _saoParams[ctuX, ctuY, comp] = new() { Type = HevcSaoFilter.SaoType.None };
        continue;
      }

      var saoTypeIdx = _cabac!.DecodeBin(144); // sao_type_idx_luma/chroma
      if (saoTypeIdx == 0) {
        _saoParams[ctuX, ctuY, comp] = new() { Type = HevcSaoFilter.SaoType.None };
        continue;
      }

      // Determine actual SAO type (band or edge)
      var isBandOffset = _cabac!.DecodeBypass() != 0;
      var offsets = new int[4];

      for (var i = 0; i < 4; ++i) {
        var absOffset = _cabac!.DecodeUnaryMaxBypass(31);
        if (absOffset > 0 && (isBandOffset || i < 2)) {
          var sign = _cabac!.DecodeBypass();
          offsets[i] = sign != 0 ? -absOffset : absOffset;
        } else {
          offsets[i] = isBandOffset ? absOffset : (i < 2 ? absOffset : -absOffset);
        }
      }

      var bandPosition = 0;
      var edgeClass = HevcSaoFilter.SaoEdgeClass.Horizontal;

      if (isBandOffset) {
        bandPosition = 0;
        for (var i = 4; i >= 0; --i)
          bandPosition = (bandPosition << 1) | _cabac!.DecodeBypass();
      } else {
        var edgeDir = (_cabac!.DecodeBypass() << 1) | _cabac!.DecodeBypass();
        edgeClass = (HevcSaoFilter.SaoEdgeClass)edgeDir;
      }

      _saoParams[ctuX, ctuY, comp] = new() {
        Type = isBandOffset ? HevcSaoFilter.SaoType.BandOffset : HevcSaoFilter.SaoType.EdgeOffset,
        Offsets = offsets,
        BandPosition = bandPosition,
        EdgeClass = edgeClass,
      };
    }
  }

  private void _DecodeCtu(int x0, int y0, int size) {
    // Start recursive CU decoding (coding_quadtree)
    _DecodeCodingQuadtree(x0, y0, size, 0);
  }

  private void _DecodeCodingQuadtree(int x0, int y0, int size, int depth) {
    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    // Out of bounds: skip
    if (x0 >= width || y0 >= height)
      return;

    // Determine if split is possible/required
    var canSplit = size > _minCbSizeY;
    var mustSplit = x0 + size > width || y0 + size > height;
    var split = mustSplit;

    if (canSplit && !mustSplit) {
      // Decode split_cu_flag
      var ctxIdx = Math.Min(depth, 2); // Context 0..2 for split_cu_flag
      split = _cabac!.DecodeBin(ctxIdx) != 0;
    }

    if (split) {
      var halfSize = size >> 1;
      _DecodeCodingQuadtree(x0, y0, halfSize, depth + 1);
      _DecodeCodingQuadtree(x0 + halfSize, y0, halfSize, depth + 1);
      _DecodeCodingQuadtree(x0, y0 + halfSize, halfSize, depth + 1);
      _DecodeCodingQuadtree(x0 + halfSize, y0 + halfSize, halfSize, depth + 1);
    } else {
      _DecodeCodingUnit(x0, y0, size, depth);
    }
  }

  private void _DecodeCodingUnit(int x0, int y0, int size, int depth) {
    // For I-slice, all CUs are intra-coded
    // In BPG I-only mode: pred_mode = INTRA, part_mode = 2Nx2N (or NxN for min CU)

    // Decode part_mode (for I-slice: 0 = 2Nx2N, 1 = NxN)
    var partMode = 0; // Default: 2Nx2N
    if (size == _minCbSizeY) {
      // NxN partitioning is only possible at minimum CU size
      var partModeFlag = _cabac!.DecodeBin(132); // part_mode context
      partMode = partModeFlag != 0 ? 0 : 1; // 0 = 2Nx2N, 1 = NxN
    }

    if (partMode == 0) {
      // 2Nx2N: one prediction unit covers the entire CU
      var intraMode = _DecodeIntraPredMode();
      _DecodePredictionUnit(x0, y0, size, intraMode);
    } else {
      // NxN: four prediction units
      var halfSize = size >> 1;
      for (var py = 0; py < 2; ++py)
        for (var px = 0; px < 2; ++px) {
          var puX = x0 + px * halfSize;
          var puY = y0 + py * halfSize;
          var intraMode = _DecodeIntraPredMode();
          _DecodePredictionUnit(puX, puY, halfSize, intraMode);
        }
    }

    // Decode transform tree
    _DecodeTransformTree(x0, y0, size, 0);
  }

  private int _DecodeIntraPredMode() {
    // prev_intra_luma_pred_flag
    var prevIntraLumaPredFlag = _cabac!.DecodeBin(136) != 0;

    if (prevIntraLumaPredFlag) {
      // Use most probable mode (MPM)
      // Decode mpm_idx (truncated unary, max 2)
      var mpmIdx = 0;
      if (_cabac!.DecodeBypass() != 0) {
        ++mpmIdx;
        if (_cabac!.DecodeBypass() != 0)
          ++mpmIdx;
      }
      // For simplicity (no neighbor tracking), use default MPMs: [Planar, DC, Angular26]
      return mpmIdx switch {
        0 => HevcIntraPredictor.Planar,
        1 => HevcIntraPredictor.Dc,
        _ => 26, // Vertical
      };
    }

    // rem_intra_luma_pred_mode (5-bit fixed-length bypass)
    var remMode = 0;
    for (var i = 4; i >= 0; --i)
      remMode |= _cabac!.DecodeBypass() << i;

    // Map remaining mode to actual mode (skip MPMs)
    // Default MPMs are {0, 1, 26}; remaining modes fill gaps
    if (remMode >= 0)
      ++remMode; // Skip Planar
    if (remMode >= 1)
      ++remMode; // Skip DC
    if (remMode >= 26)
      ++remMode; // Skip Angular26

    return Math.Clamp(remMode, 0, 34);
  }

  private void _DecodePredictionUnit(int x0, int y0, int size, int intraMode) {
    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    // Effective block dimensions (clamp to frame boundary)
    var blockW = Math.Min(size, width - x0);
    var blockH = Math.Min(size, height - y0);

    if (blockW <= 0 || blockH <= 0)
      return;

    // Build reference samples and perform intra prediction on luma
    var refSize = 2 * size + 1;
    var refAbove = new int[refSize];
    var refLeft = new int[refSize];

    HevcIntraPredictor.BuildReferenceArrays(refAbove, refLeft, _yPlane, _yStride, x0, y0, size, width, height, _bitDepthY);

    var predBlock = new int[size * size];
    HevcIntraPredictor.Predict(intraMode, predBlock, refAbove, refLeft, size, _bitDepthY);

    // Write predicted samples to luma plane
    for (var py = 0; py < blockH; ++py)
      for (var px = 0; px < blockW; ++px)
        _yPlane[(y0 + py) * _yStride + (x0 + px)] = predBlock[py * size + px];

    // Chroma prediction
    if (_chromaFormatIdc > 0) {
      var chromaMode = _MapChromaMode(intraMode);
      _PredictChroma(x0, y0, size, chromaMode);
    }
  }

  private void _PredictChroma(int x0, int y0, int lumaSize, int chromaMode) {
    int chromaSubX, chromaSubY;
    switch (_chromaFormatIdc) {
      case 1: chromaSubX = 2; chromaSubY = 2; break;
      case 2: chromaSubX = 2; chromaSubY = 1; break;
      default: chromaSubX = 1; chromaSubY = 1; break;
    }

    var chromaX = x0 / chromaSubX;
    var chromaY = y0 / chromaSubY;
    var chromaSize = Math.Max(lumaSize / chromaSubX, 4);

    if (chromaSize < 4)
      chromaSize = 4;

    var blockW = Math.Min(chromaSize, _chromaWidth - chromaX);
    var blockH = Math.Min(chromaSize, _chromaHeight - chromaY);

    if (blockW <= 0 || blockH <= 0)
      return;

    var refSize = 2 * chromaSize + 1;

    // Cb prediction
    var refAbove = new int[refSize];
    var refLeft = new int[refSize];

    HevcIntraPredictor.BuildReferenceArrays(refAbove, refLeft, _cbPlane, _cStride, chromaX, chromaY, chromaSize, _chromaWidth, _chromaHeight, _bitDepthC);
    var predBlock = new int[chromaSize * chromaSize];
    HevcIntraPredictor.Predict(chromaMode, predBlock, refAbove, refLeft, chromaSize, _bitDepthC);

    for (var py = 0; py < blockH; ++py)
      for (var px = 0; px < blockW; ++px)
        _cbPlane[(chromaY + py) * _cStride + (chromaX + px)] = predBlock[py * chromaSize + px];

    // Cr prediction
    HevcIntraPredictor.BuildReferenceArrays(refAbove, refLeft, _crPlane, _cStride, chromaX, chromaY, chromaSize, _chromaWidth, _chromaHeight, _bitDepthC);
    predBlock = new int[chromaSize * chromaSize];
    HevcIntraPredictor.Predict(chromaMode, predBlock, refAbove, refLeft, chromaSize, _bitDepthC);

    for (var py = 0; py < blockH; ++py)
      for (var px = 0; px < blockW; ++px)
        _crPlane[(chromaY + py) * _cStride + (chromaX + px)] = predBlock[py * chromaSize + px];
  }

  private void _DecodeTransformTree(int x0, int y0, int size, int trDepth) {
    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    if (x0 >= width || y0 >= height)
      return;

    var minTbSize = _minTbSizeY;
    var maxTbSize = _maxTbSizeY;

    // Determine if transform split is possible
    var canSplit = size > minTbSize && trDepth < _maxTrDepthIntra;
    var mustSplit = size > maxTbSize;
    var split = mustSplit;

    if (canSplit && !mustSplit) {
      // Decode split_transform_flag
      var ctxIdx = 3 + Math.Min(trDepth, 2); // Context 3..5
      split = _cabac!.DecodeBin(ctxIdx) != 0;
    }

    if (split) {
      var halfSize = size >> 1;
      if (halfSize < 4)
        return;

      _DecodeTransformTree(x0, y0, halfSize, trDepth + 1);
      _DecodeTransformTree(x0 + halfSize, y0, halfSize, trDepth + 1);
      _DecodeTransformTree(x0, y0 + halfSize, halfSize, trDepth + 1);
      _DecodeTransformTree(x0 + halfSize, y0 + halfSize, halfSize, trDepth + 1);
    } else {
      _DecodeTransformUnit(x0, y0, size, trDepth);
    }
  }

  private void _DecodeTransformUnit(int x0, int y0, int size, int trDepth) {
    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    // Decode CBF (coded block flag) for luma
    var cbfLuma = false;
    {
      var ctxIdx = 6 + Math.Min(trDepth, 1); // cbf_luma context
      cbfLuma = _cabac!.DecodeBin(ctxIdx) != 0;
    }

    // Decode CBF for chroma (if applicable)
    var cbfCb = false;
    var cbfCr = false;
    if (_chromaFormatIdc > 0 && size > 4) {
      cbfCb = _cabac!.DecodeBin(8 + Math.Min(trDepth, 1)) != 0; // cbf_cb context
      cbfCr = _cabac!.DecodeBin(10 + Math.Min(trDepth, 1)) != 0; // cbf_cr context
    }

    // Decode and add luma residual
    if (cbfLuma)
      _DecodeResidualBlock(x0, y0, size, 0, _qp);

    // Decode and add chroma residuals
    if (_chromaFormatIdc > 0) {
      int chromaSubX, chromaSubY;
      switch (_chromaFormatIdc) {
        case 1: chromaSubX = 2; chromaSubY = 2; break;
        case 2: chromaSubX = 2; chromaSubY = 1; break;
        default: chromaSubX = 1; chromaSubY = 1; break;
      }

      var chromaSize = Math.Max(size / chromaSubX, 4);
      var chromaX = x0 / chromaSubX;
      var chromaY = y0 / chromaSubY;
      var chromaQp = HevcDequantizer.LumaToChromaQp(_qp + _pps.CbQpOffset);

      if (cbfCb)
        _DecodeResidualBlock(chromaX, chromaY, chromaSize, 1, chromaQp);

      chromaQp = HevcDequantizer.LumaToChromaQp(_qp + _pps.CrQpOffset);
      if (cbfCr)
        _DecodeResidualBlock(chromaX, chromaY, chromaSize, 2, chromaQp);
    }
  }

  private void _DecodeResidualBlock(int x0, int y0, int size, int component, int qp) {
    var plane = component switch {
      0 => _yPlane,
      1 => _cbPlane,
      2 => _crPlane,
      _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
    var stride = component == 0 ? _yStride : _cStride;
    var bitDepth = component == 0 ? _bitDepthY : _bitDepthC;
    var planeWidth = component == 0 ? _sps.PicWidthInLumaSamples : _chromaWidth;
    var planeHeight = component == 0 ? _sps.PicHeightInLumaSamples : _chromaHeight;
    var maxVal = (1 << bitDepth) - 1;

    // Decode residual coding
    var coeffs = _DecodeResidualCoding(size, component);

    // Inverse quantization
    HevcDequantizer.Dequantize(coeffs, size, qp, bitDepth);

    // Inverse transform
    var isDst = component == 0 && size == 4; // DST for 4x4 intra luma
    var residuals = new int[size * size];
    HevcTransform.InverseTransform(coeffs, residuals, size, isDst, bitDepth);

    // Add residuals to prediction (already in plane)
    var blockW = Math.Min(size, planeWidth - x0);
    var blockH = Math.Min(size, planeHeight - y0);

    for (var py = 0; py < blockH; ++py)
      for (var px = 0; px < blockW; ++px) {
        var idx = (y0 + py) * stride + (x0 + px);
        if (idx >= 0 && idx < plane.Length)
          plane[idx] = Math.Clamp(plane[idx] + residuals[py * size + px], 0, maxVal);
      }
  }

  private int[] _DecodeResidualCoding(int size, int component) {
    var numCoeffs = size * size;
    var coeffs = new int[numCoeffs];

    // Decode last significant coefficient position
    var lastSigCoeffX = _DecodeLastSigCoeffPrefix(size, false, component);
    var lastSigCoeffY = _DecodeLastSigCoeffPrefix(size, true, component);

    if (lastSigCoeffX >= size)
      lastSigCoeffX = size - 1;
    if (lastSigCoeffY >= size)
      lastSigCoeffY = size - 1;

    // Scan order (diagonal for intra)
    var scanOrder = _GetDiagonalScanOrder(size);

    // Find the last scan position
    var lastScanPos = -1;
    for (var i = numCoeffs - 1; i >= 0; --i) {
      var (sx, sy) = scanOrder[i];
      if (sx == lastSigCoeffX && sy == lastSigCoeffY) {
        lastScanPos = i;
        break;
      }
    }

    if (lastScanPos < 0)
      lastScanPos = 0;

    // Process sub-blocks (4x4 groups) in reverse scan order
    var subBlockSize = 4;
    var numSubBlocksPerRow = size / subBlockSize;

    // Decode significance flags and coefficient levels
    var sigFlags = new bool[numCoeffs];
    sigFlags[scanOrder[lastScanPos].x * size + scanOrder[lastScanPos].y] = true; // Last position is always significant

    // Simplified coefficient decoding: decode all positions from last to first
    for (var i = lastScanPos - 1; i >= 0; --i) {
      var ctxIdx = 54 + Math.Min(i % 16, 15); // sig_coeff_flag context
      sigFlags[i] = _cabac!.DecodeBin(ctxIdx) != 0;
    }

    // Decode coefficient levels for significant positions
    for (var i = lastScanPos; i >= 0; --i) {
      if (!sigFlags[i])
        continue;

      var (sx, sy) = scanOrder[i];

      // coeff_abs_level_greater1_flag
      var greater1 = _cabac!.DecodeBin(98) != 0; // coeff_abs_level_greater1 context

      var absLevel = 1;
      if (greater1) {
        // coeff_abs_level_greater2_flag
        var greater2 = _cabac!.DecodeBin(122) != 0; // coeff_abs_level_greater2 context
        absLevel = 2;
        if (greater2) {
          // Remaining level (bypass Exp-Golomb)
          var remaining = _DecodeCoeffAbsLevelRemaining(0);
          absLevel = (int)(3 + remaining);
        }
      }

      // Sign flag (bypass)
      var sign = _cabac!.DecodeBypass();
      coeffs[sy * size + sx] = sign != 0 ? -absLevel : absLevel;
    }

    return coeffs;
  }

  private int _DecodeLastSigCoeffPrefix(int size, bool isY, int component) {
    // Context offset for last_sig_coeff prefix
    var ctxBase = isY ? 32 : 14;
    if (component > 0)
      ctxBase += 3; // Chroma offset

    var maxPrefix = (size <= 4) ? 3 : (size <= 8) ? 5 : (size <= 16) ? 7 : 9;
    var prefix = 0;

    while (prefix < maxPrefix) {
      var ctxIdx = ctxBase + Math.Min(prefix, 3);
      if (_cabac!.DecodeBin(ctxIdx) == 0)
        break;
      ++prefix;
    }

    // Decode suffix for large positions
    if (prefix >= 4) {
      var numSuffixBits = (prefix - 2) >> 1;
      var suffix = 0;
      for (var i = numSuffixBits - 1; i >= 0; --i)
        suffix |= _cabac!.DecodeBypass() << i;

      return ((2 + (prefix & 1)) << numSuffixBits) + suffix;
    }

    return prefix;
  }

  private uint _DecodeCoeffAbsLevelRemaining(int riceParam) {
    // Truncated Rice + Exp-Golomb bypass coding
    var prefix = 0;
    while (prefix < 31 && _cabac!.DecodeBypass() != 0)
      ++prefix;

    if (prefix < 3) {
      var value = (uint)(prefix << riceParam);
      for (var i = riceParam - 1; i >= 0; --i)
        value |= (uint)(_cabac!.DecodeBypass() << i);
      return value;
    }

    // Exp-Golomb suffix
    var suffixLen = prefix - 3 + riceParam + 1;
    var suffix = 0u;
    for (var i = suffixLen - 1; i >= 0; --i)
      suffix |= (uint)(_cabac!.DecodeBypass() << i);

    return suffix + (((1u << (prefix - 3)) + 2) << riceParam);
  }

  private static (int x, int y)[] _GetDiagonalScanOrder(int size) {
    var count = size * size;
    var scan = new (int x, int y)[count];
    var idx = 0;

    // Diagonal scan pattern
    for (var d = 0; d < 2 * size - 1 && idx < count; ++d)
      for (var y = Math.Min(d, size - 1); y >= 0 && d - y < size && idx < count; --y) {
        var x = d - y;
        scan[idx++] = (x, y);
      }

    return scan;
  }

  private int _MapChromaMode(int lumaMode) =>
    // Simplified chroma mode mapping: use the same mode as luma (DM mode)
    // In a full implementation, intra_chroma_pred_mode would be parsed
    lumaMode;

  private void _ApplyInLoopFilters() {
    var width = _sps.PicWidthInLumaSamples;
    var height = _sps.PicHeightInLumaSamples;

    // Deblocking filter
    if (!_sliceHeader.DeblockingFilterDisabled) {
      HevcDeblockFilter.Apply(_yPlane, _yStride, width, height, _qp, _bitDepthY, _minCbSizeY);

      if (_chromaFormatIdc > 0) {
        var chromaQp = HevcDequantizer.LumaToChromaQp(_qp);
        HevcDeblockFilter.Apply(_cbPlane, _cStride, _chromaWidth, _chromaHeight, chromaQp, _bitDepthC, Math.Max(_minCbSizeY / 2, 4));
        HevcDeblockFilter.Apply(_crPlane, _cStride, _chromaWidth, _chromaHeight, chromaQp, _bitDepthC, Math.Max(_minCbSizeY / 2, 4));
      }
    }

    // SAO filter
    if (_sps.SaoEnabled) {
      for (var ctuY = 0; ctuY < _sps.PicHeightInCtbs; ++ctuY)
        for (var ctuX = 0; ctuX < _sps.PicWidthInCtbs; ++ctuX) {
          var x = ctuX * _ctbSizeY;
          var y = ctuY * _ctbSizeY;

          // Luma SAO
          HevcSaoFilter.ApplyCtu(_yPlane, _yStride, x, y, _ctbSizeY, width, height, _saoParams[ctuX, ctuY, 0], _bitDepthY);

          // Chroma SAO
          if (_chromaFormatIdc > 0) {
            int chromaSubX, chromaSubY;
            switch (_chromaFormatIdc) {
              case 1: chromaSubX = 2; chromaSubY = 2; break;
              case 2: chromaSubX = 2; chromaSubY = 1; break;
              default: chromaSubX = 1; chromaSubY = 1; break;
            }

            var cx = x / chromaSubX;
            var cy = y / chromaSubY;
            var chromaCtuSize = _ctbSizeY / chromaSubX;

            HevcSaoFilter.ApplyCtu(_cbPlane, _cStride, cx, cy, chromaCtuSize, _chromaWidth, _chromaHeight, _saoParams[ctuX, ctuY, 1], _bitDepthC);
            HevcSaoFilter.ApplyCtu(_crPlane, _cStride, cx, cy, chromaCtuSize, _chromaWidth, _chromaHeight, _saoParams[ctuX, ctuY, 2], _bitDepthC);
          }
        }
    }
  }
}
