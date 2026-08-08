using System;

namespace FileFormat.Jpeg;

/// <summary>Decodes baseline JPEG entropy data into coefficient blocks (MCU-by-MCU with RST handling).</summary>
internal static class JpegBaselineDecoder {

  public static void Decode(
    byte[] data,
    int entropyStart,
    JpegFrameHeader frame,
    JpegScanHeader scan,
    JpegHuffmanTable[] dcTables,
    JpegHuffmanTable[] acTables,
    JpegComponentData[] componentData,
    int restartInterval
  ) {
    var reader = new JpegBitReader(data, entropyStart);
    var dcPred = new int[frame.Components.Length];

    // Calculate MCU dimensions
    var maxH = 1;
    var maxV = 1;
    foreach (var comp in frame.Components) {
      if (comp.HSamplingFactor > maxH) maxH = comp.HSamplingFactor;
      if (comp.VSamplingFactor > maxV) maxV = comp.VSamplingFactor;
    }

    // A scan naming one component is not interleaved, and its minimum coded unit is a single block
    // rather than the frame's. The blocks then run in the component's own raster order over the
    // component's own grid — ceil(componentWidth/8) by ceil(componentHeight/8) — and the sampling
    // factors say nothing about how many are read.
    //
    // Reading such a scan on the interleaved grid took the sampling factors as a repeat count, so a
    // three-by-two picture had six blocks read where one was written and the bit reader was lost
    // from the first block onwards. It decoded to coloured hash that still filled the frame, which
    // is why nothing noticed: the only files here that use it are a camera's, and every ordinary
    // JPEG interleaves.
    if (scan.Components.Length == 1) {
      _DecodeSingleComponentScan(reader, frame, scan, dcTables, acTables, componentData, restartInterval, maxH, maxV);
      return;
    }

    var mcuWidth = maxH * 8;
    var mcuHeight = maxV * 8;
    var mcuCols = (frame.Width + mcuWidth - 1) / mcuWidth;
    var mcuRows = (frame.Height + mcuHeight - 1) / mcuHeight;

    var rstCounter = 0;
    var mcuCount = 0;

    for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
      for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
        // A restart interval says how often a marker MAY appear, not that one does. Both files in
        // the corpus that state an interval carry no restart markers at all, and this cleared the DC
        // predictors every interval regardless — so each picture was right down to its first
        // interval and wrong from there on, the predictors having been thrown away mid-stream.
        //
        // The predictors are now cleared only when a marker was actually stepped over, which is what
        // resynchronises them.
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          if (reader.TryConsumeRestart(rstCounter))
            Array.Clear(dcPred);

          ++rstCounter;
        }

        // Decode each component in the MCU
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
              var blockX = mcuCol * comp.HSamplingFactor + h;
              var blockY = mcuRow * comp.VSamplingFactor + v;

              if (blockX >= compData.WidthInBlocks || blockY >= compData.HeightInBlocks)
                continue;

              var block = compData.Blocks[blockY][blockX];

              // Decode DC
              var dcDiff = JpegHuffmanDecoder.DecodeDc(reader, dcTable);
              dcPred[compIdx] += dcDiff;
              block.Coefficients[0] = (short)dcPred[compIdx];

              // Decode AC
              JpegHuffmanDecoder.DecodeAcBlock(reader, acTable, block.Coefficients, 1, 63);
            }
        }

        ++mcuCount;
      }
  }

  /// <summary>Decodes a scan carrying one component, block by block over that component's grid.</summary>
  private static void _DecodeSingleComponentScan(
    JpegBitReader reader,
    JpegFrameHeader frame,
    JpegScanHeader scan,
    JpegHuffmanTable[] dcTables,
    JpegHuffmanTable[] acTables,
    JpegComponentData[] componentData,
    int restartInterval,
    int maxH,
    int maxV
  ) {
    var scanComp = scan.Components[0];
    var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
    if (compIdx < 0)
      return;

    var comp = frame.Components[compIdx];
    var compData = componentData[compIdx];
    var dcTable = dcTables[scanComp.DcTableId];
    var acTable = acTables[scanComp.AcTableId];

    // The component's own pixel size, and its grid rounded up from that. The store may be larger
    // where the interleaved grid needed padding blocks, but a non-interleaved scan does not carry
    // them, so they are left as they were allocated.
    var compWidth = (frame.Width * comp.HSamplingFactor + maxH - 1) / maxH;
    var compHeight = (frame.Height * comp.VSamplingFactor + maxV - 1) / maxV;
    var cols = Math.Min((compWidth + 7) / 8, compData.WidthInBlocks);
    var rows = Math.Min((compHeight + 7) / 8, compData.HeightInBlocks);

    var dcPred = 0;
    var rstCounter = 0;
    var unitCount = 0;

    for (var blockY = 0; blockY < rows; ++blockY)
      for (var blockX = 0; blockX < cols; ++blockX) {
        if (restartInterval > 0 && unitCount > 0 && unitCount % restartInterval == 0) {
          if (reader.TryConsumeRestart(rstCounter))
            dcPred = 0;

          ++rstCounter;
        }

        var block = compData.Blocks[blockY][blockX];
        dcPred += JpegHuffmanDecoder.DecodeDc(reader, dcTable);
        block.Coefficients[0] = (short)dcPred;
        JpegHuffmanDecoder.DecodeAcBlock(reader, acTable, block.Coefficients, 1, 63);

        ++unitCount;
      }
  }

  private static int _FindComponent(JpegComponentInfo[] components, byte id) {
    for (var i = 0; i < components.Length; ++i)
      if (components[i].Id == id)
        return i;
    return -1;
  }
}
