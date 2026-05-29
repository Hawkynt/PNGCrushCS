using System;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// IW44 (InterWave) wavelet image decoder for DjVu.
/// Decodes progressive wavelet-encoded image data from BG44/FG44/PM44 chunks.
/// The IW44 codec processes data in "slices" (progressive quality layers),
/// where each slice adds more detail to the reconstructed image.
///
/// Pipeline: ZP-decode bitplanes -> de-quantize -> inverse CDF 5/3 wavelet -> YCbCr-to-RGB.
///
/// Wavelet coefficient layout after forward transform:
///   Level L stores subbands at 1/(2^L) resolution.
///   Each level has LH (horizontal detail), HL (vertical detail), HH (diagonal detail).
///   The coarsest LL subband sits in the top-left corner after all levels.
/// </summary>
internal sealed class Iw44Decoder {

  private readonly int _width;
  private readonly int _height;
  private readonly bool _isColor;

  // Wavelet coefficient buffers (YCbCr color space)
  private readonly short[] _yCoeffs;
  private readonly short[]? _cbCoeffs;
  private readonly short[]? _crCoeffs;

  // Coefficient dimensions (padded to multiples of block size)
  private readonly int _coeffWidth;
  private readonly int _coeffHeight;
  private readonly int _chromaW;
  private readonly int _chromaH;

  // Bitplane context model for progressive coding
  private readonly Iw44BitContext _bitCtx = new();

  // Number of slices decoded so far
  private int _slicesDecoded;

  // Current bitplane being decoded (starts high, decreases)
  private int _currentBitplane;

  /// <summary>Wavelet decomposition levels.</summary>
  internal const int DecompositionLevels = 5;

  /// <summary>Block alignment size.</summary>
  private const int _BLOCK_SIZE = 32;

  /// <summary>
  /// Maximum bitplane index (MSB). CDF 5/3 wavelet with 5 levels on 32x32 blocks
  /// can produce coefficients exceeding 2048, so we use 14 to cover up to 16383.
  /// </summary>
  private const int _MAX_BITPLANE = 14;

  public Iw44Decoder(int width, int height, bool isColor) {
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Invalid dimensions.");

    _width = width;
    _height = height;
    _isColor = isColor;

    // Pad to multiple of block size for wavelet transform
    _coeffWidth = (width + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);
    _coeffHeight = (height + _BLOCK_SIZE - 1) & ~(_BLOCK_SIZE - 1);

    var coeffCount = _coeffWidth * _coeffHeight;
    _yCoeffs = new short[coeffCount];

    if (isColor) {
      _chromaW = (_coeffWidth + 1) / 2;
      _chromaH = (_coeffHeight + 1) / 2;
      _cbCoeffs = new short[_chromaW * _chromaH];
      _crCoeffs = new short[_chromaW * _chromaH];
    }

    _currentBitplane = _MAX_BITPLANE;
  }

  /// <summary>Number of slices decoded so far.</summary>
  public int SlicesDecoded => _slicesDecoded;

  /// <summary>
  /// Decodes one progressive slice from the given chunk data.
  /// Each slice refines the existing coefficients with more detail.
  /// </summary>
  /// <param name="chunkData">Raw BG44/FG44/PM44 chunk data.</param>
  /// <returns>True if the slice was decoded successfully; false if the data is insufficient.</returns>
  public bool DecodeSlice(byte[] chunkData) {
    ArgumentNullException.ThrowIfNull(chunkData);
    if (chunkData.Length < 2)
      return false;

    // Parse slice header
    var sliceHeader = chunkData[0];
    var sliceCount = sliceHeader & 0x7F;
    if (sliceCount == 0)
      sliceCount = 1;

    var dataOffset = 1;

    // For the first slice, skip additional header bytes if present
    if (_slicesDecoded == 0 && chunkData.Length >= 4) {
      // First slice has extra 2-byte header (version/flags info)
      dataOffset = 3;
    }

    if (dataOffset >= chunkData.Length)
      return false;

    var zp = new ZpDecoder(chunkData, dataOffset);

    for (var s = 0; s < sliceCount && !zp.IsEof; ++s) {
      var bitplane = Math.Max(0, _currentBitplane);

      // Decode luminance channel
      _DecodeBitplane(zp, _yCoeffs, _coeffWidth, _coeffHeight, bitplane);

      // Decode chroma channels
      if (_isColor && _cbCoeffs != null && _crCoeffs != null) {
        _DecodeBitplane(zp, _cbCoeffs, _chromaW, _chromaH, Math.Max(0, bitplane - 1));
        _DecodeBitplane(zp, _crCoeffs, _chromaW, _chromaH, Math.Max(0, bitplane - 1));
      }

      --_currentBitplane;
    }

    _slicesDecoded += sliceCount;
    return true;
  }

  /// <summary>
  /// Reconstructs the image from the decoded wavelet coefficients.
  /// Returns RGB24 pixel data (top-to-bottom, left-to-right).
  /// </summary>
  public byte[] Reconstruct() {
    // Apply inverse wavelet transform to Y channel
    var yPixels = _ShortToInt(_yCoeffs, _coeffWidth * _coeffHeight);
    Iw44Wavelet.Inverse(yPixels, _coeffWidth, _coeffWidth, _coeffHeight, DecompositionLevels);

    if (_isColor && _cbCoeffs != null && _crCoeffs != null) {
      var cbPixels = _ShortToInt(_cbCoeffs, _chromaW * _chromaH);
      var crPixels = _ShortToInt(_crCoeffs, _chromaW * _chromaH);

      Iw44Wavelet.Inverse(cbPixels, _chromaW, _chromaW, _chromaH, DecompositionLevels - 1);
      Iw44Wavelet.Inverse(crPixels, _chromaW, _chromaW, _chromaH, DecompositionLevels - 1);

      return _YCbCrToRgb(yPixels, cbPixels, crPixels);
    }

    // Grayscale: Y -> identical R=G=B
    var result = new byte[_width * _height * 3];
    for (var py = 0; py < _height; ++py)
    for (var px = 0; px < _width; ++px) {
      var val = _Clamp(yPixels[py * _coeffWidth + px] + 128);
      var idx = (py * _width + px) * 3;
      result[idx] = result[idx + 1] = result[idx + 2] = (byte)val;
    }
    return result;
  }

