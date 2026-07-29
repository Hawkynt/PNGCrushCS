using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Extracts DCT coefficients from JPEG byte stream without performing IDCT.
/// Key component for lossless transcoding.</summary>
internal static class JpegCoefficientExtractor {

  /// <summary>Parses a JPEG file and extracts all coefficients at DCT level.</summary>
  public static JpegImage Extract(byte[] data) {
    if (data.Length < 2 || data[0] != 0xFF || data[1] != JpegMarker.SOI)
      throw new InvalidDataException("Invalid JPEG signature.");

    var image = JpegMarkerParser.ParseAllMarkers(data);
    var frame = image.Frame;

    if (frame.Components.Length == 0)
      throw new InvalidDataException("No SOF marker found in JPEG.");

    // Allocate component data
    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    image.ComponentData = new JpegComponentData[frame.Components.Length];
    for (var i = 0; i < frame.Components.Length; ++i) {
      var comp = frame.Components[i];
      var widthInBlocks = ((frame.Width * comp.HSamplingFactor + maxH - 1) / maxH + 7) / 8;
      var heightInBlocks = ((frame.Height * comp.VSamplingFactor + maxV - 1) / maxV + 7) / 8;

      // For interleaved scans, round up to MCU boundaries
      var mcuCols = (frame.Width + maxH * 8 - 1) / (maxH * 8);
      var mcuRows = (frame.Height + maxV * 8 - 1) / (maxV * 8);
      widthInBlocks = Math.Max(widthInBlocks, mcuCols * comp.HSamplingFactor);
      heightInBlocks = Math.Max(heightInBlocks, mcuRows * comp.VSamplingFactor);

      image.ComponentData[i] = JpegComponentData.Allocate(widthInBlocks, heightInBlocks);
    }

    // Walk through the data again to find and decode SOS segments
    _DecodeSosSegments(data, image);

    return image;
  }

  private static void _DecodeSosSegments(byte[] data, JpegImage image) {
    var pos = 2; // Skip SOI

    while (pos < data.Length - 1) {
      if (data[pos] != 0xFF) {
        ++pos;
        continue;
      }

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

      if (marker == JpegMarker.DHT) {
        // Update Huffman tables — progressive JPEGs have DHT between SOS markers
        _ParseDhtSegment(data, pos + 2, segLen - 2, image.DcHuffmanTables, image.AcHuffmanTables);
        pos += segLen;
        continue;
      }

      if (marker == JpegMarker.DRI) {
        image.RestartInterval = (data[pos + 2] << 8) | data[pos + 3];
        pos += segLen;
        continue;
      }

      if (marker == JpegMarker.SOS) {
        var scan = JpegMarkerParser.ParseSos(data, pos + 2);
        var entropyStart = pos + segLen;
        var entropyEnd = JpegMarkerParser.FindEntropyEnd(data, entropyStart);

        if (image.Frame.IsProgressive)
          JpegProgressiveDecoder.DecodeScan(
            data, entropyStart, image.Frame, scan,
            image.DcHuffmanTables, image.AcHuffmanTables,
            image.ComponentData, image.RestartInterval
          );
        else
          JpegBaselineDecoder.Decode(
            data, entropyStart, image.Frame, scan,
            image.DcHuffmanTables, image.AcHuffmanTables,
            image.ComponentData, image.RestartInterval
          );

        pos = entropyEnd;
        continue;
      }

      pos += segLen;
    }
  }

  private static void _ParseDhtSegment(byte[] data, int offset, int length, JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables) {
    var end = offset + length;
    while (offset < end) {
      var tcth = data[offset++];
      var tc = tcth >> 4;
      var th = tcth & 0x0F;

      var bits = new byte[16];
      var totalSymbols = 0;
      for (var i = 0; i < 16; ++i) {
        bits[i] = data[offset++];
        totalSymbols += bits[i];
      }

      var values = new byte[totalSymbols];
      System.Array.Copy(data, offset, values, 0, totalSymbols);
      offset += totalSymbols;

      var table = new JpegHuffmanTable { Bits = bits, Values = values };
      table.BuildTables();

      if (th < 4) {
        if (tc == 0)
          dcTables[th] = table;
        else
          acTables[th] = table;
      }
    }
  }
}
