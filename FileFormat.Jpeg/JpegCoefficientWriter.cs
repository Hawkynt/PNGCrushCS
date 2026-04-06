using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Writes a JpegImage (coefficient-level) to JPEG byte stream.
/// Supports optional two-pass optimal Huffman coding and baseline/progressive modes.</summary>
internal static class JpegCoefficientWriter {

  /// <summary>Writes a complete JPEG file from coefficients.</summary>
  public static byte[] Write(JpegImage image, JpegMode mode, bool optimizeHuffman, bool stripMetadata) {
    using var stream = new MemoryStream();

    JpegMarkerWriter.WriteSoi(stream);

    // Write APP0 JFIF header
    JpegMarkerWriter.WriteApp0Jfif(stream);

    // Write preserved markers (unless stripping)
    if (!stripMetadata)
      foreach (var seg in image.MarkerSegments)
        JpegMarkerWriter.WriteMarkerSegment(stream, seg);

    // Write quantization tables
    foreach (var qt in image.QuantTables) {
      if (qt == null)
        continue;
      JpegMarkerWriter.WriteDqt(stream, qt.TableId, qt.Values, qt.Is16Bit);
    }

    // Determine SOF marker
    var sofMarker = mode == JpegMode.Progressive ? JpegMarker.SOF2 : JpegMarker.SOF0;
    JpegMarkerWriter.WriteSof(stream, sofMarker, image.Frame);

    if (mode == JpegMode.Progressive)
      _WriteProgressive(stream, image, optimizeHuffman);
    else
      _WriteBaseline(stream, image, optimizeHuffman);

    JpegMarkerWriter.WriteEoi(stream);
    return stream.ToArray();
  }

  private static void _WriteBaseline(MemoryStream stream, JpegImage image, bool optimizeHuffman) {
    var frame = image.Frame;
    var numComponents = frame.Components.Length;

    // Build scan header for all components
    var scanComponents = new (byte ComponentId, byte DcTableId, byte AcTableId)[numComponents];
    for (var i = 0; i < numComponents; ++i) {
      var dcId = (byte)(i == 0 ? 0 : 1);
      var acId = (byte)(i == 0 ? 0 : 1);
      scanComponents[i] = (frame.Components[i].Id, dcId, acId);
    }

    var scan = new JpegScanHeader {
      Components = scanComponents,
      SpectralStart = 0,
      SpectralEnd = 63,
      SuccessiveApproxHigh = 0,
      SuccessiveApproxLow = 0
    };

    // Build or optimize Huffman tables
    var dcTables = new JpegHuffmanTable[4];
    var acTables = new JpegHuffmanTable[4];

    if (optimizeHuffman) {
      _BuildOptimalTables(frame, scan, image.ComponentData, dcTables, acTables, image.RestartInterval);
    } else {
      // Use standard tables
      dcTables[0] = _MakeTable(JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues);
      dcTables[1] = _MakeTable(JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues);
      acTables[0] = _MakeTable(JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues);
      acTables[1] = _MakeTable(JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues);
    }

    // Write Huffman tables
    _WriteDhtTables(stream, dcTables, acTables, scan);

    // Write DRI if needed
    if (image.RestartInterval > 0)
      JpegMarkerWriter.WriteDri(stream, image.RestartInterval);

    // Write SOS + entropy data
    JpegMarkerWriter.WriteSos(stream, scan);
    JpegBaselineEncoder.Encode(stream, frame, scan, dcTables, acTables, image.ComponentData, image.RestartInterval);
  }

  private static void _WriteProgressive(MemoryStream stream, JpegImage image, bool optimizeHuffman) {
    var frame = image.Frame;
    var isGrayscale = frame.Components.Length == 1;
    var scanScript = isGrayscale
      ? JpegStandardTables.ProgressiveScanScriptGrayscale
      : JpegStandardTables.ProgressiveScanScript;

    // For progressive, we need multiple scans
    // First, build tables for all scans
    var dcTables = new JpegHuffmanTable[4];
    var acTables = new JpegHuffmanTable[4];

    if (!optimizeHuffman) {
      dcTables[0] = _MakeTable(JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues);
      dcTables[1] = _MakeTable(JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues);
      acTables[0] = _MakeTable(JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues);
      acTables[1] = _MakeTable(JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues);
    }

    if (image.RestartInterval > 0)
      JpegMarkerWriter.WriteDri(stream, image.RestartInterval);

    foreach (var (ss, se, ah, al, components) in scanScript) {
      // Skip scans referencing components that don't exist
      var validComponents = true;
      foreach (var ci in components)
        if (ci >= frame.Components.Length) {
          validComponents = false;
          break;
        }

      if (!validComponents)
        continue;

      var scanComps = new (byte ComponentId, byte DcTableId, byte AcTableId)[components.Length];
      for (var i = 0; i < components.Length; ++i) {
        var ci = components[i];
        var dcId = (byte)(ci == 0 ? 0 : 1);
        var acId = (byte)(ci == 0 ? 0 : 1);
        scanComps[i] = (frame.Components[ci].Id, dcId, acId);
      }

      var scan = new JpegScanHeader {
        Components = scanComps,
        SpectralStart = (byte)ss,
        SpectralEnd = (byte)se,
        SuccessiveApproxHigh = (byte)ah,
        SuccessiveApproxLow = (byte)al
      };

      if (optimizeHuffman) {
        // Build per-scan optimal tables
        dcTables = new JpegHuffmanTable[4];
        acTables = new JpegHuffmanTable[4];
        _BuildOptimalTablesForScan(frame, scan, image.ComponentData, dcTables, acTables, image.RestartInterval);
      }

      // Write Huffman tables needed for this scan
      _WriteDhtTables(stream, dcTables, acTables, scan);

      // Write SOS + entropy
      JpegMarkerWriter.WriteSos(stream, scan);
      JpegProgressiveEncoder.EncodeScan(stream, frame, scan, dcTables, acTables, image.ComponentData, image.RestartInterval);
    }
  }

