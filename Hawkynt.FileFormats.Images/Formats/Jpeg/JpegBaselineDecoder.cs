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

    var mcuWidth = maxH * 8;
    var mcuHeight = maxV * 8;
    var mcuCols = (frame.Width + mcuWidth - 1) / mcuWidth;
    var mcuRows = (frame.Height + mcuHeight - 1) / mcuHeight;
    var totalMcus = mcuCols * mcuRows;

    var rstCounter = 0;
    var mcuCount = 0;

    for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
      for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
        // Check for restart
        // KNOWN WRONG, and worth recording here because JPEG is the format most likely to be
        // trusted without checking.
        //
        // Two files with restart markers decode to the right size and the wrong picture: the mean
        // error a channel is 44 of 255 against ImageMagick and XnView, which agree with each other
        // exactly. The first eight rows — one row of minimum coded units — come out perfect and
        // everything below them is wrong, so the loss of step happens at the very first restart and
        // not gradually.
        //
        // Reading the pieces did not find it: the interval is applied at the right moment, the bit
        // buffer is emptied before the marker is looked for, the marker itself is stepped over, the
        // predictors are cleared after it, and the byte reader backs up onto a marker rather than
        // consuming it. Each of those is what it should be, so the fault is in how they fit together
        // rather than in any one of them.
        //
        // A JPEG without restart markers is unaffected, which is most of them, and is why this went
        // unseen. Recorded rather than guessed at: this decoder is used by QuickTime pictures, raw
        // previews and QTIF as well as by JPEG itself.
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          reader.TryConsumeRestart(rstCounter);
          ++rstCounter;
          Array.Clear(dcPred);
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

  private static int _FindComponent(JpegComponentInfo[] components, byte id) {
    for (var i = 0; i < components.Length; ++i)
      if (components[i].Id == id)
        return i;
    return -1;
  }
}
