using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>
/// Pure-managed JPEG decoder. Orchestrates marker parsing, entropy decoding,
/// inverse DCT, block assembly, chroma upsampling, and color conversion to
/// produce an RGB pixel buffer — no third-party library required.
/// </summary>
internal static class JpegManagedDecoder {

  /// <summary>Decodes JPEG data into a <see cref="JpegFile"/> with RGB pixel data.</summary>
  public static JpegFile Decode(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      throw new InvalidDataException("Data too small for a valid JPEG file.");

    if (data[0] != 0xFF || data[1] != 0xD8)
      throw new InvalidDataException("Invalid JPEG signature.");

    var dataArray = data.ToArray();

    // 1. Parse all non-SOS markers to get frame info, quant tables, Huffman tables.
    var image = JpegMarkerParser.ParseAllMarkers(dataArray);
    var frame = image.Frame;

    if (frame.Width == 0 || frame.Height == 0)
      throw new InvalidDataException("JPEG frame has zero dimensions.");

    // 2. Determine sampling factors.
    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    // 3. Allocate coefficient storage for each component.
    var componentData = new JpegComponentData[frame.Components.Length];
    for (var ci = 0; ci < frame.Components.Length; ++ci) {
      var comp = frame.Components[ci];
      var widthInBlocks = ((frame.Width * comp.HSamplingFactor + maxH - 1) / maxH + 7) / 8;
      var heightInBlocks = ((frame.Height * comp.VSamplingFactor + maxV - 1) / maxV + 7) / 8;

      // For interleaved scans the MCU grid may require extra blocks.
      var mcuCols = (frame.Width + maxH * 8 - 1) / (maxH * 8);
      var mcuRows = (frame.Height + maxV * 8 - 1) / (maxV * 8);
      var mcuW = mcuCols * comp.HSamplingFactor;
      var mcuH = mcuRows * comp.VSamplingFactor;
      if (mcuW > widthInBlocks) widthInBlocks = mcuW;
      if (mcuH > heightInBlocks) heightInBlocks = mcuH;

      componentData[ci] = JpegComponentData.Allocate(widthInBlocks, heightInBlocks);
    }

    image.ComponentData = componentData;

    // 4. Walk through the data a second time to find SOS markers and decode entropy data.
    _DecodeScanData(dataArray, frame, image, componentData);

    // 5. IDCT: dequantize and transform each 8x8 block to spatial domain.
    var planes = _InverseDctAllComponents(frame, image.QuantTables, componentData, maxH, maxV);

    // 6. Assemble, upsample chroma, color convert.
    var isGrayscale = frame.Components.Length == 1;
    var width = frame.Width;
    var height = frame.Height;

    byte[] rgbPixelData;
    if (isGrayscale) {
      // A single-component JPEG stays one byte a pixel. It used to be expanded to RGB triples here
      // while JpegFile.ToRawImage went on reporting the buffer as Gray8, so every consumer read three
      // bytes' worth of image for each pixel it wanted: the picture came out sheared, each row
      // showing pixels from a third of the way down the one above. The two ends of the format now
      // agree that grey means grey — which is also what the encoder has always assumed of this field.
      rgbPixelData = _CropPlane(
        planes[0], componentData[0].WidthInBlocks * 8, componentData[0].HeightInBlocks * 8, width, height);
    } else {
      // Crop each plane to its actual pixel dimensions, then upsample to full resolution.
      var yCompW = componentData[0].WidthInBlocks * 8;
      var yCompH = componentData[0].HeightInBlocks * 8;
      var yPlane = _CropPlane(planes[0], yCompW, yCompH, width, height);

      var cbCompW = componentData[1].WidthInBlocks * 8;
      var cbCompH = componentData[1].HeightInBlocks * 8;
      var cbActualW = (width * frame.Components[1].HSamplingFactor + maxH - 1) / maxH;
      var cbActualH = (height * frame.Components[1].VSamplingFactor + maxV - 1) / maxV;
      var cbPlane = _CropPlane(planes[1], cbCompW, cbCompH, cbActualW, cbActualH);

      var crCompW = componentData[2].WidthInBlocks * 8;
      var crCompH = componentData[2].HeightInBlocks * 8;
      var crActualW = (width * frame.Components[2].HSamplingFactor + maxH - 1) / maxH;
      var crActualH = (height * frame.Components[2].VSamplingFactor + maxV - 1) / maxV;
      var crPlane = _CropPlane(planes[2], crCompW, crCompH, crActualW, crActualH);

      // Upsample chroma to full image dimensions.
      cbPlane = JpegColorConverter.Upsample(cbPlane, cbActualW, cbActualH, width, height);
      crPlane = JpegColorConverter.Upsample(crPlane, crActualW, crActualH, width, height);

      rgbPixelData = JpegColorConverter.YCbCrToRgb(yPlane, cbPlane, crPlane, width, height);
    }

    return new JpegFile {
      Width = width,
      Height = height,
      IsGrayscale = isGrayscale,
      RgbPixelData = rgbPixelData,
      RawJpegBytes = dataArray
    };
  }

