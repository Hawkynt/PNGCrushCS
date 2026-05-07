using System;

namespace FileFormat.Jpeg;

/// <summary>
/// Pure-managed JPEG lossy encoder. Builds a <see cref="JpegImage"/> from RGB pixel data —
/// color-converts to YCbCr, downsamples chroma per <see cref="JpegSubsampling"/>, splits each
/// component into 8x8 blocks (with edge-replicating padding), runs forward DCT + quantization,
/// stores coefficients in zigzag order — then delegates the byte-stream emit to
/// <see cref="JpegCoefficientWriter"/>.
///
/// Exists because <c>BitMiracle.LibJpeg.NET</c> raises "Not implemented yet" for 4:2:2
/// chroma subsampling. This encoder fills that gap and supports all three modes uniformly.
/// </summary>
internal static class JpegManagedEncoder {

  /// <summary>Encodes RGB pixel data as a baseline JPEG with the specified subsampling.</summary>
  /// <param name="rgbPixelData">Packed RGB24 (or grayscale) pixel data, top-left origin, no padding.</param>
  /// <param name="quality">IJG quality factor, 1–100.</param>
  /// <param name="mode">Baseline or progressive.</param>
  /// <param name="subsampling">Chroma subsampling mode (ignored for grayscale).</param>
  /// <param name="optimizeHuffman">When true, performs two-pass optimal Huffman table construction.</param>
  /// <param name="isGrayscale">When true, <paramref name="rgbPixelData"/> is single-channel Gray8.</param>
  /// <param name="stripMetadata">When true, omits any preserved marker segments (always true for fresh encodes).</param>
  public static byte[] Encode(
    byte[] rgbPixelData, int width, int height,
    int quality, JpegMode mode, JpegSubsampling subsampling,
    bool optimizeHuffman, bool isGrayscale, bool stripMetadata = true
  ) {
    ArgumentNullException.ThrowIfNull(rgbPixelData);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

    // 1. Color separation
    byte[] yPlane;
    byte[]? cbPlane = null;
    byte[]? crPlane = null;
    if (isGrayscale) {
      yPlane = JpegColorConverter.RgbToGrayscale(rgbPixelData, width, height);
    } else {
      var planes = JpegColorConverter.RgbToYCbCr(rgbPixelData, width, height);
      yPlane = planes[0]; cbPlane = planes[1]; crPlane = planes[2];
    }

    // 2. Sampling factors. For grayscale only one component @ 1x1.
    //    For color: Y at maxH×maxV, Cb/Cr at 1×1 (subsampled by chromaH/chromaV).
    var (chromaH, chromaV) = isGrayscale
      ? (1, 1)
      : JpegColorConverter.GetChromaFactors(subsampling);
    var maxH = chromaH;
    var maxV = chromaV;

    // 3. Subsample Cb/Cr if needed (box-filter average)
    var cWidth = (width + chromaH - 1) / chromaH;
    var cHeight = (height + chromaV - 1) / chromaV;
    if (!isGrayscale && (chromaH > 1 || chromaV > 1)) {
      cbPlane = JpegColorConverter.Downsample(cbPlane!, width, height, chromaH, chromaV);
      crPlane = JpegColorConverter.Downsample(crPlane!, width, height, chromaH, chromaV);
    }

    // 4. Quantization tables — natural order for in-loop quantization,
    //    zigzag order for DQT serialization (the JPEG bitstream stores DQT in zigzag).
    var lumQuantNatural = JpegQuantizer.BuildQuantTable(isLuminance: true, quality);
    var lumQuantZigzag = _NaturalToZigzag(lumQuantNatural);

    int[]? chromQuantNatural = null;
    int[]? chromQuantZigzag = null;
    if (!isGrayscale) {
      chromQuantNatural = JpegQuantizer.BuildQuantTable(isLuminance: false, quality);
      chromQuantZigzag = _NaturalToZigzag(chromQuantNatural);
    }

    // 5. Component data — MCU dimensions in pixels are maxH*8 × maxV*8.
    var mcuCols = (width + maxH * 8 - 1) / (maxH * 8);
    var mcuRows = (height + maxV * 8 - 1) / (maxV * 8);

    var componentCount = isGrayscale ? 1 : 3;
    var components = new JpegComponentInfo[componentCount];
    var componentData = new JpegComponentData[componentCount];

    // Y component spans the full MCU at maxH×maxV blocks.
    components[0] = new() { Id = 1, HSamplingFactor = (byte)maxH, VSamplingFactor = (byte)maxV, QuantTableId = 0 };
    componentData[0] = _BuildComponent(yPlane, width, height, mcuCols, mcuRows, maxH, maxV, lumQuantNatural);

    if (!isGrayscale) {
      // Cb and Cr each occupy 1×1 block per MCU on the subsampled grid.
      components[1] = new() { Id = 2, HSamplingFactor = 1, VSamplingFactor = 1, QuantTableId = 1 };
      componentData[1] = _BuildComponent(cbPlane!, cWidth, cHeight, mcuCols, mcuRows, 1, 1, chromQuantNatural!);
      components[2] = new() { Id = 3, HSamplingFactor = 1, VSamplingFactor = 1, QuantTableId = 1 };
      componentData[2] = _BuildComponent(crPlane!, cWidth, cHeight, mcuCols, mcuRows, 1, 1, chromQuantNatural!);
    }

    var quantTables = new JpegQuantTable[isGrayscale ? 1 : 2];
    quantTables[0] = new() { TableId = 0, Values = lumQuantZigzag, Is16Bit = false };
    if (!isGrayscale)
      quantTables[1] = new() { TableId = 1, Values = chromQuantZigzag!, Is16Bit = false };

    var frame = new JpegFrameHeader {
      Precision = 8,
      Width = width,
      Height = height,
      Components = components,
      IsProgressive = mode == JpegMode.Progressive,
    };

    var image = new JpegImage {
      Frame = frame,
      QuantTables = quantTables,
      ComponentData = componentData,
      RestartInterval = 0,
    };

    return JpegCoefficientWriter.Write(image, mode, optimizeHuffman, stripMetadata);
  }

