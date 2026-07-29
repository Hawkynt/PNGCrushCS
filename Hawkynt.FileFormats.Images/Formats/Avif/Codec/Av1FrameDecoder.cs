using System;
using System.Buffers.Binary;

namespace FileFormat.Avif.Codec;

/// <summary>Top-level AV1 still frame decode pipeline for AVIF images.
/// Orchestrates: OBU parsing -> sequence header -> frame header -> tile decode -> loop filter -> CDEF -> LR -> YUV to RGB.</summary>
internal static class Av1FrameDecoder {

  /// <summary>Decodes AV1 OBU data from an AVIF mdat box into RGB24 pixel data.</summary>
  /// <returns>Tuple of (width, height, rgbPixelData).</returns>
  public static (int Width, int Height, byte[] RgbData) Decode(byte[] av1Data, int offset, int length) {
    ArgumentNullException.ThrowIfNull(av1Data);
    if (length == 0)
      throw new InvalidOperationException("AV1: empty bitstream data.");

    // Step 1: Parse all OBUs
    var obus = Av1ObuParser.ParseObus(av1Data, offset, length);

    Av1SequenceHeader? seqHeader = null;
    Av1FrameHeader? frameHeader = null;
    byte[]? tileData = null;
    var tileDataOffset = 0;
    var tileDataLength = 0;

    foreach (var obu in obus) {
      switch (obu.Type) {
        case Av1ObuType.SequenceHeader:
          seqHeader = Av1SequenceHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize);
          break;

        case Av1ObuType.Frame:
          // Frame OBU contains both frame header and tile group
          if (seqHeader == null)
            throw new InvalidOperationException("AV1: frame OBU before sequence header.");

          frameHeader = Av1FrameHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize, seqHeader);
          tileData = av1Data;
          tileDataOffset = obu.PayloadOffset + frameHeader.TileDataOffset;
          tileDataLength = obu.PayloadSize - frameHeader.TileDataOffset;
          break;

        case Av1ObuType.FrameHeader:
          if (seqHeader == null)
            throw new InvalidOperationException("AV1: frame header OBU before sequence header.");

          frameHeader = Av1FrameHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize, seqHeader);
          break;

