using System;

namespace FileFormat.Heif.Codec;

/// <summary>HEVC CTU (Coding Tree Unit) quad-tree partition and prediction unit decoder for I-frames.
/// Recursively splits CTUs into CUs, determines intra prediction mode, decodes coefficients,
/// applies inverse transform, and writes reconstructed samples to the frame buffer.</summary>
internal sealed class HeifSliceDecoder {

  private readonly HevcSps _sps;
  private readonly HevcPps _pps;
  private readonly short[][] _planes;
  private readonly int[] _planeWidths;
  private readonly int[] _planeHeights;
  private readonly int[] _planeStrides;
  private readonly int _picWidthInCtbs;
  private readonly int _picHeightInCtbs;

  public HeifSliceDecoder(
    HevcSps sps, HevcPps pps,
    short[][] planes, int[] planeWidths, int[] planeHeights, int[] planeStrides
  ) {
    _sps = sps;
    _pps = pps;
    _planes = planes;
    _planeWidths = planeWidths;
    _planeHeights = planeHeights;
    _planeStrides = planeStrides;
    _picWidthInCtbs = (sps.PicWidthInLumaSamples + sps.CtbSize - 1) / sps.CtbSize;
    _picHeightInCtbs = (sps.PicHeightInLumaSamples + sps.CtbSize - 1) / sps.CtbSize;
  }

  /// <summary>Decodes a slice from CABAC-encoded data.</summary>
  public void DecodeSlice(byte[] data, int offset, int length) {
    var cabac = new HeifCabacDecoder(data, offset, length);

    for (var ctbY = 0; ctbY < _picHeightInCtbs; ++ctbY) {
      for (var ctbX = 0; ctbX < _picWidthInCtbs; ++ctbX) {
        var x = ctbX * _sps.CtbSize;
        var y = ctbY * _sps.CtbSize;
        _DecodeCodingTree(cabac, x, y, _sps.CtbSizeLog2);

        if (cabac.IsAtEnd)
          return;
      }
    }
  }

  private void _DecodeCodingTree(HeifCabacDecoder cabac, int x, int y, int log2Size) {
    var size = 1 << log2Size;

    // Check if this CU is within picture bounds
    if (x >= _sps.PicWidthInLumaSamples || y >= _sps.PicHeightInLumaSamples)
      return;

    // Check if we can split further
    var minCuLog2 = _sps.MinCbSizeLog2;
    var canSplit = log2Size > minCuLog2;

    var split = false;
    if (canSplit && !cabac.IsAtEnd) {
      // Decode split_cu_flag
      split = cabac.DecodeBin(0) != 0;
    }

    if (split) {
      var halfSize = size >> 1;
      var subLog2 = log2Size - 1;
      _DecodeCodingTree(cabac, x, y, subLog2);
      _DecodeCodingTree(cabac, x + halfSize, y, subLog2);
      _DecodeCodingTree(cabac, x, y + halfSize, subLog2);
      _DecodeCodingTree(cabac, x + halfSize, y + halfSize, subLog2);
    } else {
      _DecodeCodingUnit(cabac, x, y, size);
    }
  }

  private void _DecodeCodingUnit(HeifCabacDecoder cabac, int x, int y, int cuSize) {
    // For I-frame: always intra prediction
    // Decode luma intra mode
    var lumaMode = _DecodeIntraMode(cabac);

    // Decode chroma intra mode (typically derived from luma or explicitly coded)
    var chromaMode = lumaMode; // simplified: same as luma

    // Decode each plane
    for (var plane = 0; plane < (_sps.ChromaFormatIdc > 0 ? 3 : 1); ++plane) {
      var subX = plane > 0 ? (_sps.ChromaFormatIdc == 1 ? 1 : 0) : 0;
      var subY = plane > 0 ? (_sps.ChromaFormatIdc == 1 ? 1 : 0) : 0;
      var pw = cuSize >> subX;
      var ph = cuSize >> subY;
      var px = x >> subX;
      var py = y >> subY;

      if (pw < 4) pw = 4;
      if (ph < 4) ph = 4;

      var planeW = _planeWidths[plane];
      var planeH = _planeHeights[plane];
      var stride = _planeStrides[plane];

      if (px >= planeW || py >= planeH)
        continue;

      var actualW = Math.Min(pw, planeW - px);
      var actualH = Math.Min(ph, planeH - py);

      // Build reference samples
      var nTbS = Math.Min(actualW, actualH);
      var above = _GetAboveRef(plane, px, py, nTbS);
      var left = _GetLeftRef(plane, px, py, nTbS);
      var topLeft = _GetTopLeftSample(plane, px, py);

      // Intra prediction
      var mode = plane == 0 ? lumaMode : chromaMode;
      var pred = new short[actualW * actualH];
      HeifIntraPredictor.Predict(mode, nTbS, _sps.BitDepth, above, left, topLeft, pred, actualW);

      // Copy prediction to output
      for (var dy = 0; dy < actualH; ++dy)
        for (var dx = 0; dx < actualW; ++dx)
          _planes[plane][(py + dy) * stride + (px + dx)] = pred[dy * actualW + dx];

      // Decode transform coefficients
      var coeffs = _DecodeResidual(cabac, actualW, actualH);
      if (_HasNonZero(coeffs)) {
        var useDst = plane == 0 && nTbS == 4;
        HeifTransform.InverseTransform2D(coeffs, _planes[plane], py * stride + px, stride, nTbS, _sps.BitDepth, useDst);
      }
    }
  }