  /// <summary>
  /// Decodes one bitplane of wavelet coefficients for a single channel.
  /// Processes the LL (DC) subband first, then detail subbands from coarsest to finest.
  /// For each coefficient:
  ///   - If zero (not yet significant): decode significance bit, then sign if newly significant.
  ///   - If non-zero (already significant): decode refinement bit to add precision.
  /// </summary>
  private void _DecodeBitplane(ZpDecoder zp, short[] coeffs, int width, int height, int bitplane) {
    var threshold = (short)(1 << bitplane);
    var maxLevel = Math.Min(DecompositionLevels, _MaxLevelFor(width, height));

    // Decode LL subband (coarsest approximation) first
    var llW = width >> maxLevel;
    var llH = height >> maxLevel;
    if (llW > 0 && llH > 0)
      _DecodeSubBand(zp, coeffs, width, 0, 0, llW, llH,
        threshold, Iw44BitContext.GetBucket(maxLevel, 0), bitplane);

    // Decode detail subbands from coarsest to finest
    for (var level = maxLevel - 1; level >= 0; --level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 1 || levelH <= 1)
        continue;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      // LH: horizontal detail
      _DecodeSubBand(zp, coeffs, width, halfW, 0, levelW - halfW, halfH,
        threshold, Iw44BitContext.GetBucket(level, 0), bitplane);

      // HL: vertical detail
      _DecodeSubBand(zp, coeffs, width, 0, halfH, halfW, levelH - halfH,
        threshold, Iw44BitContext.GetBucket(level, 1), bitplane);

      // HH: diagonal detail
      _DecodeSubBand(zp, coeffs, width, halfW, halfH, levelW - halfW, levelH - halfH,
        threshold, Iw44BitContext.GetBucket(level, 2), bitplane);
    }
  }

  /// <summary>Returns the maximum useful decomposition level for the given dimensions.</summary>
  private static int _MaxLevelFor(int w, int h)
    => Math.Min(DecompositionLevels, (int)Math.Floor(Math.Log2(Math.Min(w, h))));

  /// <summary>Decodes significance/sign/refinement bits for one sub-band region.</summary>
  private void _DecodeSubBand(ZpDecoder zp, short[] coeffs, int stride,
    int startX, int startY, int subWidth, int subHeight,
    short threshold, int bucket, int bitplane) {

    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      if (zp.IsEof)
        return;

      var idx = y * stride + x;
      if (idx >= coeffs.Length)
        continue;

      if (coeffs[idx] == 0) {
        // Not yet significant: test if it becomes significant at this bitplane
        var significant = zp.DecodeBit(ref _bitCtx.Significance(bucket, bitplane));
        if (significant != 0) {
          var sign = zp.DecodeBit(ref _bitCtx.Sign(bucket));
          coeffs[idx] = sign != 0 ? (short)-threshold : threshold;
        }
      } else {
        // Already significant: decode refinement bit
        var refine = zp.DecodeBit(ref _bitCtx.Refinement(bucket, bitplane));
        if (refine != 0) {
          var half = (short)(threshold >> 1);
          if (half == 0)
            half = 1;
          coeffs[idx] = coeffs[idx] > 0 ? (short)(coeffs[idx] + half) : (short)(coeffs[idx] - half);
        }
      }
    }
  }

  private static int[] _ShortToInt(short[] src, int count) {
    var dst = new int[count];
    var len = Math.Min(src.Length, count);
    for (var i = 0; i < len; ++i)
      dst[i] = src[i];
    return dst;
  }

  /// <summary>Converts YCbCr to RGB24 with chroma upsampling (nearest neighbor).</summary>
  private byte[] _YCbCrToRgb(int[] yData, int[] cbData, int[] crData) {
    var result = new byte[_width * _height * 3];

    for (var py = 0; py < _height; ++py)
    for (var px = 0; px < _width; ++px) {
      var yVal = yData[py * _coeffWidth + px] + 128;
      var cIdx = (py / 2) * _chromaW + px / 2;
      var cbVal = cIdx < cbData.Length ? cbData[cIdx] : 0;
      var crVal = cIdx < crData.Length ? crData[cIdx] : 0;

      // Standard YCbCr -> RGB conversion (ITU-R BT.601)
      var r = yVal + ((crVal * 91881 + 32768) >> 16);
      var g = yVal - ((cbVal * 22554 + crVal * 46802 + 32768) >> 16);
      var b = yVal + ((cbVal * 116130 + 32768) >> 16);

      var idx = (py * _width + px) * 3;
      result[idx] = (byte)_Clamp(r);
      result[idx + 1] = (byte)_Clamp(g);
      result[idx + 2] = (byte)_Clamp(b);
    }

    return result;
  }

  private static int _Clamp(int value) => Math.Clamp(value, 0, 255);
}