  /// <summary>Splits one image plane into 8×8 blocks, runs FDCT + quantization,
  /// stores coefficients in zigzag order. Edges are replicated when the plane
  /// dimensions don't reach the MCU boundary.</summary>
  private static JpegComponentData _BuildComponent(
    byte[] plane, int planeWidth, int planeHeight,
    int mcuCols, int mcuRows, int hSamp, int vSamp,
    int[] quantNatural
  ) {
    var widthInBlocks = mcuCols * hSamp;
    var heightInBlocks = mcuRows * vSamp;
    var data = JpegComponentData.Allocate(widthInBlocks, heightInBlocks);

    var dctBuf = new int[64];

    for (var by = 0; by < heightInBlocks; ++by)
      for (var bx = 0; bx < widthInBlocks; ++bx) {
        // Load 8×8 with edge-replicating padding and level shift (subtract 128).
        for (var py = 0; py < 8; ++py) {
          var sy = Math.Min(by * 8 + py, planeHeight - 1);
          var rowOffset = sy * planeWidth;
          var dstOffset = py * 8;
          for (var px = 0; px < 8; ++px) {
            var sx = Math.Min(bx * 8 + px, planeWidth - 1);
            dctBuf[dstOffset + px] = plane[rowOffset + sx] - 128;
          }
        }

        JpegDct.ForwardDct(dctBuf);

        // Quantize natural-indexed DCT outputs and write into the zigzag-ordered
        // coefficient block. quantNatural[natIdx] equals quantZigzag[k] (same value,
        // different index conventions).
        var coefficients = data.Blocks[by][bx].Coefficients;
        for (var k = 0; k < 64; ++k) {
          var natIdx = JpegZigZag.Order[k];
          coefficients[k] = JpegQuantizer.Quantize(dctBuf[natIdx], quantNatural[natIdx]);
        }
      }

    return data;
  }

  private static int[] _NaturalToZigzag(int[] natural) {
    var z = new int[64];
    for (var k = 0; k < 64; ++k)
      z[k] = natural[JpegZigZag.Order[k]];
    return z;
  }
}
