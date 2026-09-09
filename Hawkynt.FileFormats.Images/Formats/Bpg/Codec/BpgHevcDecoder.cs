using System;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>Top-level decoder that wires BPG container data through the HEVC I-frame decoder pipeline.</summary>
internal static class BpgHevcDecoder {

  /// <summary>Decodes the HEVC bitstream embedded in a BPG file and returns pixel data.</summary>
  /// <param name="bpg">The parsed BPG file with raw HEVC data in <see cref="BpgFile.PixelData"/>.</param>
  /// <returns>Decoded pixel data: Gray8 for grayscale, RGB24 for color, or RGBA32 if alpha is present.</returns>
  public static byte[] Decode(BpgFile bpg) {
    ArgumentNullException.ThrowIfNull(bpg);

    if (bpg.PixelData.Length == 0)
      throw new InvalidOperationException("BPG file contains no HEVC data.");

    if (bpg.IsAnimation)
      throw new NotSupportedException("Animated BPG decoding is not supported.");

    // alpha1_flag clear with alpha2_flag set is how BPG says the extra plane holds CMYK's W rather
    // than an alpha channel, which is a fourth ink and not a transparency.
    if (bpg is { HasAlpha: false, HasAlpha2: true })
      throw new NotSupportedException("A CMYK BPG picture is not supported for decoding.");

    if (_TryDecodeUniformPcm(bpg, out var pcm))
      return pcm;

    // Parse NAL units from the BPG HEVC data
    var (vps, sps, pps, sliceHeader, sliceData) = HevcNalParser.ParseBpgHevcData(bpg.PixelData);

    // Validate that this is an I-slice
    if (sliceHeader.SliceType != 2)
      throw new NotSupportedException($"Only I-slices are supported for BPG decoding (got slice type {sliceHeader.SliceType}).");

    // Validate bit depth
    var bitDepthY = sps.BitDepthLumaMinus8 + 8;
    var bitDepthC = sps.BitDepthChromaMinus8 + 8;
    if (bitDepthY != 8 && bitDepthY != 10)
      throw new NotSupportedException($"Only 8-bit and 10-bit luma bit depths are supported (got {bitDepthY}).");

    // Validate dimensions match BPG header
    var width = sps.PicWidthInLumaSamples;
    var height = sps.PicHeightInLumaSamples;

    // Apply conformance window cropping
    if (sps.ConformanceWindowPresent) {
      var cropUnitX = sps.ChromaFormatIdc is 1 or 2 ? 2 : 1;
      var cropUnitY = sps.ChromaFormatIdc == 1 ? 2 : 1;
      width -= (sps.ConfWinLeftOffset + sps.ConfWinRightOffset) * cropUnitX;
      height -= (sps.ConfWinTopOffset + sps.ConfWinBottomOffset) * cropUnitY;
    }

    // Create and run the I-slice decoder
    var decoder = new HevcSliceDecoder(sps, pps, sliceHeader);
    var (yPlane, cbPlane, crPlane, yStride, cStride) = decoder.Decode(sliceData);

    // Apply conformance window offset to output
    var cropX = 0;
    var cropY = 0;
    if (sps.ConformanceWindowPresent) {
      var cropUnitX = sps.ChromaFormatIdc is 1 or 2 ? 2 : 1;
      var cropUnitY = sps.ChromaFormatIdc == 1 ? 2 : 1;
      cropX = sps.ConfWinLeftOffset * cropUnitX;
      cropY = sps.ConfWinTopOffset * cropUnitY;
    }

    // Crop planes if needed
    if (cropX > 0 || cropY > 0) {
      yPlane = _CropPlane(yPlane, yStride, cropX, cropY, width, height);
      yStride = width;

      if (cbPlane.Length > 0) {
        int chromaSubX, chromaSubY;
        _GetChromaSubsampling(sps.ChromaFormatIdc, out chromaSubX, out chromaSubY);
        var chromaCropX = cropX / chromaSubX;
        var chromaCropY = cropY / chromaSubY;
        var chromaWidth = (width + chromaSubX - 1) / chromaSubX;
        var chromaHeight = (height + chromaSubY - 1) / chromaSubY;

        cbPlane = _CropPlane(cbPlane, cStride, chromaCropX, chromaCropY, chromaWidth, chromaHeight);
        crPlane = _CropPlane(crPlane, cStride, chromaCropX, chromaCropY, chromaWidth, chromaHeight);
        cStride = chromaWidth;
      }
    }

    // Convert to output pixel format
    if (bpg.PixelFormat == BpgPixelFormat.Grayscale)
      return HevcYuvToRgb.ConvertGrayscale(yPlane, yStride, width, height, bitDepthY, bpg.LimitedRange);

    // Color space: either YCbCr->RGB or direct RGB
    if (bpg.ColorSpace == BpgColorSpace.Rgb && sps.ChromaFormatIdc == 3)
      return _ExtractDirectRgb(yPlane, cbPlane, crPlane, yStride, cStride, width, height, bitDepthY);

    // YCbCr to RGB conversion
    var chromaFormat = bpg.PixelFormat;
    if (bpg.HasAlpha) {
      // TODO: Alpha plane decoding requires a second HEVC stream in BPG
      // For now, return RGB24 without alpha
      return HevcYuvToRgb.Convert(
        yPlane, cbPlane, crPlane, yStride, cStride,
        width, height, chromaFormat, bpg.ColorSpace, bitDepthY, bpg.LimitedRange
      );
    }

    return HevcYuvToRgb.Convert(
      yPlane, cbPlane, crPlane, yStride, cStride,
      width, height, chromaFormat, bpg.ColorSpace, bitDepthY, bpg.LimitedRange
    );
  }

