using System;
using System.Buffers.Binary;

namespace FileFormat.Ecw.Codec;

/// <summary>
/// ECW (Enhanced Compressed Wavelet) image decoder.
/// Decodes range-coded wavelet coefficients via <see cref="EcwRangeDecoder"/>,
/// dequantizes via <see cref="EcwQuantizer"/>,
/// and applies inverse CDF 9/7 wavelet transform via <see cref="EcwWavelet"/>.
/// </summary>
internal static class EcwDecoder {

  // ECW header field sizes
  private const int _MAGIC_SIZE = 4;
  private const int _HEADER_FIXED_SIZE = 24;

  // ECW magic bytes
  private static ReadOnlySpan<byte> _EcwMagic => [0x65, 0x63, 0x77, 0x00]; // "ecw\0"

  /// <summary>Maximum supported wavelet decomposition levels.</summary>
  private const int _MAX_LEVELS = 6;

  /// <summary>Default base quantization step size (must match encoder).</summary>
  private const double _DEFAULT_BASE_STEP = 1.0;

  /// <summary>
  /// Attempts to decode ECW compressed data into RGB24 pixel data.
  /// </summary>
  /// <param name="data">Full ECW file data.</param>
  /// <param name="fallbackWidth">Width to use if header parsing fails.</param>
  /// <param name="fallbackHeight">Height to use if header parsing fails.</param>
  /// <returns>Tuple of (pixelData, width, height).</returns>
  public static (byte[] PixelData, int Width, int Height) Decode(byte[] data, int fallbackWidth, int fallbackHeight) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < _HEADER_FIXED_SIZE)
      return (_FallbackDecode(data, fallbackWidth, fallbackHeight), fallbackWidth, fallbackHeight);

    if (!_TryParseHeader(data, out var header))
      return (_FallbackDecode(data, fallbackWidth, fallbackHeight), fallbackWidth, fallbackHeight);

    try {
      var pixels = _DecodeWavelet(data, header);
      return (pixels, header.Width, header.Height);
    } catch {
      return (_FallbackDecode(data, fallbackWidth, fallbackHeight), fallbackWidth, fallbackHeight);
    }
  }

  /// <summary>ECW file header.</summary>
  private readonly struct EcwHeader {
    public readonly int Width;
    public readonly int Height;
    public readonly int BandCount;
    public readonly int DecompositionLevels;
    public readonly int DataOffset;

    public EcwHeader(int width, int height, int bandCount, int decompositionLevels, int dataOffset) {
      Width = width;
      Height = height;
      BandCount = bandCount;
      DecompositionLevels = decompositionLevels;
      DataOffset = dataOffset;
    }
  }

  private static bool _TryParseHeader(byte[] data, out EcwHeader header) {
    header = default;

    if (data.Length < _HEADER_FIXED_SIZE)
      return false;

    if (!data.AsSpan(0, _MAGIC_SIZE).SequenceEqual(_EcwMagic))
      return false;

    // Parse header fields (all little-endian)
    var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(6));
    var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10));
    var bandCount = (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
    var decompositionLevels = (int)data[16];
    // data[17] = quantization bits indicator (reserved for future use)
    var dataOffset = _HEADER_FIXED_SIZE;

    if (width <= 0 || width > 65535) return false;
    if (height <= 0 || height > 65535) return false;
    if (bandCount < 1 || bandCount > 4) bandCount = 3;
    if (decompositionLevels < 1 || decompositionLevels > _MAX_LEVELS) decompositionLevels = 3;

    header = new(width, height, bandCount, decompositionLevels, dataOffset);
    return true;
  }

  /// <summary>Decodes wavelet-compressed data from the ECW bitstream.</summary>
  private static byte[] _DecodeWavelet(byte[] data, EcwHeader header) {
    var paddedW = EcwWavelet.PadToDecompositionBlock(header.Width, header.DecompositionLevels);
    var paddedH = EcwWavelet.PadToDecompositionBlock(header.Height, header.DecompositionLevels);
    var pixelCount = paddedW * paddedH;

    // Range-decode quantized coefficients per band
    var decoder = new EcwRangeDecoder(data, header.DataOffset);
    var quantizedBands = new int[header.BandCount][];
    for (var b = 0; b < header.BandCount; ++b) {
      quantizedBands[b] = new int[pixelCount];
      _DecodeSubBands(decoder, quantizedBands[b], paddedW, paddedH, header.DecompositionLevels);
    }

    // Dequantize and inverse wavelet transform per band
    var bands = new double[header.BandCount][];
    for (var b = 0; b < header.BandCount; ++b) {
      bands[b] = EcwQuantizer.DequantizeAllSubBands(quantizedBands[b], paddedW, paddedW, paddedH, header.DecompositionLevels, _DEFAULT_BASE_STEP);
      EcwWavelet.Inverse2D(bands[b], paddedW, paddedW, paddedH, header.DecompositionLevels);
    }

    // Interleave color bands into RGB24
    return _InterleaveBands(bands, header.Width, header.Height, paddedW);
  }

  /// <summary>Range-decodes all subbands for one band.</summary>
  private static void _DecodeSubBands(EcwRangeDecoder decoder, int[] coefficients, int width, int height, int levels) {
    var contexts = new EcwContextSet();

    // Decode detail subbands from coarsest to finest (must match encoder order)
    for (var level = levels - 1; level >= 0; --level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 0 || levelH <= 0)
        continue;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      // LH subband
      _DecodeSubBandRegion(decoder, coefficients, width, halfW, 0, halfW, halfH, contexts);
      // HL subband
      _DecodeSubBandRegion(decoder, coefficients, width, 0, halfH, halfW, halfH, contexts);
      // HH subband
      _DecodeSubBandRegion(decoder, coefficients, width, halfW, halfH, halfW, halfH, contexts);
    }

    // LL (DC) subband at coarsest level
    var dcW = Math.Max(width >> levels, 1);
    var dcH = Math.Max(height >> levels, 1);
    _DecodeSubBandRegion(decoder, coefficients, width, 0, 0, dcW, dcH, contexts);
  }

  /// <summary>Range-decodes one rectangular subband region.</summary>
  private static void _DecodeSubBandRegion(EcwRangeDecoder decoder, int[] coefficients, int stride,
    int startX, int startY, int subWidth, int subHeight, EcwContextSet contexts) {
    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      var idx = y * stride + x;
      if (idx >= coefficients.Length)
        continue;
      coefficients[idx] = decoder.DecodeCoefficient(contexts);
    }
  }

  /// <summary>Interleaves decoded color bands into RGB24 pixel data.</summary>
  private static byte[] _InterleaveBands(double[][] bands, int width, int height, int paddedWidth) {
    var result = new byte[width * height * 3];
    var bandCount = bands.Length;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var srcIdx = y * paddedWidth + x;
      var dstIdx = (y * width + x) * 3;

      if (bandCount >= 3) {
        result[dstIdx] = _Clamp(bands[0][srcIdx] + 128.0);
        result[dstIdx + 1] = _Clamp(bands[1][srcIdx] + 128.0);
        result[dstIdx + 2] = _Clamp(bands[2][srcIdx] + 128.0);
      } else {
        var gray = _Clamp(bands[0][srcIdx] + 128.0);
        result[dstIdx] = result[dstIdx + 1] = result[dstIdx + 2] = gray;
      }
    }

    return result;
  }

  private static byte _Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

  private static byte[] _FallbackDecode(byte[] data, int width, int height) {
    var pixelBytes = width * height * 3;
    var result = new byte[pixelBytes];
    var headerSize = data.Length >= _HEADER_FIXED_SIZE && data.AsSpan(0, _MAGIC_SIZE).SequenceEqual(_EcwMagic)
      ? _HEADER_FIXED_SIZE
      : EcwFile.HeaderSize;
    var available = Math.Min(pixelBytes, data.Length - headerSize);
    if (available > 0 && headerSize < data.Length)
      data.AsSpan(headerSize, available).CopyTo(result);
    return result;
  }
}
