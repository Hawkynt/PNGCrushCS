using System;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 partition types for superblock subdivision.</summary>
internal enum Av1PartitionType {
  None = 0,
  Horizontal = 1,
  Vertical = 2,
  Split = 3,
  HorizontalA = 4,
  HorizontalB = 5,
  VerticalA = 6,
  VerticalB = 7,
  Horizontal4 = 8,
  Vertical4 = 9,
}

/// <summary>Decodes AV1 tiles for single key frames: partition tree traversal,
/// intra prediction block decode, coefficient decode, and inverse transform.</summary>
internal sealed class Av1TileDecoder {

  private readonly Av1SequenceHeader _seq;
  private readonly Av1FrameHeader _fh;
  private readonly short[][] _planes;
  private readonly int[] _planeWidths;
  private readonly int[] _planeHeights;
  private readonly int[] _planeStrides;
  private readonly int _miCols;
  private readonly int _miRows;
  private readonly int _sbSizeLog2;
  private readonly int _sbSize;

  public Av1TileDecoder(Av1SequenceHeader seq, Av1FrameHeader fh, short[][] planes, int[] planeWidths, int[] planeHeights, int[] planeStrides) {
    _seq = seq;
    _fh = fh;
    _planes = planes;
    _planeWidths = planeWidths;
    _planeHeights = planeHeights;
    _planeStrides = planeStrides;
    _miCols = (fh.FrameWidth + 3) / 4;
    _miRows = (fh.FrameHeight + 3) / 4;
    _sbSizeLog2 = seq.Use128x128Superblock ? 7 : 6;
    _sbSize = 1 << _sbSizeLog2;
  }

  /// <summary>Decodes a single tile from compressed data, placing pixels into the plane buffers.</summary>
  public void DecodeTile(byte[] data, int offset, int length, int tileCol, int tileRow) {
    var decoder = new Av1AnsDecoder(data, offset, length);
    var sbMiSize = _sbSize / 4; // superblock size in MI units (4-pixel units)

    var colStartSb = _fh.TileColStarts[tileCol];
    var colEndSb = _fh.TileColStarts[tileCol + 1];
    var rowStartSb = _fh.TileRowStarts[tileRow];
    var rowEndSb = _fh.TileRowStarts[tileRow + 1];

    for (var sbRow = rowStartSb; sbRow < rowEndSb; ++sbRow) {
      for (var sbCol = colStartSb; sbCol < colEndSb; ++sbCol) {
        var miRowStart = sbRow * sbMiSize;
        var miColStart = sbCol * sbMiSize;
        _DecodePartition(decoder, miRowStart, miColStart, _sbSizeLog2);
      }
    }
  }

  private void _DecodePartition(Av1AnsDecoder decoder, int miRow, int miCol, int bSizeLog2) {
    if (miRow >= _miRows || miCol >= _miCols)
      return;

    var bSize = 1 << bSizeLog2; // in pixels
    var bSizeMi = bSize / 4;    // in MI units

    if (bSizeLog2 <= 3) {
      // Minimum partition size (8x8 block = 2 MI units) - no further splitting
      _DecodeBlock(decoder, miRow, miCol, bSizeLog2);
      return;
    }

    // Determine partition type from entropy decoder
    var partition = _DecodePartitionType(decoder, miRow, miCol, bSizeLog2);

    var subSizeLog2 = bSizeLog2 - 1;
    var halfMi = bSizeMi / 2;

    switch (partition) {
      case Av1PartitionType.None:
        _DecodeBlock(decoder, miRow, miCol, bSizeLog2);
        break;

      case Av1PartitionType.Horizontal:
        _DecodeBlock(decoder, miRow, miCol, bSizeLog2, bSizeLog2, subSizeLog2);
        if (miRow + halfMi < _miRows)
          _DecodeBlock(decoder, miRow + halfMi, miCol, bSizeLog2, bSizeLog2, subSizeLog2);
        break;

      case Av1PartitionType.Vertical:
        _DecodeBlock(decoder, miRow, miCol, subSizeLog2, subSizeLog2, bSizeLog2);
        if (miCol + halfMi < _miCols)
          _DecodeBlock(decoder, miRow, miCol + halfMi, subSizeLog2, subSizeLog2, bSizeLog2);
        break;

      case Av1PartitionType.Split:
        _DecodePartition(decoder, miRow, miCol, subSizeLog2);
        _DecodePartition(decoder, miRow, miCol + halfMi, subSizeLog2);
        _DecodePartition(decoder, miRow + halfMi, miCol, subSizeLog2);
        _DecodePartition(decoder, miRow + halfMi, miCol + halfMi, subSizeLog2);
        break;

      default:
        // Complex partition types (H-A, H-B, V-A, V-B, H4, V4)
        // For still image decoding, fall back to split
        _DecodePartition(decoder, miRow, miCol, subSizeLog2);
        _DecodePartition(decoder, miRow, miCol + halfMi, subSizeLog2);
        _DecodePartition(decoder, miRow + halfMi, miCol, subSizeLog2);
        _DecodePartition(decoder, miRow + halfMi, miCol + halfMi, subSizeLog2);
        break;
    }
  }

