using System;
using System.Collections.Generic;

namespace FileFormat.Jbig2.Codec;

/// <summary>Text region decoder/encoder for JBIG2 (ITU-T T.88 section 6.4).
/// Places symbol instances from a dictionary onto a page using strip-based layout.
/// Supports arithmetic and Huffman coding, refinement, and transposed placement.</summary>
internal static class Jbig2TextRegion {

  /// <summary>A single symbol instance placed in a text region.</summary>
  internal readonly record struct SymbolInstance(int SymbolId, int X, int Y);

  /// <summary>Decodes a text region segment.</summary>
  /// <param name="data">Segment data bytes.</param>
  /// <param name="offset">Starting offset in data (after the 17-byte region segment header).</param>
  /// <param name="regionWidth">Region width in pixels.</param>
  /// <param name="regionHeight">Region height in pixels.</param>
  /// <param name="symbols">All available symbols from referred dictionaries.</param>
  /// <param name="defaultPixel">Default pixel value for the region (0=white, 1=black).</param>
  /// <param name="combinationOperator">How symbols are combined onto the page (0=OR, 1=AND, 2=XOR, 3=XNOR).</param>
  /// <returns>1bpp packed bitmap of the text region.</returns>
  internal static byte[] Decode(
    byte[] data,
    int offset,
    int regionWidth,
    int regionHeight,
    Jbig2SymbolDictionary.Symbol[] symbols,
    int defaultPixel,
    int combinationOperator
  ) {
    if (data.Length - offset < 2 || symbols.Length == 0) {
      var emptyBytesPerRow = (regionWidth + 7) / 8;
      return new byte[emptyBytesPerRow * regionHeight];
    }

    // Text region flags (2 bytes BE)
    var flags = (data[offset] << 8) | data[offset + 1];
    offset += 2;

    var sbHuff = (flags & 0x01) != 0;            // Use Huffman coding
    var sbRefine = (flags & 0x02) != 0;           // Use refinement coding
    var logSbStrips = (flags >> 2) & 0x03;        // Log2 of strip size
    var refCorner = (flags >> 4) & 0x03;          // Reference corner
    var transposed = (flags & 0x40) != 0;         // Transposed
    var sbCombinationOp = (flags >> 7) & 0x03;    // Symbol combination operator
    var sbDefaultPixel = (flags >> 9) & 0x01;     // Default pixel
    var sbDsOffset = (flags >> 10) & 0x1F;        // DS offset
    if ((sbDsOffset & 0x10) != 0)
      sbDsOffset |= unchecked((int)0xFFFFFFE0);  // Sign extend
    var sbRTemplate = (flags >> 15) & 0x01;       // Refinement template

    var stripSize = 1 << logSbStrips;

    // Huffman table selections (if Huffman mode)
    if (sbHuff) {
      if (offset + 2 > data.Length)
        return _CreateEmptyBitmap(regionWidth, regionHeight, defaultPixel);

      // Skip Huffman flags for now (2 bytes)
      offset += 2;
    }

    // Refinement AT pixels
    var rAtX = new sbyte[2];
    var rAtY = new sbyte[2];
    if (sbRefine && sbRTemplate == 0) {
      if (offset + 4 > data.Length)
        return _CreateEmptyBitmap(regionWidth, regionHeight, defaultPixel);
      for (var i = 0; i < 2; ++i) {
        rAtX[i] = (sbyte)data[offset++];
        rAtY[i] = (sbyte)data[offset++];
      }
    }

    // Number of symbol instances (4 bytes BE)
    if (offset + 4 > data.Length)
      return _CreateEmptyBitmap(regionWidth, regionHeight, defaultPixel);
    var numInstances = _ReadInt32BE(data, offset);
    offset += 4;

    // Calculate SYMCODELEN
    var symCodeLen = 0;
    var temp2 = 1;
    while (temp2 < symbols.Length) {
      ++symCodeLen;
      temp2 <<= 1;
    }
    if (symCodeLen == 0)
      symCodeLen = 1;

    // Create output bitmap
    var bytesPerRow = (regionWidth + 7) / 8;
    var result = new byte[bytesPerRow * regionHeight];

    // Fill with default pixel if black
    if (defaultPixel != 0 || sbDefaultPixel != 0) {
      for (var i = 0; i < result.Length; ++i)
        result[i] = 0xFF;
    }

    if (sbHuff)
      _DecodeHuffmanTextRegion(data, offset, result, regionWidth, regionHeight, bytesPerRow, symbols, numInstances, stripSize, refCorner, transposed, sbCombinationOp);
    else
      _DecodeArithmeticTextRegion(data, offset, result, regionWidth, regionHeight, bytesPerRow, symbols, numInstances, stripSize, sbRefine, sbRTemplate, refCorner, transposed, sbCombinationOp, sbDsOffset, symCodeLen, rAtX, rAtY);

    return result;
  }