  private static void _BuildOptimalTables(
    JpegFrameHeader frame, JpegScanHeader scan, JpegComponentData[] componentData,
    JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables, int restartInterval
  ) {
    var dcFreqs = new long[4][];
    var acFreqs = new long[4][];
    for (var i = 0; i < 4; ++i) {
      dcFreqs[i] = new long[256];
      acFreqs[i] = new long[256];
    }

    JpegBaselineEncoder.CountFrequencies(frame, scan, componentData, dcFreqs, acFreqs, restartInterval);

    for (var i = 0; i < 4; ++i) {
      if (_HasFrequencies(dcFreqs[i]))
        dcTables[i] = JpegHuffmanTable.FromFrequencies(dcFreqs[i], 15);
      if (_HasFrequencies(acFreqs[i]))
        acTables[i] = JpegHuffmanTable.FromFrequencies(acFreqs[i], 255);
    }

    // Ensure at least standard tables exist
    dcTables[0] ??= _MakeTable(JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues);
    dcTables[1] ??= _MakeTable(JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues);
    acTables[0] ??= _MakeTable(JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues);
    acTables[1] ??= _MakeTable(JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues);
  }

  private static void _BuildOptimalTablesForScan(
    JpegFrameHeader frame, JpegScanHeader scan, JpegComponentData[] componentData,
    JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables, int restartInterval
  ) {
    // For progressive scans, we count frequencies specifically for this scan's spectral range
    // and build tables only for the referenced table IDs
    var dcFreqs = new long[4][];
    var acFreqs = new long[4][];
    for (var i = 0; i < 4; ++i) {
      dcFreqs[i] = new long[256];
      acFreqs[i] = new long[256];
    }

    // Simple approach: count from the coefficient data for this scan's spectral range
    foreach (var scanComp in scan.Components) {
      var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
      if (compIdx < 0)
        continue;

      var compData = componentData[compIdx];
      var al = scan.SuccessiveApproxLow;

      if (scan.SpectralStart == 0 && scan.SuccessiveApproxHigh == 0) {
        // DC first pass
        var dcPred = 0;
        for (var by = 0; by < compData.HeightInBlocks; ++by)
          for (var bx = 0; bx < compData.WidthInBlocks; ++bx) {
            var dcVal = compData.Blocks[by][bx].Coefficients[0] >> al;
            JpegHuffmanEncoder.CountDcFrequencies(dcFreqs[scanComp.DcTableId], dcVal - dcPred);
            dcPred = dcVal;
          }
      } else if (scan.SpectralStart > 0 && scan.SuccessiveApproxHigh == 0) {
        // AC first pass
        for (var by = 0; by < compData.HeightInBlocks; ++by)
          for (var bx = 0; bx < compData.WidthInBlocks; ++bx) {
            var block = compData.Blocks[by][bx];
            var shifted = new short[64];
            for (var i = 0; i < 64; ++i)
              shifted[i] = (short)(block.Coefficients[i] >> al);
            JpegHuffmanEncoder.CountAcFrequencies(acFreqs[scanComp.AcTableId], shifted, scan.SpectralStart, scan.SpectralEnd);
          }
      }
    }

    for (var i = 0; i < 4; ++i) {
      if (_HasFrequencies(dcFreqs[i]))
        dcTables[i] = JpegHuffmanTable.FromFrequencies(dcFreqs[i], 15);
      if (_HasFrequencies(acFreqs[i]))
        acTables[i] = JpegHuffmanTable.FromFrequencies(acFreqs[i], 255);
    }

    // Fallback to standard tables
    dcTables[0] ??= _MakeTable(JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues);
    dcTables[1] ??= _MakeTable(JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues);
    acTables[0] ??= _MakeTable(JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues);
    acTables[1] ??= _MakeTable(JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues);
  }

  private static void _WriteDhtTables(MemoryStream stream, JpegHuffmanTable[] dcTables, JpegHuffmanTable[] acTables, JpegScanHeader scan) {
    // Only write tables referenced by this scan
    var writtenDc = new bool[4];
    var writtenAc = new bool[4];

    foreach (var comp in scan.Components) {
      if (!writtenDc[comp.DcTableId] && dcTables[comp.DcTableId] != null) {
        JpegMarkerWriter.WriteDht(stream, 0, comp.DcTableId, dcTables[comp.DcTableId]);
        writtenDc[comp.DcTableId] = true;
      }

      if (!writtenAc[comp.AcTableId] && acTables[comp.AcTableId] != null && scan.SpectralEnd > 0) {
        JpegMarkerWriter.WriteDht(stream, 1, comp.AcTableId, acTables[comp.AcTableId]);
        writtenAc[comp.AcTableId] = true;
      }
    }
  }

  private static bool _HasFrequencies(long[] freq) {
    foreach (var f in freq)
      if (f > 0)
        return true;
    return false;
  }

  private static JpegHuffmanTable _MakeTable(byte[] bits, byte[] values) {
    var table = new JpegHuffmanTable { Bits = (byte[])bits.Clone(), Values = (byte[])values.Clone() };
    table.BuildTables();
    return table;
  }

  private static int _FindComponent(JpegComponentInfo[] components, byte id) {
    for (var i = 0; i < components.Length; ++i)
      if (components[i].Id == id)
        return i;
    return -1;
  }
}