        case Av1ObuType.TileGroup:
          tileData = av1Data;
          tileDataOffset = obu.PayloadOffset;
          tileDataLength = obu.PayloadSize;
          break;
      }
    }

    if (seqHeader == null)
      throw new InvalidOperationException("AV1: no sequence header found in bitstream.");

    if (frameHeader == null)
      throw new InvalidOperationException("AV1: no frame header found in bitstream.");

    // Step 2: Allocate frame buffers (YCbCr planes)
    var width = frameHeader.FrameWidth;
    var height = frameHeader.FrameHeight;

    var numPlanes = seqHeader.NumPlanes;
    var planes = new short[numPlanes][];
    var planeWidths = new int[numPlanes];
    var planeHeights = new int[numPlanes];
    var planeStrides = new int[numPlanes];

    for (var p = 0; p < numPlanes; ++p) {
      var subX = p > 0 ? seqHeader.SubsamplingX : 0;
      var subY = p > 0 ? seqHeader.SubsamplingY : 0;
      planeWidths[p] = (width + subX) >> subX;
      planeHeights[p] = (height + subY) >> subY;
      planeStrides[p] = planeWidths[p];
      planes[p] = new short[planeStrides[p] * planeHeights[p]];

      // Initialize to mid-gray
      var mid = (short)(1 << (seqHeader.BitDepth - 1));
      Array.Fill(planes[p], mid);
    }

    // Step 3: Decode tiles
    if (tileData != null && tileDataLength > 0)
      _DecodeTiles(tileData, tileDataOffset, tileDataLength, seqHeader, frameHeader, planes, planeWidths, planeHeights, planeStrides);

    // Step 4: Apply loop filter
    if (frameHeader.LoopFilterLevel[0] != 0 || frameHeader.LoopFilterLevel[1] != 0)
      Av1LoopFilter.ApplyDeblocking(planes, planeWidths, planeHeights, planeStrides, frameHeader, seqHeader);

    // Step 5: Apply CDEF
    if (seqHeader.EnableCdef)
      Av1LoopFilter.ApplyCdef(planes, planeWidths, planeHeights, planeStrides, frameHeader, seqHeader);

    // Step 6: Apply Loop Restoration
    if (seqHeader.EnableRestoration)
      Av1LoopFilter.ApplyLoopRestoration(planes, planeWidths, planeHeights, planeStrides, frameHeader, seqHeader);

    // Step 7: Convert YCbCr to RGB
    var rgbData = _ConvertToRgb(planes, planeWidths, planeHeights, planeStrides, width, height, seqHeader);

    return (width, height, rgbData);
  }

  private static void _DecodeTiles(
    byte[] data, int offset, int length,
    Av1SequenceHeader seq, Av1FrameHeader fh,
    short[][] planes, int[] planeWidths, int[] planeHeights, int[] planeStrides
  ) {
    var tileDecoder = new Av1TileDecoder(seq, fh, planes, planeWidths, planeHeights, planeStrides);

    var numTiles = fh.TileRows * fh.TileCols;
    if (numTiles == 1) {
      // Single tile: use all remaining data
      tileDecoder.DecodeTile(data, offset, length, 0, 0);
      return;
    }

    // Multiple tiles: each tile has a size prefix
    var currentOffset = offset;
    var remaining = length;

    for (var tileIdx = 0; tileIdx < numTiles; ++tileIdx) {
      var tileRow = tileIdx / fh.TileCols;
      var tileCol = tileIdx % fh.TileCols;

      int tileSize;
      if (tileIdx < numTiles - 1) {
        // Read tile size (tileSizeBytes bytes, little-endian)
        if (remaining < fh.TileSizeBytes)
          break;

        tileSize = 0;
        for (var i = 0; i < fh.TileSizeBytes; ++i)
          tileSize |= data[currentOffset + i] << (i * 8);
        tileSize += 1; // tile_size_minus_1

        currentOffset += fh.TileSizeBytes;
        remaining -= fh.TileSizeBytes;
      } else {
        tileSize = remaining;
      }

      if (tileSize > remaining)
        tileSize = remaining;

      tileDecoder.DecodeTile(data, currentOffset, tileSize, tileCol, tileRow);

      currentOffset += tileSize;
      remaining -= tileSize;
    }
  }

  private static byte[] _ConvertToRgb(
    short[][] planes, int[] planeWidths, int[] planeHeights, int[] planeStrides,
    int width, int height, Av1SequenceHeader seq
  ) {
    if (seq.MonoChrome)
      return Av1YuvToRgb.ConvertMonoToRgb(planes[0], planeStrides[0], width, height, seq.BitDepth, seq.ColorRange);

    if (seq.MatrixCoefficients == Av1MatrixCoefficients.Identity)
      return Av1YuvToRgb.ConvertIdentityToRgb(
        planes[0], planeStrides[0],
        planes[1], planeStrides[1],
        planes[2], planeStrides[2],
        width, height, seq.BitDepth);

    if (seq.SubsamplingX != 0 && seq.SubsamplingY != 0)
      return Av1YuvToRgb.ConvertYuv420ToRgb(
        planes[0], planeStrides[0],
        planes[1], planeStrides[1],
        planes[2], planeStrides[2],
        width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);

    if (seq.SubsamplingX == 0 && seq.SubsamplingY == 0)
      return Av1YuvToRgb.ConvertYuv444ToRgb(
        planes[0], planeStrides[0],
        planes[1], planeStrides[1],
        planes[2], planeStrides[2],
        width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);

    // 4:2:2 fallback: treat as 4:4:4 (upsample U/V horizontally)
    var upsampledU = _UpsampleHorizontal(planes[1], planeStrides[1], planeWidths[1], planeHeights[1], width);
    var upsampledV = _UpsampleHorizontal(planes[2], planeStrides[2], planeWidths[2], planeHeights[2], width);

    return Av1YuvToRgb.ConvertYuv444ToRgb(
      planes[0], planeStrides[0],
      upsampledU, width,
      upsampledV, width,
      width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);
  }

  private static short[] _UpsampleHorizontal(short[] src, int srcStride, int srcW, int srcH, int dstW) {
    var dst = new short[dstW * srcH];
    for (var y = 0; y < srcH; ++y) {
      for (var x = 0; x < dstW; ++x) {
        var srcX = x >> 1;
        if (srcX >= srcW)
          srcX = srcW - 1;
        dst[y * dstW + x] = src[y * srcStride + srcX];
      }
    }
    return dst;
  }
}
