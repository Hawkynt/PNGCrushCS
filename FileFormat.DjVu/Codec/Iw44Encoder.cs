using System;
using System.IO;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// IW44 (InterWave) wavelet image encoder for DjVu.
/// Encodes RGB24 pixel data into progressive wavelet-encoded chunks (BG44 format).
///
/// Pipeline: RGB-to-YCbCr -> forward CDF 5/3 wavelet -> quantize -> ZP-encode bitplanes.
///
/// The encoder produces a single chunk containing a configurable number of
/// progressive quality slices. Each slice encodes one bitplane of coefficients,
/// starting from the most significant bit and working down.
/// </summary>
internal static class Iw44Encoder {

  private const int _BLOCK_SIZE = 32;
  private const int _DECOMPOSITION_LEVELS = Iw44Decoder.DecompositionLevels;

  /// <summary>
  /// Maximum bitplane (matches decoder). CDF 5/3 wavelet with 5 levels on 32x32 blocks
  /// can produce coefficients exceeding 2048 for edge/padding artifacts, so we use 14
  /// to cover up to 16383.
  /// </summary>
  private const int _MAX_BITPLANE = 14;

  /// <summary>
  /// Number of quality slices to encode. We encode all bitplanes from MAX_BITPLANE down to 0,
  /// giving the decoder enough data to reconstruct all coefficient magnitudes.
  /// </summary>
  private static int _SliceCount => _MAX_BITPLANE + 1;

  /// <summary>
  /// Encodes RGB24 pixel data into an IW44 compressed chunk.
  /// Returns raw chunk data suitable for embedding in a BG44 DjVu chunk.
  /// </summary>
  /// <param name="pixelData">RGB24 pixel data (3 bytes per pixel, row-major, top-to-bottom).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <returns>Compressed IW44 chunk data bytes.</returns>
  public static byte[] Encode(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    var coeffWidth = (width + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);
    var coeffHeight = (height + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);
    var chromaW = (coeffWidth + 1) / 2;
    var chromaH = (coeffHeight + 1) / 2;

    // Convert RGB to YCbCr
    var yCoeffs = new int[coeffWidth * coeffHeight];
    var cbCoeffs = new int[chromaW * chromaH];
    var crCoeffs = new int[chromaW * chromaH];
    _RgbToYCbCr(pixelData, yCoeffs, cbCoeffs, crCoeffs, width, height, coeffWidth, chromaW);

    // Pad with edge replication to avoid boundary artifacts in wavelet transform
    _PadEdge(yCoeffs, coeffWidth, coeffHeight, width, height);
    _PadEdge(cbCoeffs, chromaW, chromaH, (width + 1) / 2, (height + 1) / 2);
    _PadEdge(crCoeffs, chromaW, chromaH, (width + 1) / 2, (height + 1) / 2);

    // Forward wavelet transform
    Iw44Wavelet.Forward(yCoeffs, coeffWidth, coeffWidth, coeffHeight, _DECOMPOSITION_LEVELS);
    Iw44Wavelet.Forward(cbCoeffs, chromaW, chromaW, chromaH, _DECOMPOSITION_LEVELS - 1);
    Iw44Wavelet.Forward(crCoeffs, chromaW, chromaW, chromaH, _DECOMPOSITION_LEVELS - 1);

    // Convert to short for encoding
    var yShort = _IntToShort(yCoeffs);
    var cbShort = _IntToShort(cbCoeffs);
    var crShort = _IntToShort(crCoeffs);

    // Encode progressively using ZP coder
    return _EncodeSlices(yShort, cbShort, crShort, coeffWidth, coeffHeight, chromaW, chromaH);
  }

  /// <summary>Encodes a grayscale image (single channel).</summary>
  public static byte[] EncodeGrayscale(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    var coeffWidth = (width + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);
    var coeffHeight = (height + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);

    var yCoeffs = new int[coeffWidth * coeffHeight];
    for (var py = 0; py < height; ++py)
    for (var px = 0; px < width; ++px) {
      var srcIdx = py * width + px;
      if (srcIdx < pixelData.Length)
        yCoeffs[py * coeffWidth + px] = pixelData[srcIdx] - 128;
    }

    _PadEdge(yCoeffs, coeffWidth, coeffHeight, width, height);
    Iw44Wavelet.Forward(yCoeffs, coeffWidth, coeffWidth, coeffHeight, _DECOMPOSITION_LEVELS);

    var yShort = _IntToShort(yCoeffs);
    return _EncodeSlices(yShort, null, null, coeffWidth, coeffHeight, 0, 0);
  }

