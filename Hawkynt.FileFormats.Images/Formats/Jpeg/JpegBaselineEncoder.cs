using System;
using System.IO;

namespace FileFormat.Jpeg;

/// <summary>Encodes coefficient blocks into baseline JPEG entropy data.</summary>
internal static class JpegBaselineEncoder {

  /// <summary>Encodes all coefficient blocks as a single baseline scan.</summary>
  public static void Encode(
    MemoryStream stream,
    JpegFrameHeader frame,
    JpegScanHeader scan,
    JpegHuffmanTable[] dcTables,
    JpegHuffmanTable[] acTables,
    JpegComponentData[] componentData,
    int restartInterval
  ) {
    var writer = new JpegBitWriter(stream);
    var dcPred = new int[frame.Components.Length];

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
          var dcTable = dcTables[scanComp.DcTableId];
          var acTable = acTables[scanComp.AcTableId];
          var compData = componentData[compIdx];

          for (var v = 0; v < comp.VSamplingFactor; ++v)
            for (var h = 0; h < comp.HSamplingFactor; ++h) {
              var bx = mcuCol * comp.HSamplingFactor + h;
              var by = mcuRow * comp.VSamplingFactor + v;

              if (bx >= compData.WidthInBlocks || by >= compData.HeightInBlocks) {
                // Encode an empty block for padding
                JpegHuffmanEncoder.EncodeDc(writer, dcTable, -dcPred[compIdx]);
                dcPred[compIdx] = 0;
                writer.WriteHuffmanCode(acTable.EhufCo[0x00], acTable.EhufSi[0x00]); // EOB
                continue;
              }

              var block = compData.Blocks[by][bx];

              // Encode DC
              var dcDiff = block.Coefficients[0] - dcPred[compIdx];
              dcPred[compIdx] = block.Coefficients[0];
              JpegHuffmanEncoder.EncodeDc(writer, dcTable, dcDiff);

              // Encode AC
              JpegHuffmanEncoder.EncodeAcBlock(writer, acTable, block.Coefficients, 1, 63);
            }
        }

        ++mcuCount;
      }

    writer.FlushBits();
  }

  /// <summary>Counts symbol frequencies for two-pass optimal Huffman (pass 1).</summary>
  public static void CountFrequencies(
    JpegFrameHeader frame,
    JpegScanHeader scan,
    JpegComponentData[] componentData,
    long[][] dcFreqs,
    long[][] acFreqs,
    int restartInterval
  ) {
    var dcPred = new int[frame.Components.Length];

    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    var mcuCols = (frame.Width + maxH * 8 - 1) / (maxH * 8);
    var mcuRows = (frame.Height + maxV * 8 - 1) / (maxV * 8);
    var mcuCount = 0;

    for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
      for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0)
          Array.Clear(dcPred);

        for (var ci = 0; ci < scan.Components.Length; ++ci) {
          var scanComp = scan.Components[ci];
          var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
          if (compIdx < 0)
            continue;

          var comp = frame.Components[compIdx];
          var compData = componentData[compIdx];

          for (var v = 0; v < comp.VSamplingFactor; ++v)
            for (var h = 0; h < comp.HSamplingFactor; ++h) {
              var bx = mcuCol * comp.HSamplingFactor + h;
              var by = mcuRow * comp.VSamplingFactor + v;

              if (bx >= compData.WidthInBlocks || by >= compData.HeightInBlocks) {
                JpegHuffmanEncoder.CountDcFrequencies(dcFreqs[scanComp.DcTableId], -dcPred[compIdx]);
                dcPred[compIdx] = 0;
                ++acFreqs[scanComp.AcTableId][0x00]; // EOB
                continue;
              }

              var block = compData.Blocks[by][bx];

              var dcDiff = block.Coefficients[0] - dcPred[compIdx];
              dcPred[compIdx] = block.Coefficients[0];
              JpegHuffmanEncoder.CountDcFrequencies(dcFreqs[scanComp.DcTableId], dcDiff);
              JpegHuffmanEncoder.CountAcFrequencies(acFreqs[scanComp.AcTableId], block.Coefficients, 1, 63);
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