  private Av1PartitionType _DecodePartitionType(Av1AnsDecoder decoder, int miRow, int miCol, int bSizeLog2) {
    // Simplified partition type decoding
    // In a proper implementation, this would use CDF tables from the AV1 spec
    var hasRows = miRow + (1 << (bSizeLog2 - 1)) / 4 < _miRows;
    var hasCols = miCol + (1 << (bSizeLog2 - 1)) / 4 < _miCols;

    if (!hasRows && !hasCols)
      return Av1PartitionType.Split;

    if (decoder.IsAtEnd)
      return Av1PartitionType.None;

    // Read partition decision from bitstream
    var bit = decoder.DecodeLiteral();
    if (bit == 0)
      return Av1PartitionType.None;

    if (!hasRows) {
      bit = decoder.DecodeLiteral();
      return bit == 0 ? Av1PartitionType.Horizontal : Av1PartitionType.Split;
    }

    if (!hasCols) {
      bit = decoder.DecodeLiteral();
      return bit == 0 ? Av1PartitionType.Vertical : Av1PartitionType.Split;
    }

    bit = decoder.DecodeLiteral();
    if (bit == 0)
      return Av1PartitionType.Horizontal;

    bit = decoder.DecodeLiteral();
    return bit == 0 ? Av1PartitionType.Vertical : Av1PartitionType.Split;
  }

  private void _DecodeBlock(Av1AnsDecoder decoder, int miRow, int miCol, int bSizeLog2) {
    _DecodeBlock(decoder, miRow, miCol, bSizeLog2, bSizeLog2, bSizeLog2);
  }

  private void _DecodeBlock(Av1AnsDecoder decoder, int miRow, int miCol, int bSizeLog2, int bwLog2, int bhLog2) {
    var bw = 1 << bwLog2; // block width in pixels
    var bh = 1 << bhLog2; // block height in pixels
    var pixelX = miCol * 4;
    var pixelY = miRow * 4;

    // Decode intra mode
    var mode = _DecodeIntraMode(decoder);
    var angleDelta = 0;
    if ((int)mode >= 1 && (int)mode <= 8) {
      // Directional modes can have angle deltas
      if (!decoder.IsAtEnd) {
        var hasDelta = decoder.DecodeLiteral();
        if (hasDelta != 0)
          angleDelta = decoder.DecodeLiteral() * 2 - 1; // simplified: -1, 0, or +1
      }
    }

    // Decode each plane
    for (var plane = 0; plane < _seq.NumPlanes; ++plane) {
      var subX = plane > 0 ? _seq.SubsamplingX : 0;
      var subY = plane > 0 ? _seq.SubsamplingY : 0;
      var pw = bw >> subX;
      var ph = bh >> subY;
      var px = pixelX >> subX;
      var py = pixelY >> subY;

      if (pw == 0) pw = 1;
      if (ph == 0) ph = 1;

      var planeW = _planeWidths[plane];
      var planeH = _planeHeights[plane];
      var stride = _planeStrides[plane];

      if (px >= planeW || py >= planeH)
        continue;

      var actualW = Math.Min(pw, planeW - px);
      var actualH = Math.Min(ph, planeH - py);

      // Get reference samples for intra prediction
      var above = _GetAboveRefSamples(plane, px, py, actualW, actualH);
      var left = _GetLeftRefSamples(plane, px, py, actualW, actualH);
      var topLeft = _GetTopLeftSample(plane, px, py);

      // Perform intra prediction
      var predMode = plane == 0 ? mode : _MapChromaMode(mode);
      var pred = new short[actualW * actualH];
      Av1IntraPredictor.Predict(predMode, angleDelta, actualW, actualH, _seq.BitDepth, above, left, topLeft, pred, actualW);

      // Decode transform coefficients and apply inverse transform
      var txSize = _GetTxSize(actualW, actualH);
      var txType = Av1TxType.DctDct; // Default transform type for key frames
      var coeffs = _DecodeCoefficients(decoder, actualW, actualH, plane);

      // Copy prediction to output, then add residual via inverse transform
      for (var y = 0; y < actualH; ++y)
        for (var x = 0; x < actualW; ++x)
          _planes[plane][(py + y) * stride + (px + x)] = pred[y * actualW + x];

      if (_HasNonZeroCoeffs(coeffs))
        Av1Transform.InverseTransform2D(coeffs, _planes[plane], py * stride + px, stride, txType, txSize, _seq.BitDepth);
    }
  }

  private Av1PredictionMode _DecodeIntraMode(Av1AnsDecoder decoder) {
    if (decoder.IsAtEnd)
      return Av1PredictionMode.DcPred;

    // Decode intra mode using entropy coder
    // Simplified: read a few bits to determine mode
    var modeBits = decoder.DecodeLiteralBits(4);
    return (Av1PredictionMode)Math.Min(modeBits, 12);
  }