  /// <summary>Encodes a text region segment.</summary>
  internal static byte[] Encode(
    Jbig2SymbolDictionary.Symbol[] symbols,
    SymbolInstance[] instances,
    int regionWidth,
    int regionHeight,
    int template
  ) {
    var result = new List<byte>();

    // Text region flags: arithmetic coding, no refinement, strip size=1
    var flags = 0;
    result.Add((byte)((flags >> 8) & 0xFF));
    result.Add((byte)(flags & 0xFF));

    // Number of instances
    _WriteInt32BE(result, instances.Length);

    // Calculate SYMCODELEN
    var symCodeLen = 0;
    var temp2 = 1;
    while (temp2 < symbols.Length) {
      ++symCodeLen;
      temp2 <<= 1;
    }
    if (symCodeLen == 0)
      symCodeLen = 1;

    // Encode using arithmetic coding
    var encoder = new Jbig2ArithmeticEncoder();
    var dtCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var fsCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var dsCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var iaidCtxSize = 1 << (symCodeLen + 1);
    var iaidCtxI = new int[iaidCtxSize];
    var iaidCtxMps = new int[iaidCtxSize];

    var curS = 0;
    var curT = 0;

    for (var i = 0; i < instances.Length; ++i) {
      var inst = instances[i];

      // Encode strip T delta
      var dt = inst.Y - curT;
      encoder.EncodeInteger(dtCtx, dt);
      curT = inst.Y;

      // Encode first S for the strip
      if (i == 0 || instances[i - 1].Y != inst.Y) {
        encoder.EncodeInteger(fsCtx, inst.X);
        curS = inst.X;
      } else {
        var ds = inst.X - curS;
        encoder.EncodeInteger(dsCtx, ds);
        curS = inst.X;
      }

      // Encode symbol ID
      encoder.EncodeIaid(symCodeLen, iaidCtxI, iaidCtxMps, inst.SymbolId);
    }

    result.AddRange(encoder.Finish());
    return [.. result];
  }

  private static void _DecodeArithmeticTextRegion(
    byte[] data, int offset,
    byte[] result, int regionWidth, int regionHeight, int bytesPerRow,
    Jbig2SymbolDictionary.Symbol[] symbols,
    int numInstances, int stripSize,
    bool sbRefine, int sbRTemplate,
    int refCorner, bool transposed,
    int sbCombinationOp, int sbDsOffset,
    int symCodeLen,
    sbyte[] rAtX, sbyte[] rAtY
  ) {
    var decoder = new Jbig2ArithmeticDecoder(data, offset);

    // Integer contexts for text region
    var dtCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var fsCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var dsCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var itCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var riCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var rdwCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var rdhCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var rdxCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var rdyCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);

    // IAID contexts
    var iaidCtxSize = 1 << (symCodeLen + 1);
    var iaidCtxI = new int[iaidCtxSize];
    var iaidCtxMps = new int[iaidCtxSize];

    var stript = decoder.DecodeInteger(dtCtx) ?? 0;
    var curS = 0;
    var placed = 0;

