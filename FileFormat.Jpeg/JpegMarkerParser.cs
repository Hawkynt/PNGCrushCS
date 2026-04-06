using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Stateless parser for JPEG marker segments: SOF, SOS, DQT, DHT, DRI.</summary>
internal static class JpegMarkerParser {

  public static JpegFrameHeader ParseSof(byte[] data, int offset, int length, byte sofMarker) {
    var precision = data[offset];
    var height = (data[offset + 1] << 8) | data[offset + 2];
    var width = (data[offset + 3] << 8) | data[offset + 4];
    var numComponents = data[offset + 5];

    var components = new JpegComponentInfo[numComponents];
    for (var i = 0; i < numComponents; ++i) {
      var idx = offset + 6 + i * 3;
      var hv = data[idx + 1];
      components[i] = new() {
        Id = data[idx],
        HSamplingFactor = (byte)(hv >> 4),
        VSamplingFactor = (byte)(hv & 0x0F),
        QuantTableId = data[idx + 2]
      };
    }

    return new() {
      Precision = precision,
      Width = width,
      Height = height,
      Components = components,
      IsProgressive = sofMarker == JpegMarker.SOF2
    };
  }

  public static JpegScanHeader ParseSos(byte[] data, int offset) {
    var numComponents = data[offset];
    var components = new (byte ComponentId, byte DcTableId, byte AcTableId)[numComponents];

    for (var i = 0; i < numComponents; ++i) {
      var idx = offset + 1 + i * 2;
      var tables = data[idx + 1];
      components[i] = (data[idx], (byte)(tables >> 4), (byte)(tables & 0x0F));
    }

    var paramOffset = offset + 1 + numComponents * 2;
    return new() {
      Components = components,
      SpectralStart = data[paramOffset],
      SpectralEnd = data[paramOffset + 1],
      SuccessiveApproxHigh = (byte)(data[paramOffset + 2] >> 4),
      SuccessiveApproxLow = (byte)(data[paramOffset + 2] & 0x0F)
    };
  }

  public static JpegQuantTable[] ParseDqt(byte[] data, int offset, int length) {
    var tables = new List<JpegQuantTable>();
    var end = offset + length;

    while (offset < end) {
      var pq = data[offset] >> 4;   // Precision: 0=8-bit, 1=16-bit
      var tq = data[offset] & 0x0F; // Table ID
      ++offset;

      var values = new int[64];
      if (pq == 0) {
        for (var i = 0; i < 64; ++i)
          values[i] = data[offset++];
      } else {
        for (var i = 0; i < 64; ++i) {
          values[i] = (data[offset] << 8) | data[offset + 1];
          offset += 2;
        }
      }

      tables.Add(new() { TableId = tq, Values = values, Is16Bit = pq == 1 });
    }

    return tables.ToArray();
  }

  public static JpegHuffmanTable[] ParseDht(byte[] data, int offset, int length) {
    var tables = new List<JpegHuffmanTable>();
    var end = offset + length;

    while (offset < end) {
      var tcth = data[offset++];
      // tc = tcth >> 4 (0=DC, 1=AC), th = tcth & 0x0F (table index)

      var bits = new byte[16];
      var totalSymbols = 0;
      for (var i = 0; i < 16; ++i) {
        bits[i] = data[offset++];
        totalSymbols += bits[i];
      }

      var values = new byte[totalSymbols];
      Array.Copy(data, offset, values, 0, totalSymbols);
      offset += totalSymbols;

      var table = new JpegHuffmanTable { Bits = bits, Values = values };
      table.BuildTables();

      // Store table class + index in a way the caller can differentiate
      // We'll use the convention: DC tables first, then AC tables
      tables.Add(table);
    }

    return tables.ToArray();
  }

