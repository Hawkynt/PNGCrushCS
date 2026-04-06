using System;

namespace FileFormat.Jpeg;

/// <summary>Decodes progressive JPEG entropy data into coefficient blocks (spectral selection + successive approximation).</summary>
internal static class JpegProgressiveDecoder {

  public static void DecodeScan(
    byte[] data,
    int entropyStart,
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
        _DecodeDcFirst(data, entropyStart, frame, scan, dcTables, componentData, restartInterval);
      else
        _DecodeDcRefine(data, entropyStart, frame, scan, componentData, restartInterval);
    } else {
      if (isFirstPass)
        _DecodeAcFirst(data, entropyStart, frame, scan, acTables, componentData, restartInterval);
      else
        _DecodeAcRefine(data, entropyStart, frame, scan, acTables, componentData, restartInterval);
    }
  }

  private static void _DecodeDcFirst(
    byte[] data, int entropyStart, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] dcTables, JpegComponentData[] componentData, int restartInterval
  ) {
    var reader = new JpegBitReader(data, entropyStart);
    var dcPred = new int[frame.Components.Length];
    var al = scan.SuccessiveApproxLow;

    _ForEachMcu(frame, scan, restartInterval, reader, dcPred,
      (compIdx, comp, scanComp, blockX, blockY) => {
        var dcTable = dcTables[scanComp.DcTableId];
        var block = componentData[compIdx].Blocks[blockY][blockX];

        var dcDiff = JpegHuffmanDecoder.DecodeDc(reader, dcTable);
        dcPred[compIdx] += dcDiff;
        block.Coefficients[0] = (short)(dcPred[compIdx] << al);
      });
  }

  private static void _DecodeDcRefine(
    byte[] data, int entropyStart, JpegFrameHeader frame, JpegScanHeader scan,
    JpegComponentData[] componentData, int restartInterval
  ) {
    var reader = new JpegBitReader(data, entropyStart);
    var dcPred = new int[frame.Components.Length];
    var al = scan.SuccessiveApproxLow;

    _ForEachMcu(frame, scan, restartInterval, reader, dcPred,
      (compIdx, comp, scanComp, blockX, blockY) => {
        var block = componentData[compIdx].Blocks[blockY][blockX];
        block.Coefficients[0] |= (short)(reader.ReadBit() << al);
      });
  }

  private static void _DecodeAcFirst(
    byte[] data, int entropyStart, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] acTables, JpegComponentData[] componentData, int restartInterval
  ) {
    // AC-first progressive: single component per scan
    if (scan.Components.Length != 1)
      return;

    var reader = new JpegBitReader(data, entropyStart);
    var scanComp = scan.Components[0];
    var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
    if (compIdx < 0)
      return;

    var acTable = acTables[scanComp.AcTableId];
    var compData = componentData[compIdx];
    var comp = frame.Components[compIdx];
    var al = scan.SuccessiveApproxLow;
    var ss = scan.SpectralStart;
    var se = scan.SpectralEnd;

    var eobRun = 0;
    var rstCounter = 0;
    var mcuCount = 0;

    for (var blockY = 0; blockY < compData.HeightInBlocks; ++blockY)
      for (var blockX = 0; blockX < compData.WidthInBlocks; ++blockX) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          reader.TryConsumeRestart(rstCounter);
          ++rstCounter;
          eobRun = 0;
        }

        var block = compData.Blocks[blockY][blockX];

        if (eobRun > 0) {
          --eobRun;
          ++mcuCount;
          continue;
        }

        for (int k = ss; k <= se;) {
          var rs = reader.DecodeHuffman(acTable);
          var r = rs >> 4;
          var s = rs & 0x0F;

          if (s == 0) {
            if (r < 15) {
              eobRun = (1 << r) - 1;
              if (r > 0)
                eobRun += reader.ReadBits(r);
              break;
            }

            k += 16;
            continue;
          }

          k += r;
          if (k > se)
            break;

          block.Coefficients[k] = (short)(reader.Receive(s) << al);
          ++k;
        }

        ++mcuCount;
      }
  }

  private static void _DecodeAcRefine(
    byte[] data, int entropyStart, JpegFrameHeader frame, JpegScanHeader scan,
    JpegHuffmanTable[] acTables, JpegComponentData[] componentData, int restartInterval
  ) {
    if (scan.Components.Length != 1)
      return;

    var reader = new JpegBitReader(data, entropyStart);
    var scanComp = scan.Components[0];
    var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
    if (compIdx < 0)
      return;

    var acTable = acTables[scanComp.AcTableId];
    var compData = componentData[compIdx];
    var al = scan.SuccessiveApproxLow;
    var ss = scan.SpectralStart;
    var se = scan.SpectralEnd;
    var p1 = 1 << al;
    var m1 = (-1) << al;

    var eobRun = 0;
    var rstCounter = 0;
    var mcuCount = 0;

    for (var blockY = 0; blockY < compData.HeightInBlocks; ++blockY)
      for (var blockX = 0; blockX < compData.WidthInBlocks; ++blockX) {
        if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
          reader.TryConsumeRestart(rstCounter);
          ++rstCounter;
          eobRun = 0;
        }

        var block = compData.Blocks[blockY][blockX];

        if (eobRun > 0) {
          // Refine existing non-zero coefficients
          for (var k = ss; k <= se; ++k)
            if (block.Coefficients[k] != 0) {
              var bit = reader.ReadBit();
              if (bit != 0)
                if ((block.Coefficients[k] & p1) == 0)
                  block.Coefficients[k] += (short)(block.Coefficients[k] > 0 ? p1 : m1);
            }

          --eobRun;
          ++mcuCount;
          continue;
        }

        var k2 = ss;
        while (k2 <= se) {
          var rs = reader.DecodeHuffman(acTable);
          var r = rs >> 4;
          var s = rs & 0x0F;

          if (s == 0) {
            if (r < 15) {
              eobRun = (1 << r) - 1;
              if (r > 0)
                eobRun += reader.ReadBits(r);

              // Refine remaining non-zero
              while (k2 <= se) {
                if (block.Coefficients[k2] != 0) {
                  var bit = reader.ReadBit();
                  if (bit != 0)
                    if ((block.Coefficients[k2] & p1) == 0)
                      block.Coefficients[k2] += (short)(block.Coefficients[k2] > 0 ? p1 : m1);
                }

                ++k2;
              }

              break;
            }

            // ZRL: skip 16 zero/refine
            for (var i = 0; i < 16 && k2 <= se;) {
              if (block.Coefficients[k2] != 0) {
                var bit = reader.ReadBit();
                if (bit != 0)
                  if ((block.Coefficients[k2] & p1) == 0)
                    block.Coefficients[k2] += (short)(block.Coefficients[k2] > 0 ? p1 : m1);
              } else
                ++i;

              ++k2;
            }

            continue;
          }

          // s == 1: new non-zero coefficient
          // Skip r zero coefficients, refining non-zeros along the way
          for (var i = 0; i < r && k2 <= se;) {
            if (block.Coefficients[k2] != 0) {
              var bit = reader.ReadBit();
              if (bit != 0)
                if ((block.Coefficients[k2] & p1) == 0)
                  block.Coefficients[k2] += (short)(block.Coefficients[k2] > 0 ? p1 : m1);
            } else
              ++i;

            ++k2;
          }

          if (k2 > se)
            break;

          var sign = reader.ReadBit();
          block.Coefficients[k2] = (short)(sign != 0 ? p1 : m1);
          ++k2;
        }

        ++mcuCount;
      }
  }

  private static void _ForEachMcu(
    JpegFrameHeader frame,
    JpegScanHeader scan,
    int restartInterval,
    JpegBitReader reader,
    int[] dcPred,
    Action<int, JpegComponentInfo, (byte ComponentId, byte DcTableId, byte AcTableId), int, int> blockAction
  ) {
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

    // For interleaved scans with multiple components
    var isInterleaved = scan.Components.Length > 1;
    var rstCounter = 0;
    var mcuCount = 0;

    if (isInterleaved) {
      for (var mcuRow = 0; mcuRow < mcuRows; ++mcuRow)
        for (var mcuCol = 0; mcuCol < mcuCols; ++mcuCol) {
          if (restartInterval > 0 && mcuCount > 0 && mcuCount % restartInterval == 0) {
            reader.TryConsumeRestart(rstCounter);
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
                blockAction(compIdx, comp, scanComp, bx, by);
              }
          }

          ++mcuCount;
        }
    } else {
      // Non-interleaved: single component
      var scanComp = scan.Components[0];
      var compIdx = _FindComponent(frame.Components, scanComp.ComponentId);
      if (compIdx < 0)
        return;
      var comp = frame.Components[compIdx];

      var blockCols = (frame.Width * comp.HSamplingFactor + maxH * 8 - 1) / (maxH * 8);
      var blockRows = (frame.Height * comp.VSamplingFactor + maxV * 8 - 1) / (maxV * 8);

      var blockCount = 0;
      for (var by = 0; by < blockRows; ++by)
        for (var bx = 0; bx < blockCols; ++bx) {
          if (restartInterval > 0 && blockCount > 0 && blockCount % restartInterval == 0) {
            reader.TryConsumeRestart(rstCounter);
            ++rstCounter;
            Array.Clear(dcPred);
          }

          blockAction(compIdx, comp, scanComp, bx, by);
          ++blockCount;
        }
    }
  }

  private static int _FindComponent(JpegComponentInfo[] components, byte id) {
    for (var i = 0; i < components.Length; ++i)
      if (components[i].Id == id)
        return i;
    return -1;
  }
}