  /// <summary>Decodes a JPEG to the coefficient level (no IDCT / color conversion).
  /// Used by lossless transcode to read coefficients and re-emit them.</summary>
  internal static JpegImage DecodeToCoefficients(byte[] data) {
    var image = JpegMarkerParser.ParseAllMarkers(data);
    var frame = image.Frame;

    if (frame.Width == 0 || frame.Height == 0)
      throw new InvalidDataException("JPEG frame has zero dimensions.");

    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    var componentData = new JpegComponentData[frame.Components.Length];
    for (var ci = 0; ci < frame.Components.Length; ++ci) {
      var comp = frame.Components[ci];
      var mcuCols = (frame.Width + maxH * 8 - 1) / (maxH * 8);
      var mcuRows = (frame.Height + maxV * 8 - 1) / (maxV * 8);
      var widthInBlocks = mcuCols * comp.HSamplingFactor;
      var heightInBlocks = mcuRows * comp.VSamplingFactor;
      componentData[ci] = JpegComponentData.Allocate(widthInBlocks, heightInBlocks);
    }

    image.ComponentData = componentData;
    _DecodeScanData(data, frame, image, componentData);
    return image;
  }

  /// <summary>Walks through the raw JPEG data finding SOS markers and dispatching to
  /// the appropriate entropy decoder (baseline or progressive).</summary>
  private static void _DecodeScanData(
    byte[] data,
    JpegFrameHeader frame,
    JpegImage image,
    JpegComponentData[] componentData
  ) {
    var pos = 2; // Skip SOI

    while (pos < data.Length - 1) {
      if (data[pos] != 0xFF) {
        ++pos;
        continue;
      }

      // Skip fill bytes
      while (pos < data.Length - 1 && data[pos + 1] == 0xFF)
        ++pos;

      if (pos >= data.Length - 1)
        break;

      var marker = data[pos + 1];
      pos += 2;

      if (marker == JpegMarker.EOI)
        break;

      if (marker == JpegMarker.SOI || JpegMarker.IsRst(marker) || marker == 0x00)
        continue;

      if (pos + 1 >= data.Length)
        break;

      var segLen = (data[pos] << 8) | data[pos + 1];
      var segData = pos + 2;

      if (marker == JpegMarker.SOS) {
        // Parse the SOS header
        var scanHeader = JpegMarkerParser.ParseSos(data, segData);

        // Find the start of entropy data (right after the SOS segment)
        var entropyStart = JpegMarkerParser.FindSosData(data, pos);

        // Decode entropy data
        if (frame.IsProgressive)
          JpegProgressiveDecoder.DecodeScan(
            data, entropyStart, frame, scanHeader,
            image.DcHuffmanTables, image.AcHuffmanTables,
            componentData, image.RestartInterval);
        else
          JpegBaselineDecoder.Decode(
            data, entropyStart, frame, scanHeader,
            image.DcHuffmanTables, image.AcHuffmanTables,
            componentData, image.RestartInterval);

        // Skip past the entropy data to find the next marker
        var entropyEnd = JpegMarkerParser.FindEntropyEnd(data, entropyStart);
        pos = entropyEnd;
        continue;
      }

      // Handle DHT markers that may appear between SOS segments (progressive JPEGs
      // often interleave DHT markers between scans)
      if (marker == JpegMarker.DHT) {
        JpegMarkerParser._ParseDhtDirect(data, segData, segLen - 2, image.DcHuffmanTables, image.AcHuffmanTables);
      }

      pos += segLen;
    }
  }

  /// <summary>Runs inverse DCT on every block of every component, producing one spatial byte plane per component.</summary>
  private static byte[][] _InverseDctAllComponents(
    JpegFrameHeader frame,
    JpegQuantTable[] quantTables,
    JpegComponentData[] componentData,
    int maxH,
    int maxV
  ) {
    var planes = new byte[frame.Components.Length][];

    for (var ci = 0; ci < frame.Components.Length; ++ci) {
      var comp = frame.Components[ci];
      var compData = componentData[ci];
      var qt = quantTables[comp.QuantTableId];
      if (qt == null)
        throw new InvalidDataException($"Missing quantization table {comp.QuantTableId} for component {comp.Id}.");

      var planeWidth = compData.WidthInBlocks * 8;
      var planeHeight = compData.HeightInBlocks * 8;
      var plane = new byte[planeWidth * planeHeight];

      for (var by = 0; by < compData.HeightInBlocks; ++by)
        for (var bx = 0; bx < compData.WidthInBlocks; ++bx) {
          var block = compData.Blocks[by][bx];
          JpegDct.InverseDct(block.Coefficients, qt.Values, plane, by * 8 * planeWidth + bx * 8, planeWidth);
        }

      planes[ci] = plane;
    }

    return planes;
  }

  /// <summary>Crops a plane to the specified target dimensions (removes MCU-edge padding).</summary>
  private static byte[] _CropPlane(byte[] plane, int planeWidth, int planeHeight, int targetWidth, int targetHeight) {
    if (planeWidth == targetWidth && planeHeight == targetHeight)
      return plane;

    var cropped = new byte[targetWidth * targetHeight];
    var copyWidth = Math.Min(planeWidth, targetWidth);
    var copyHeight = Math.Min(planeHeight, targetHeight);

    for (var y = 0; y < copyHeight; ++y)
      Array.Copy(plane, y * planeWidth, cropped, y * targetWidth, copyWidth);

    return cropped;
  }
}
