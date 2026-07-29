using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Encodes coefficient blocks into progressive JPEG multi-SOS entropy data.</summary>
internal static class JpegProgressiveEncoder {

  /// <summary>Encodes a single progressive scan (DC or AC, first or refine).</summary>
  public static void EncodeScan(
    MemoryStream stream,
    JpegFrameHeader frame,
    JpegScanHeader scan,
    JpegHuffmanTable[] dcTables,
    JpegHuffmanTable[] acTables,
    JpegComponentData[] componentData,
    int restartInterval
  ) {
    var isDcScan = scan.SpectralStart == 0;
    var isFirstPass = scan.SuccessiveApproxHigh == 0;

    if (isDcScan) {
      if (isFirstPass)
        _EncodeDcFirst(stream, frame, scan, dcTables, componentData, restartInterval);
      else
        _EncodeDcRefine(stream, frame, scan, componentData, restartInterval);
    } else {
      if (isFirstPass)
        _EncodeAcFirst(stream, frame, scan, acTables, componentData, restartInterval);
      else
        _EncodeAcRefine(stream, frame, scan, acTables, componentData, restartInterval);
    }
  }

  private static void _EncodeDcFirst(
    MemoryStream stream, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] dcTables, JpegComponentData[] componentData, int restartInterval
  ) {
    var writer = new JpegBitWriter(stream);
    var dcPred = new int[frame.Components.Length];
    var al = scan.SuccessiveApproxLow;

    _ForEachMcuInterleaved(frame, scan, restartInterval, writer, stream, dcPred,
      (compIdx, scanComp, bx, by) => {
        var block = componentData[compIdx].Blocks[by][bx];
        var dcVal = block.Coefficients[0] >> al;
        var dcDiff = dcVal - dcPred[compIdx];
        dcPred[compIdx] = dcVal;
        JpegHuffmanEncoder.EncodeDc(writer, dcTables[scanComp.DcTableId], dcDiff);
      });

    writer.FlushBits();
  }

  private static void _EncodeDcRefine(
    MemoryStream stream, JpegFrameHeader frame, JpegScanHeader scan,
    JpegComponentData[] componentData, int restartInterval
  ) {
    var writer = new JpegBitWriter(stream);
    var dcPred = new int[frame.Components.Length];
    var al = scan.SuccessiveApproxLow;

    _ForEachMcuInterleaved(frame, scan, restartInterval, writer, stream, dcPred,
      (compIdx, scanComp, bx, by) => {
        var block = componentData[compIdx].Blocks[by][bx];
        writer.WriteBits((block.Coefficients[0] >> al) & 1, 1);
      });

    writer.FlushBits();
  }

  private static void _EncodeAcFirst(
    MemoryStream stream, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] acTables, JpegComponentData[] componentData, int restartInterval
  ) {
    if (scan.Components.Length != 1)
      return;

    var writer = new JpegBitWriter(stream);
    var scanComp = scan.Components[0];
    var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
    if (compIdx < 0)
      return;

    var acTable = acTables[scanComp.AcTableId];
    var compData = componentData[compIdx];
    var al = scan.SuccessiveApproxLow;
    var ss = scan.SpectralStart;
    var se = scan.SpectralEnd;

    var rstCounter = 0;
    var mcuCount = 0;

    for (var by = 0; by < compData.HeightInBlocks; ++by)
      for (var bx = 0; bx < compData.WidthInBlocks; ++bx) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          writer.FlushBits();
          JpegMarkerWriter.WriteMarker(stream, (byte)(JpegMarker.RST0 + (rstCounter & 7)));
          ++rstCounter;
        }

        var block = compData.Blocks[by][bx];
        var zeroRun = 0;

        for (var k = ss; k <= se; ++k) {
          var value = block.Coefficients[k] >> al;
          if (value == 0) {
            ++zeroRun;
            continue;
          }

          while (zeroRun > 15) {
            writer.WriteHuffmanCode(acTable.EhufCo[0xF0], acTable.EhufSi[0xF0]);
            zeroRun -= 16;
          }

          var absVal = value < 0 ? -value : value;
          var category = 0;
          var tmp = absVal;
          while (tmp > 0) {
            ++category;
            tmp >>= 1;
          }

          var rs = (zeroRun << 4) | category;
          writer.WriteHuffmanCode(acTable.EhufCo[rs], acTable.EhufSi[rs]);
          var encValue = value >= 0 ? value : value + (1 << category) - 1;
          writer.WriteBits(encValue, category);
          zeroRun = 0;
        }

        if (zeroRun > 0)
          writer.WriteHuffmanCode(acTable.EhufCo[0x00], acTable.EhufSi[0x00]);

        ++mcuCount;
      }

    writer.FlushBits();
  }

  private static void _EncodeAcRefine(
    MemoryStream stream, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] acTables, JpegComponentData[] componentData, int restartInterval
  ) {
    if (scan.Components.Length != 1)
      return;

    var writer = new JpegBitWriter(stream);
    var scanComp = scan.Components[0];
    var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
    if (compIdx < 0)
      return;

    var acTable = acTables[scanComp.AcTableId];
    var compData = componentData[compIdx];
    var al = scan.SuccessiveApproxLow;
    var ss = scan.SpectralStart;
    var se = scan.SpectralEnd;

    var rstCounter = 0;
    var mcuCount = 0;

    for (var by = 0; by < compData.HeightInBlocks; ++by)
      for (var bx = 0; bx < compData.WidthInBlocks; ++bx) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          writer.FlushBits();
          JpegMarkerWriter.WriteMarker(stream, (byte)(JpegMarker.RST0 + (rstCounter & 7)));
          ++rstCounter;
        }

        var block = compData.Blocks[by][bx];
        var zeroRun = 0;
        var pendingBits = new System.Collections.Generic.List<int>();

        for (var k = ss; k <= se; ++k) {
          var absCoeff = block.Coefficients[k] < 0 ? -block.Coefficients[k] : block.Coefficients[k];
          var isBitSet = (absCoeff >> al) & 1;

          if (block.Coefficients[k] == 0 || (absCoeff >> (al + 1)) != 0) {
            // Zero or previously non-zero
            if (block.Coefficients[k] == 0) {
              ++zeroRun;
            } else {
              // Previously non-zero: add correction bit
              pendingBits.Add(isBitSet);
            }
            continue;
          }

          // Newly non-zero coefficient
          while (zeroRun > 15) {
            writer.WriteHuffmanCode(acTable.EhufCo[0xF0], acTable.EhufSi[0xF0]);
            foreach (var bit in pendingBits)
              writer.WriteBits(bit, 1);
            pendingBits.Clear();
            zeroRun -= 16;
          }

          var rs = (zeroRun << 4) | 1;
          writer.WriteHuffmanCode(acTable.EhufCo[rs], acTable.EhufSi[rs]);
          writer.WriteBits(block.Coefficients[k] > 0 ? 1 : 0, 1);
          foreach (var bit in pendingBits)
            writer.WriteBits(bit, 1);
          pendingBits.Clear();
          zeroRun = 0;
        }

        if (zeroRun > 0 || pendingBits.Count > 0) {
          writer.WriteHuffmanCode(acTable.EhufCo[0x00], acTable.EhufSi[0x00]);
          foreach (var bit in pendingBits)
            writer.WriteBits(bit, 1);
        }

        ++mcuCount;
      }

    writer.FlushBits();
  }

  private static void _ForEachMcuInterleaved(
    JpegFrameHeader frame, JpegScanHeader scan, int restartInterval,
    JpegBitWriter writer, MemoryStream stream, int[] dcPred,
    Action<int, (byte ComponentId, byte DcTableId, byte AcTableId), int, int> blockAction
  ) {
    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    var mcuCols = (frame.Width + maxH * 8 - 1) / (maxH * 8);
    var mcuRows = (frame.Height + maxV * 8 - 1) / (maxV * 8);
    var rstCounter = 0;
    var mcuCount = 0;

    for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
      for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          writer.FlushBits();
          JpegMarkerWriter.WriteMarker(stream, (byte)(JpegMarker.RST0 + (rstCounter & 7)));
          ++rstCounter;
          Array.Clear(dcPred);
        }

        for (var ci = 0; ci < scan.Components.Length; ++ci) {
          var scanComp = scan.Components[ci];
          var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
          if (compIdx < 0)
            continue;
          var comp = frame.Components[compIdx];

          for (var v = 0; v < comp.VSamplingFactor; ++v)
            for (var h = 0; h < comp.HSamplingFactor; ++h) {
              var bx = mcuCol * comp.HSamplingFactor + h;
              var by = mcuRow * comp.VSamplingFactor + v;
              blockAction(compIdx, scanComp, bx, by);
            }
        }

        ++mcuCount;
      }
  }

  private static int _FindComponent(JpegComponentInfo[] components, byte id) {
    for (var i = 0; i < components.Length; ++i)
      if (components[i].Id == id)
        return i;
    return -1;
  }
}
