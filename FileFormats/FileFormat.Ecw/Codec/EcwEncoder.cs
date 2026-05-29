using System;
using System.IO;

namespace FileFormat.Ecw.Codec;

/// <summary>
/// ECW (Enhanced Compressed Wavelet) image encoder.
/// Performs forward CDF 9/7 wavelet transform via <see cref="EcwWavelet"/>,
/// quantizes coefficients via <see cref="EcwQuantizer"/>,
/// range-encodes via <see cref="EcwRangeEncoder"/>,
/// and assembles the ECW bitstream.
/// </summary>
internal static class EcwEncoder {

  // ECW magic bytes
  private static ReadOnlySpan<byte> _EcwMagic => [0x65, 0x63, 0x77, 0x00]; // "ecw\0"

  /// <summary>Default decomposition levels for wavelet transform.</summary>
  private const int _DEFAULT_LEVELS = 3;

  /// <summary>Default base quantization step size (controls lossy quality).</summary>
  private const double _DEFAULT_BASE_STEP = 1.0;

  /// <summary>
  /// Encodes RGB24 pixel data into an ECW compressed file.
  /// </summary>
  /// <param name="pixelData">RGB24 pixel data (3 bytes per pixel).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <returns>Complete ECW file bytes.</returns>
  public static byte[] Encode(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    const int bandCount = 3;
    var levels = _DEFAULT_LEVELS;

    var paddedW = EcwWavelet.PadToDecompositionBlock(width, levels);
    var paddedH = EcwWavelet.PadToDecompositionBlock(height, levels);
    var pixelCount = paddedW * paddedH;

    // Separate RGB into per-band double arrays centered around zero
    var bands = new double[bandCount][];
    for (var b = 0; b < bandCount; ++b)
      bands[b] = new double[pixelCount];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var srcIdx = (y * width + x) * 3;
      var dstIdx = y * paddedW + x;
      bands[0][dstIdx] = pixelData[srcIdx] - 128.0;
      bands[1][dstIdx] = pixelData[srcIdx + 1] - 128.0;
      bands[2][dstIdx] = pixelData[srcIdx + 2] - 128.0;
    }

    // Forward wavelet transform per band
    for (var b = 0; b < bandCount; ++b)
      EcwWavelet.Forward2D(bands[b], paddedW, paddedW, paddedH, levels);

    // Quantize per band with per-subband step sizes
    var quantizedBands = new int[bandCount][];
    for (var b = 0; b < bandCount; ++b)
      quantizedBands[b] = EcwQuantizer.QuantizeAllSubBands(bands[b], paddedW, paddedW, paddedH, levels, _DEFAULT_BASE_STEP);

    // Range-encode the quantized coefficients
    byte[] encodedData;
    using (var encoder = new EcwRangeEncoder()) {
      for (var b = 0; b < bandCount; ++b)
        _EncodeSubBands(encoder, quantizedBands[b], paddedW, paddedH, levels);
      encodedData = encoder.Finish();
    }

    // Assemble the file
    using var ms = new MemoryStream();
    _WriteHeader(ms, width, height, bandCount, levels);
    ms.Write(encodedData);
    return ms.ToArray();
  }

  /// <summary>Writes the ECW file header.</summary>
  private static void _WriteHeader(MemoryStream ms, int width, int height, int bandCount, int levels) {
    // Magic: "ecw\0"
    ms.Write(_EcwMagic);

    Span<byte> buf = stackalloc byte[4];

    // Version: 3 (ECW3 format with arithmetic coding)
    buf[0] = 3; buf[1] = 0;
    ms.Write(buf[..2]);

    // Width (int32 LE)
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, width);
    ms.Write(buf[..4]);

    // Height (int32 LE)
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buf, height);
    ms.Write(buf[..4]);

    // Band count (uint16 LE)
    buf[0] = (byte)(bandCount & 0xFF); buf[1] = (byte)((bandCount >> 8) & 0xFF);
    ms.Write(buf[..2]);

    // Decomposition levels
    ms.WriteByte((byte)levels);

    // Quantization bits (used as base step indicator, 8 = step 1.0)
    ms.WriteByte(8);

    // Reserved / cell size (6 bytes)
    Span<byte> reserved = stackalloc byte[6];
    reserved.Clear();
    ms.Write(reserved);
  }

  /// <summary>Range-encodes all subbands of a quantized band.</summary>
  private static void _EncodeSubBands(EcwRangeEncoder encoder, int[] quantized, int width, int height, int levels) {
    var contexts = new EcwContextSet();

    // Encode detail subbands from coarsest to finest
    for (var level = levels - 1; level >= 0; --level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 0 || levelH <= 0)
        continue;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      // LH subband
      _EncodeSubBandRegion(encoder, quantized, width, halfW, 0, halfW, halfH, contexts);
      // HL subband
      _EncodeSubBandRegion(encoder, quantized, width, 0, halfH, halfW, halfH, contexts);
      // HH subband
      _EncodeSubBandRegion(encoder, quantized, width, halfW, halfH, halfW, halfH, contexts);
    }

    // LL (DC) subband at the coarsest level
    var dcW = Math.Max(width >> levels, 1);
    var dcH = Math.Max(height >> levels, 1);
    _EncodeSubBandRegion(encoder, quantized, width, 0, 0, dcW, dcH, contexts);
  }

  /// <summary>Range-encodes one rectangular subband region.</summary>
  private static void _EncodeSubBandRegion(EcwRangeEncoder encoder, int[] quantized, int stride,
    int startX, int startY, int subWidth, int subHeight, EcwContextSet contexts) {
    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      var idx = y * stride + x;
      var value = idx < quantized.Length ? quantized[idx] : 0;
      encoder.EncodeCoefficient(value, contexts);
    }
  }
}