  /// <summary>Scans through JPEG data, parsing all markers and returning structured data.</summary>
  public static JpegImage ParseAllMarkers(byte[] data) {
    if (data.Length < 2 || data[0] != 0xFF || data[1] != JpegMarker.SOI)
      throw new InvalidDataException("Invalid JPEG signature.");

    var image = new JpegImage();
    var quantTables = new JpegQuantTable[4];
    var dcTables = new JpegHuffmanTable[4];
    var acTables = new JpegHuffmanTable[4];
    var pos = 2;

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

      // Markers with length
      if (pos + 1 >= data.Length)
        break;

      var segLen = (data[pos] << 8) | data[pos + 1];
      var segData = pos + 2;
      var segEnd = pos + segLen;

      switch (marker) {
        case JpegMarker.SOF0:
        case JpegMarker.SOF1:
        case JpegMarker.SOF2:
          image = new() {
            Frame = ParseSof(data, segData, segLen - 2, marker),
            MarkerSegments = image.MarkerSegments,
            RestartInterval = image.RestartInterval
          };
          break;

        case JpegMarker.DQT:
          foreach (var qt in ParseDqt(data, segData, segLen - 2))
            if (qt.TableId is >= 0 and < 4)
              quantTables[qt.TableId] = qt;
          break;

        case JpegMarker.DHT: {
          var parsed = ParseDht(data, segData, segLen - 2);
          // Re-parse to get tc/th for each table
          var dhtPos = segData;
          foreach (var ht in parsed) {
            var tcth = data[dhtPos];
            var tc = tcth >> 4;
            var th = tcth & 0x0F;
            if (th < 4) {
              if (tc == 0)
                dcTables[th] = ht;
              else
                acTables[th] = ht;
            }

            dhtPos += 1 + 16;
            for (var i = 0; i < 16; ++i)
              dhtPos += ht.Bits[i];
            // Move past the values we already counted
            dhtPos = dhtPos - 16;
            var valCount = 0;
            for (var i = 0; i < 16; ++i)
              valCount += ht.Bits[i];
            dhtPos = segData;
            // Recalculate properly
            break;
          }

          // Simpler approach: re-walk the DHT segment directly
          _ParseDhtDirect(data, segData, segLen - 2, dcTables, acTables);
          break;
        }

        case JpegMarker.DRI:
          image.RestartInterval = (data[segData] << 8) | data[segData + 1];
          break;

        case JpegMarker.SOS:
          // Don't parse here - caller handles SOS
          break;

        default:
          // APP and COM markers - preserve for lossless transcode
          if (JpegMarker.IsApp(marker) || marker == JpegMarker.COM) {
            var markerData = new byte[segLen - 2];
            Array.Copy(data, segData, markerData, 0, segLen - 2);
            image.MarkerSegments.Add(new() { Marker = marker, Data = markerData });
          }

          break;
      }

      pos = segEnd;
    }

    image.QuantTables = quantTables;
    image.DcHuffmanTables = dcTables;
    image.AcHuffmanTables = acTables;
    return image;
  }

  private static void _ParseDhtDirect(byte[] data, int offset, int length, JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables) {
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
      Array.Copy(data, offset, values, 0, totalSymbols);
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

  /// <summary>Finds the byte position immediately after the SOS marker segment (start of entropy data).</summary>
  public static int FindSosData(byte[] data, int sosMarkerPos) {
    if (sosMarkerPos + 2 >= data.Length)
      return data.Length;

    var segLen = (data[sosMarkerPos] << 8) | data[sosMarkerPos + 1];
    return sosMarkerPos + segLen;
  }

  /// <summary>Finds the end of entropy-coded data (next 0xFF marker that isn't 0x00 stuffed or RST).</summary>
  public static int FindEntropyEnd(byte[] data, int start) {
    var pos = start;
    while (pos < data.Length - 1) {
      if (data[pos] != 0xFF) {
        ++pos;
        continue;
      }

      var next = data[pos + 1];
      if (next == 0x00) {
        pos += 2; // Stuffed byte
        continue;
      }

      if (JpegMarker.IsRst(next)) {
        pos += 2; // Restart marker
        continue;
      }

      if (next == 0xFF) {
        ++pos; // Fill byte
        continue;
      }

      return pos; // Found a real marker
    }

    return data.Length;
  }
}