  /// <summary>Converts RGB24 pixels to YCbCr with 4:2:0 chroma subsampling.</summary>
  private static void _RgbToYCbCr(byte[] rgb, int[] y, int[] cb, int[] cr,
    int width, int height, int yStride, int chromaW) {

    // Initialize chroma accumulators
    var cbAcc = new int[cb.Length];
    var crAcc = new int[cb.Length];
    var counts = new int[cb.Length];

    for (var py = 0; py < height; ++py)
    for (var px = 0; px < width; ++px) {
      var idx = (py * width + px) * 3;
      if (idx + 2 >= rgb.Length)
        continue;

      int r = rgb[idx], g = rgb[idx + 1], b = rgb[idx + 2];

      // Y = 0.299R + 0.587G + 0.114B - 128 (centered at zero)
      y[py * yStride + px] = ((r * 19595 + g * 38470 + b * 7471 + 32768) >> 16) - 128;

      // Accumulate Cb/Cr for averaging over 2x2 blocks
      var cIdx = (py / 2) * chromaW + px / 2;
      if (cIdx < cb.Length) {
        cbAcc[cIdx] += (-r * 11056 - g * 21712 + b * 32768 + 32768) >> 16;
        crAcc[cIdx] += (r * 32768 - g * 27440 - b * 5328 + 32768) >> 16;
        ++counts[cIdx];
      }
    }

    // Average chroma values
    for (var i = 0; i < cb.Length; ++i) {
      if (counts[i] > 0) {
        cb[i] = cbAcc[i] / counts[i];
        cr[i] = crAcc[i] / counts[i];
      }
    }
  }

  /// <summary>Encodes wavelet coefficients progressively using the ZP coder.</summary>
  private static byte[] _EncodeSlices(short[] yCoeffs, short[]? cbCoeffs, short[]? crCoeffs,
    int coeffWidth, int coeffHeight, int chromaW, int chromaH) {

    var sliceCount = (byte)Math.Min(_SliceCount, 127);
    var bitCtx = new Iw44BitContext();

    using var ms = new MemoryStream();

    // Slice header byte: count in low 7 bits
    ms.WriteByte(sliceCount);

    // First slice has 2 extra header bytes (version info)
    ms.WriteByte(0x01); // version
    ms.WriteByte(0x00); // flags

    var encoder = new ZpEncoder();
    var currentBitplane = _MAX_BITPLANE;

    // Quantized coefficient arrays mirror what the decoder reconstructs
    var yQuant = new short[yCoeffs.Length];
    var cbQuant = cbCoeffs != null ? new short[cbCoeffs.Length] : null;
    var crQuant = crCoeffs != null ? new short[crCoeffs.Length] : null;

    for (var s = 0; s < sliceCount; ++s) {
      var bitplane = Math.Max(0, currentBitplane);

      // Encode luminance bitplane
      _EncodeBitplane(encoder, yCoeffs, yQuant, coeffWidth, coeffHeight, bitplane, bitCtx);

      // Encode chroma bitplanes
      if (cbCoeffs != null && crCoeffs != null) {
        _EncodeBitplane(encoder, cbCoeffs, cbQuant!, chromaW, chromaH, Math.Max(0, bitplane - 1), bitCtx);
        _EncodeBitplane(encoder, crCoeffs, crQuant!, chromaW, chromaH, Math.Max(0, bitplane - 1), bitCtx);
      }

      --currentBitplane;
    }

    var encodedData = encoder.Finish();
    ms.Write(encodedData);

    return ms.ToArray();
  }