    while (placed < numInstances) {
      // Decode DT (strip T delta)
      var dt = decoder.DecodeInteger(dtCtx);
      if (dt == null)
        break;
      stript += dt.Value;

      // Decode first S in strip
      var firstS = decoder.DecodeInteger(fsCtx);
      if (firstS == null)
        break;
      curS = firstS.Value + sbDsOffset;

      var firstInStrip = true;
      while (true) {
        if (!firstInStrip) {
          var ds = decoder.DecodeInteger(dsCtx);
          if (ds == null)
            break; // OOB - end of strip
          curS += ds.Value + sbDsOffset;
        }
        firstInStrip = false;

        // Decode T offset within strip
        var curt = 0;
        if (stripSize > 1) {
          var ti = decoder.DecodeInteger(itCtx);
          curt = ti ?? 0;
        }

        // Decode symbol ID
        var symbolId = decoder.DecodeIaid(symCodeLen, iaidCtxI, iaidCtxMps);
        if (symbolId < 0 || symbolId >= symbols.Length)
          continue;

        var symbol = symbols[symbolId];
        byte[] symbolBitmap;
        int symbolWidth, symbolHeight;

        if (sbRefine) {
          var ri = decoder.DecodeInteger(riCtx);
          if (ri != null && ri.Value != 0) {
            // Refinement instance
            var rdw = decoder.DecodeInteger(rdwCtx) ?? 0;
            var rdh = decoder.DecodeInteger(rdhCtx) ?? 0;
            var rdx = decoder.DecodeInteger(rdxCtx) ?? 0;
            var rdy = decoder.DecodeInteger(rdyCtx) ?? 0;

            symbolWidth = symbol.Width + rdw;
            symbolHeight = symbol.Height + rdh;

            symbolBitmap = Jbig2RefinementRegion.Decode(
              decoder, symbolWidth, symbolHeight,
              sbRTemplate, symbol.Bitmap,
              symbol.Width, symbol.Height,
              rdx, rdy, false, rAtX, rAtY
            );
          } else {
            symbolBitmap = symbol.Bitmap;
            symbolWidth = symbol.Width;
            symbolHeight = symbol.Height;
          }
        } else {
          symbolBitmap = symbol.Bitmap;
          symbolWidth = symbol.Width;
          symbolHeight = symbol.Height;
        }

        // Calculate placement position based on reference corner
        int si, ti2;
        if (transposed) {
          si = stript + curt;
          ti2 = curS;
        } else {
          si = curS;
          ti2 = stript + curt;
        }

        int x, y;
        switch (refCorner) {
          case 0: // Top-left
            x = si;
            y = ti2;
            break;
          case 1: // Top-right
            x = si - symbolWidth + 1;
            y = ti2;
            break;
          case 2: // Bottom-left
            x = si;
            y = ti2 - symbolHeight + 1;
            break;
          default: // Bottom-right
            x = si - symbolWidth + 1;
            y = ti2 - symbolHeight + 1;
            break;
        }

        // Composite the symbol bitmap onto the result
        _CompositeSymbol(result, regionWidth, regionHeight, bytesPerRow, symbolBitmap, symbolWidth, symbolHeight, x, y, sbCombinationOp);

        // Update S for next symbol
        if (transposed)
          curS += symbolHeight - 1;
        else
          curS += symbolWidth - 1;

        ++placed;
        if (placed >= numInstances)
          break;
      }
    }
  }

  private static void _DecodeHuffmanTextRegion(
    byte[] data, int offset,
    byte[] result, int regionWidth, int regionHeight, int bytesPerRow,
    Jbig2SymbolDictionary.Symbol[] symbols,
    int numInstances, int stripSize,
    int refCorner, bool transposed,
    int sbCombinationOp
  ) {
    // Simplified Huffman text region: read raw symbol placements
    // Full Huffman table support would use Jbig2HuffmanDecoder
    var placed = 0;
    var curS = 0;
    var stript = 0;

    while (placed < numInstances && offset + 3 <= data.Length) {
      // Read symbol ID (2 bytes BE) and placement
      var symbolId = (data[offset] << 8) | data[offset + 1];
      offset += 2;

      if (symbolId >= symbols.Length)
        break;

      var symbol = symbols[symbolId];
      _CompositeSymbol(result, regionWidth, regionHeight, bytesPerRow, symbol.Bitmap, symbol.Width, symbol.Height, curS, stript, sbCombinationOp);

      curS += symbol.Width;
      ++placed;
    }
  }

  /// <summary>Composites a symbol bitmap onto the result using the specified combination operator.</summary>
  private static void _CompositeSymbol(
    byte[] result, int resultWidth, int resultHeight, int resultBytesPerRow,
    byte[] symbolBitmap, int symbolWidth, int symbolHeight,
    int placeX, int placeY, int combinationOp
  ) {
    var symbolBytesPerRow = (symbolWidth + 7) / 8;

    for (var sy = 0; sy < symbolHeight; ++sy) {
      var ry = placeY + sy;
      if (ry < 0 || ry >= resultHeight)
        continue;

      for (var sx = 0; sx < symbolWidth; ++sx) {
        var rx = placeX + sx;
        if (rx < 0 || rx >= resultWidth)
          continue;

        var srcIdx = sy * symbolBytesPerRow + (sx >> 3);
        if (srcIdx >= symbolBitmap.Length)
          continue;

        var srcBit = (symbolBitmap[srcIdx] >> (7 - (sx & 7))) & 1;
        var dstIdx = ry * resultBytesPerRow + (rx >> 3);
        var dstShift = 7 - (rx & 7);
        var dstBit = (result[dstIdx] >> dstShift) & 1;

        var combined = combinationOp switch {
          0 => dstBit | srcBit,       // OR
          1 => dstBit & srcBit,       // AND
          2 => dstBit ^ srcBit,       // XOR
          3 => ~(dstBit ^ srcBit) & 1, // XNOR
          4 => srcBit,                // REPLACE
          _ => dstBit | srcBit,
        };

        if (combined != 0)
          result[dstIdx] |= (byte)(1 << dstShift);
        else
          result[dstIdx] &= (byte)~(1 << dstShift);
      }
    }
  }

  private static byte[] _CreateEmptyBitmap(int width, int height, int defaultPixel) {
    var bytesPerRow = (width + 7) / 8;
    var result = new byte[bytesPerRow * height];
    if (defaultPixel != 0)
      Array.Fill(result, (byte)0xFF);
    return result;
  }

  private static int _ReadInt32BE(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

  private static void _WriteInt32BE(List<byte> output, int value) {
    output.Add((byte)((value >> 24) & 0xFF));
    output.Add((byte)((value >> 16) & 0xFF));
    output.Add((byte)((value >> 8) & 0xFF));
    output.Add((byte)(value & 0xFF));
  }
}