  /// <summary>
  /// Decodes a picture whose coding units are all unsplit PCM ones, which is the shape this package
  /// writes, and answers false for everything else so the general path still gets its turn.
  /// </summary>
  /// <remarks>
  /// PCM coding units carry their samples verbatim, so this path needs none of the transform,
  /// prediction or residual machinery the general decoder is made of — but it is not a shortcut
  /// around it either. A stream it declines is one it has proved is not made of whole samples, not
  /// one it merely failed to read.
  /// </remarks>
  private static bool _TryDecodeUniformPcm(BpgFile bpg, out byte[] pixels) {
    pixels = [];
    if (bpg.BitDepth != 8 || bpg.HasAlpha || bpg.HasAlpha2 || bpg.LimitedRange)
      return false;

    // 4:4:4 with no colour matrix is the one shape this path reads, and the one this package
    // writes. Subsampled chroma and the YCbCr matrices go to the general decoder instead.
    if (bpg is not { PixelFormat: BpgPixelFormat.YCbCr444, ColorSpace: BpgColorSpace.Rgb })
      return false;

    var header = BpgSequenceHeader.TryParse(bpg.PixelData);
    if (header is not {
          PcmEnabled: true, SaoEnabled: false,
          PcmBitDepthLuma: 8, PcmBitDepthChroma: 8,
          Log2CtbSize: _CODING_TREE_BLOCK_LOG2,
          Log2MinLumaCodingBlockSize: _CODING_TREE_BLOCK_LOG2,
        }
        || header.Log2MinPcmCodingBlockSize > _CODING_TREE_BLOCK_LOG2
        || header.Log2MaxPcmCodingBlockSize < _CODING_TREE_BLOCK_LOG2)
      return false;

    var block = 1 << _CODING_TREE_BLOCK_LOG2;
    var codedWidth = (bpg.Width + block - 1) / block * block;
    var codedHeight = (bpg.Height + block - 1) / block * block;
    if (!H265PcmStillCodec.TryDecodeBpgStill(
          bpg.PixelData.AsSpan(header.HevcDataOffset), codedWidth, codedHeight, out var planes))
      return false;

    pixels = _InterleaveGbr(planes, codedWidth, bpg.Width, bpg.Height);
    return true;
  }

  /// <summary>
  /// The tree block size <see cref="H265PcmStillCodec"/> writes, which clause 7.4.3.2.1 also caps
  /// a PCM coding block at.
  /// </summary>
  private const int _CODING_TREE_BLOCK_LOG2 = 5;

  /// <summary>Packs BPG's G, B and R planes into the interleaved RGB24 the rest of the package uses.</summary>
  private static byte[] _InterleaveGbr(byte[][] planes, int stride, int width, int height) {
    var result = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var from = y * stride + x;
        var to = (y * width + x) * 3;
        result[to] = planes[2][from];
        result[to + 1] = planes[0][from];
        result[to + 2] = planes[1][from];
      }

    return result;
  }

  private static int[] _CropPlane(int[] plane, int stride, int cropX, int cropY, int width, int height) {
    var result = new int[width * height];
    for (var y = 0; y < height; ++y) {
      var srcOffset = (cropY + y) * stride + cropX;
      var dstOffset = y * width;
      var count = Math.Min(width, plane.Length - srcOffset);
      if (count > 0)
        Array.Copy(plane, srcOffset, result, dstOffset, count);
    }
    return result;
  }

  /// <summary>Extracts direct RGB when BPG signals RGB color space with 4:4:4 chroma.</summary>
  private static byte[] _ExtractDirectRgb(int[] gPlane, int[] bPlane, int[] rPlane, int gStride, int brStride, int width, int height, int bitDepth) {
    // In BPG RGB mode with 4:4:4, the three planes store G, B, R (not Y, Cb, Cr)
    var rgb = new byte[width * height * 3];
    var maxVal = (1 << bitDepth) - 1;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        var gIdx = y * gStride + x;
        var brIdx = y * brStride + x;

        rgb[idx] = (byte)Math.Clamp(rPlane[brIdx] * 255 / maxVal, 0, 255);
        rgb[idx + 1] = (byte)Math.Clamp(gPlane[gIdx] * 255 / maxVal, 0, 255);
        rgb[idx + 2] = (byte)Math.Clamp(bPlane[brIdx] * 255 / maxVal, 0, 255);
      }

    return rgb;
  }

  private static void _GetChromaSubsampling(int chromaFormatIdc, out int subX, out int subY) {
    switch (chromaFormatIdc) {
      case 1: subX = 2; subY = 2; break;
      case 2: subX = 2; subY = 1; break;
      case 3: subX = 1; subY = 1; break;
      default: subX = 1; subY = 1; break;
    }
  }
}