  /// <summary>
  /// Encodes one bitplane of wavelet coefficients for a single channel.
  /// Mirrors the decoder's subband traversal order exactly.
  /// The encoder tracks quantized coefficients in parallel with what the decoder
  /// would reconstruct, ensuring significance/refinement decisions are consistent.
  /// </summary>
  private static void _EncodeBitplane(ZpEncoder encoder, short[] coeffs, short[] quantized,
    int width, int height, int bitplane, Iw44BitContext bitCtx) {

    var threshold = (short)(1 << bitplane);
    var maxLevel = Math.Min(_DECOMPOSITION_LEVELS, _MaxLevelFor(width, height));

    // Encode LL subband (coarsest approximation) first
    var llW = width >> maxLevel;
    var llH = height >> maxLevel;
    if (llW > 0 && llH > 0)
      _EncodeSubBand(encoder, coeffs, quantized, width, 0, 0, llW, llH,
        threshold, Iw44BitContext.GetBucket(maxLevel, 0), bitplane, bitCtx);

    // Encode detail subbands from coarsest to finest
    for (var level = maxLevel - 1; level >= 0; --level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 1 || levelH <= 1)
        continue;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      // LH subband (horizontal detail)
      _EncodeSubBand(encoder, coeffs, quantized, width, halfW, 0, levelW - halfW, halfH,
        threshold, Iw44BitContext.GetBucket(level, 0), bitplane, bitCtx);

      // HL subband (vertical detail)
      _EncodeSubBand(encoder, coeffs, quantized, width, 0, halfH, halfW, levelH - halfH,
        threshold, Iw44BitContext.GetBucket(level, 1), bitplane, bitCtx);

      // HH subband (diagonal detail)
      _EncodeSubBand(encoder, coeffs, quantized, width, halfW, halfH, levelW - halfW, levelH - halfH,
        threshold, Iw44BitContext.GetBucket(level, 2), bitplane, bitCtx);
    }
  }

  /// <summary>Returns the maximum useful decomposition level for the given dimensions.</summary>
  private static int _MaxLevelFor(int w, int h)
    => Math.Min(_DECOMPOSITION_LEVELS, (int)Math.Floor(Math.Log2(Math.Min(w, h))));

  /// <summary>
  /// Encodes significance/sign/refinement bits for one sub-band region.
  /// Uses the quantized array to make significance decisions identical to the decoder.
  /// </summary>
  private static void _EncodeSubBand(ZpEncoder encoder, short[] coeffs, short[] quantized,
    int stride, int startX, int startY, int subWidth, int subHeight,
    short threshold, int bucket, int bitplane, Iw44BitContext bitCtx) {

    var half = (short)(threshold >> 1);
    if (half == 0)
      half = 1;

    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      var idx = y * stride + x;
      if (idx >= coeffs.Length)
        continue;

      if (quantized[idx] == 0) {
        // Not yet significant in decoder's view -- encode significance bit
        var absVal = Math.Abs(coeffs[idx]);
        var significant = absVal >= threshold ? 1 : 0;
        encoder.EncodeBit(significant, ref bitCtx.Significance(bucket, bitplane));

        if (significant != 0) {
          // Encode sign
          encoder.EncodeBit(coeffs[idx] < 0 ? 1 : 0, ref bitCtx.Sign(bucket));
          // Mirror decoder: set quantized to +/- threshold
          quantized[idx] = coeffs[idx] < 0 ? (short)-threshold : threshold;
        }
      } else {
        // Already significant in decoder's view -- encode refinement bit
        var absOrig = Math.Abs(coeffs[idx]);
        var absQuant = Math.Abs(quantized[idx]);
        // The refinement bit indicates whether the actual value is above or below
        // the current quantized midpoint (quantized + half)
        var refineBit = absOrig >= absQuant + half ? 1 : 0;
        encoder.EncodeBit(refineBit, ref bitCtx.Refinement(bucket, bitplane));

        // Mirror decoder: adjust quantized coefficient
        if (refineBit != 0)
          quantized[idx] = quantized[idx] > 0
            ? (short)(quantized[idx] + half)
            : (short)(quantized[idx] - half);
      }
    }
  }

  /// <summary>
  /// Pads a 2D array by replicating edge pixels to fill the padded region.
  /// Prevents large boundary artifacts in the wavelet transform.
  /// </summary>
  private static void _PadEdge(int[] data, int stride, int paddedH, int realW, int realH) {
    if (realW <= 0 || realH <= 0)
      return;

    // Pad columns beyond realW by replicating the rightmost pixel in each row
    for (var y = 0; y < realH; ++y) {
      var edgeVal = data[y * stride + realW - 1];
      for (var x = realW; x < stride; ++x)
        data[y * stride + x] = edgeVal;
    }

    // Pad rows beyond realH by replicating the last valid row (which is now fully padded horizontally)
    for (var y = realH; y < paddedH; ++y)
      for (var x = 0; x < stride; ++x)
        data[y * stride + x] = data[(realH - 1) * stride + x];
  }

  private static short[] _IntToShort(int[] src) {
    var dst = new short[src.Length];
    for (var i = 0; i < src.Length; ++i)
      dst[i] = (short)Math.Clamp(src[i], short.MinValue, short.MaxValue);
    return dst;
  }
}