  private HevcIntraPredMode _DecodeIntraMode(HeifCabacDecoder cabac) {
    if (cabac.IsAtEnd)
      return HevcIntraPredMode.Dc;

    // Simplified intra mode decoding:
    // prev_intra_luma_pred_flag
    var prevFlag = cabac.DecodeBin(1);
    if (prevFlag != 0) {
      // Use most probable mode (MPM) - simplified to DC
      var mpmIdx = cabac.IsAtEnd ? 0 : cabac.DecodeBin(2);
      return mpmIdx switch {
        0 => HevcIntraPredMode.Planar,
        1 => HevcIntraPredMode.Dc,
        _ => HevcIntraPredMode.Angular26,
      };
    }

    // Read remaining mode index (5 bits via bypass)
    if (cabac.IsAtEnd)
      return HevcIntraPredMode.Dc;

    var remMode = (int)cabac.ReadBypassBits(5);
    return (HevcIntraPredMode)Math.Min(remMode, 34);
  }

  private int[] _DecodeResidual(HeifCabacDecoder cabac, int w, int h) {
    var coeffs = new int[w * h];
    if (cabac.IsAtEnd)
      return coeffs;

    // Simplified residual decoding:
    // cbf (coded block flag)
    var cbf = cabac.DecodeBin(3);
    if (cbf == 0)
      return coeffs;

    // Read coefficient levels (simplified)
    var maxCoeffs = Math.Min(w * h, 256);
    for (var i = 0; i < maxCoeffs && !cabac.IsAtEnd; ++i) {
      // sig_coeff_flag
      var sig = cabac.DecodeBin(4);
      if (sig == 0)
        continue;

      // coeff_abs_level
      var level = 1;
      var greater1 = cabac.DecodeBin(5);
      if (greater1 != 0) {
        level = 2;
        if (!cabac.IsAtEnd) {
          var greater2 = cabac.DecodeBin(6);
          if (greater2 != 0)
            level = 3 + (int)cabac.DecodeExpGolomb(0);
        }
      }

      // sign
      var sign = cabac.DecodeBypass();
      coeffs[i] = sign != 0 ? -level : level;
    }

    return coeffs;
  }

  private short[] _GetAboveRef(int plane, int px, int py, int nTbS) {
    var stride = _planeStrides[plane];
    var planeW = _planeWidths[plane];
    var count = 2 * nTbS + 1;
    var samples = new short[count];

    if (py == 0) {
      var mid = (short)(1 << (_sps.BitDepth - 1));
      Array.Fill(samples, mid);
      return samples;
    }

    // samples[0] = top-left
    samples[0] = _GetTopLeftSample(plane, px, py);
    // samples[1..nTbS] = above row
    for (var i = 0; i < 2 * nTbS && px + i < planeW; ++i)
      samples[i + 1] = _planes[plane][(py - 1) * stride + px + i];

    // Pad
    var lastValid = Math.Min(2 * nTbS, planeW - px);
    if (lastValid > 0) {
      var lastSample = samples[lastValid];
      for (var i = lastValid + 1; i < count; ++i)
        samples[i] = lastSample;
    }

    return samples;
  }

  private short[] _GetLeftRef(int plane, int px, int py, int nTbS) {
    var stride = _planeStrides[plane];
    var planeH = _planeHeights[plane];
    var count = 2 * nTbS + 1;
    var samples = new short[count];

    if (px == 0) {
      var mid = (short)(1 << (_sps.BitDepth - 1));
      Array.Fill(samples, mid);
      return samples;
    }

    samples[0] = _GetTopLeftSample(plane, px, py);
    for (var i = 0; i < 2 * nTbS && py + i < planeH; ++i)
      samples[i + 1] = _planes[plane][(py + i) * stride + px - 1];

    var lastValid = Math.Min(2 * nTbS, planeH - py);
    if (lastValid > 0) {
      var lastSample = samples[lastValid];
      for (var i = lastValid + 1; i < count; ++i)
        samples[i] = lastSample;
    }

    return samples;
  }

  private short _GetTopLeftSample(int plane, int px, int py) {
    if (px == 0 || py == 0)
      return (short)(1 << (_sps.BitDepth - 1));
    return _planes[plane][(py - 1) * _planeStrides[plane] + px - 1];
  }

  private static bool _HasNonZero(int[] coeffs) {
    foreach (var c in coeffs)
      if (c != 0)
        return true;
    return false;
  }
}