  private static Av1PredictionMode _MapChromaMode(Av1PredictionMode lumaMode) {
    // For key frames, chroma typically uses the same mode as luma
    // CFL would be decoded separately
    return lumaMode;
  }

  private short[] _GetAboveRefSamples(int plane, int px, int py, int w, int h) {
    var stride = _planeStrides[plane];
    var planeW = _planeWidths[plane];
    var samples = new short[w + h + 1];

    if (py == 0) {
      // No above row available, use (1 << (bitDepth - 1))
      var mid = (short)(1 << (_seq.BitDepth - 1));
      Array.Fill(samples, mid);
      return samples;
    }

    for (var i = 0; i < w + h + 1 && px + i < planeW; ++i)
      samples[i] = _planes[plane][(py - 1) * stride + px + i];

    // Pad with last available sample
    if (px + w + h >= planeW) {
      var lastSample = samples[Math.Min(planeW - px - 1, w + h)];
      for (var i = Math.Max(0, planeW - px); i < w + h + 1; ++i)
        samples[i] = lastSample;
    }

    return samples;
  }

  private short[] _GetLeftRefSamples(int plane, int px, int py, int w, int h) {
    var stride = _planeStrides[plane];
    var planeH = _planeHeights[plane];
    var samples = new short[h + w + 1];

    if (px == 0) {
      var mid = (short)(1 << (_seq.BitDepth - 1));
      Array.Fill(samples, mid);
      return samples;
    }

    for (var i = 0; i < h + w + 1 && py + i < planeH; ++i)
      samples[i] = _planes[plane][(py + i) * stride + px - 1];

    // Pad with last available sample
    if (py + h + w >= planeH) {
      var lastSample = samples[Math.Min(planeH - py - 1, h + w)];
      for (var i = Math.Max(0, planeH - py); i < h + w + 1; ++i)
        samples[i] = lastSample;
    }

    return samples;
  }

  private short _GetTopLeftSample(int plane, int px, int py) {
    if (px == 0 || py == 0)
      return (short)(1 << (_seq.BitDepth - 1));

    return _planes[plane][(py - 1) * _planeStrides[plane] + px - 1];
  }

  private int[] _DecodeCoefficients(Av1AnsDecoder decoder, int w, int h, int plane) {
    var coeffs = new int[w * h];

    if (decoder.IsAtEnd)
      return coeffs;

    // Simplified coefficient decoding:
    // In a full implementation, this reads coefficient levels using CDF-based
    // entropy coding with context modeling. For now, decode a basic representation.
    var hasCoeffs = decoder.DecodeLiteral();
    if (hasCoeffs == 0)
      return coeffs;

    // Read end-of-block position (simplified)
    var maxCoeffs = Math.Min(w * h, 1024);
    var numCoeffs = Math.Min((int)decoder.DecodeLiteralBits(Math.Min(_CeilLog2(maxCoeffs + 1), 10)), maxCoeffs);

    for (var i = 0; i < numCoeffs && !decoder.IsAtEnd; ++i) {
      // Read coefficient level
      var sign = decoder.DecodeLiteral();
      var level = (int)decoder.DecodeLiteralBits(Math.Min(8, 16));
      coeffs[i] = sign != 0 ? -level : level;
    }

    return coeffs;
  }

  private static bool _HasNonZeroCoeffs(int[] coeffs) {
    foreach (var c in coeffs)
      if (c != 0)
        return true;
    return false;
  }

  private static Av1TxSize _GetTxSize(int w, int h) {
    if (w == h)
      return (w, h) switch {
        (4, 4) => Av1TxSize.Tx4x4,
        (8, 8) => Av1TxSize.Tx8x8,
        (16, 16) => Av1TxSize.Tx16x16,
        (32, 32) => Av1TxSize.Tx32x32,
        (64, 64) => Av1TxSize.Tx64x64,
        _ => Av1TxSize.Tx4x4,
      };

    return (w, h) switch {
      (4, 8) => Av1TxSize.Tx4x8,
      (8, 4) => Av1TxSize.Tx8x4,
      (8, 16) => Av1TxSize.Tx8x16,
      (16, 8) => Av1TxSize.Tx16x8,
      (16, 32) => Av1TxSize.Tx16x32,
      (32, 16) => Av1TxSize.Tx32x16,
      (32, 64) => Av1TxSize.Tx32x64,
      (64, 32) => Av1TxSize.Tx64x32,
      (4, 16) => Av1TxSize.Tx4x16,
      (16, 4) => Av1TxSize.Tx16x4,
      (8, 32) => Av1TxSize.Tx8x32,
      (32, 8) => Av1TxSize.Tx32x8,
      (16, 64) => Av1TxSize.Tx16x64,
      (64, 16) => Av1TxSize.Tx64x16,
      _ => Av1TxSize.Tx4x4,
    };
  }

  private static int _CeilLog2(int n) {
    var k = 0;
    while ((1 << k) < n)
      ++k;
    return k;
  }
}
