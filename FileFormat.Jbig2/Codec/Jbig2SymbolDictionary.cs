using System;
using System.Collections.Generic;

namespace FileFormat.Jbig2.Codec;

/// <summary>Symbol dictionary segment decoder/encoder for JBIG2 (ITU-T T.88 section 6.5).
/// A symbol dictionary defines reusable symbol bitmaps (glyphs) that can be referenced
/// by text region segments. Symbols are encoded using generic or refinement coding.</summary>
internal static class Jbig2SymbolDictionary {

  /// <summary>A single symbol bitmap in a dictionary.</summary>
  internal sealed class Symbol {
    /// <summary>Symbol width in pixels.</summary>
    internal int Width { get; init; }

    /// <summary>Symbol height in pixels.</summary>
    internal int Height { get; init; }

    /// <summary>1bpp packed pixel data (MSB first).</summary>
    internal byte[] Bitmap { get; init; } = [];
  }

  /// <summary>Result of decoding a symbol dictionary segment.</summary>
  internal sealed class SymbolDictionaryResult {
    /// <summary>Exported symbols from this dictionary.</summary>
    internal Symbol[] Symbols { get; init; } = [];
  }

  /// <summary>Decodes a symbol dictionary segment.</summary>
  /// <param name="data">Segment data bytes.</param>
  /// <param name="offset">Starting offset within data.</param>
  /// <param name="referredDictionaries">Symbol dictionaries from referred segments.</param>
  /// <returns>Decoded symbol dictionary.</returns>
  internal static SymbolDictionaryResult Decode(byte[] data, int offset, List<Symbol[]> referredDictionaries) {
    if (data.Length - offset < 2)
      return new SymbolDictionaryResult();

    // Symbol dictionary flags (2 bytes BE)
    var flags = (data[offset] << 8) | data[offset + 1];
    offset += 2;

    var sdHuff = (flags & 0x01) != 0;           // Use Huffman coding
    var sdRefAgg = (flags & 0x02) != 0;          // Use refinement/aggregate coding
    var sdTemplate = (flags >> 2) & 0x03;        // Generic region template
    var sdRTemplate = (flags >> 4) & 0x01;       // Refinement template
    // Huffman table selections (bits 5-14) - used when sdHuff is true
    var sdHuffDh = (flags >> 5) & 0x03;
    var sdHuffDw = (flags >> 7) & 0x03;
    var sdHuffBmSize = (flags >> 9) & 0x01;
    var sdHuffAggInst = (flags >> 10) & 0x01;
    // bit 11: context used
    // bit 12: context retained

    // AT pixels for generic region template
    var atX = new sbyte[4];
    var atY = new sbyte[4];

    if (!sdHuff) {
      switch (sdTemplate) {
        case 0:
          if (offset + 8 > data.Length)
            return new SymbolDictionaryResult();
          for (var i = 0; i < 4; ++i) {
            atX[i] = (sbyte)data[offset++];
            atY[i] = (sbyte)data[offset++];
          }
          break;
        case 1:
        case 2:
        case 3:
          if (offset + 2 > data.Length)
            return new SymbolDictionaryResult();
          atX[0] = (sbyte)data[offset++];
          atY[0] = (sbyte)data[offset++];
          break;
      }
    }

    // AT pixels for refinement template (if refinement/aggregate)
    var rAtX = new sbyte[2];
    var rAtY = new sbyte[2];
    if (sdRefAgg && !sdHuff && sdRTemplate == 0) {
      if (offset + 4 > data.Length)
        return new SymbolDictionaryResult();
      for (var i = 0; i < 2; ++i) {
        rAtX[i] = (sbyte)data[offset++];
        rAtY[i] = (sbyte)data[offset++];
      }
    }

    // Number of exported symbols (4 bytes BE)
    if (offset + 4 > data.Length)
      return new SymbolDictionaryResult();
    var numExportedSymbols = _ReadInt32BE(data, offset);
    offset += 4;

    // Number of new symbols (4 bytes BE)
    if (offset + 4 > data.Length)
      return new SymbolDictionaryResult();
    var numNewSymbols = _ReadInt32BE(data, offset);
    offset += 4;

    // Collect all input symbols from referred dictionaries
    var inputSymbols = new List<Symbol>();
    foreach (var dict in referredDictionaries)
      inputSymbols.AddRange(dict);
    var numInputSymbols = inputSymbols.Count;

    // Calculate SYMCODELEN = ceil(log2(numInputSymbols + numNewSymbols))
    var totalSymbols = numInputSymbols + numNewSymbols;
    var symCodeLen = 0;
    var temp = 1;
    while (temp < totalSymbols) {
      ++symCodeLen;
      temp <<= 1;
    }
    if (symCodeLen == 0)
      symCodeLen = 1;

    // Decode new symbols
    var newSymbols = new List<Symbol>();

    if (sdHuff) {
      // Huffman-coded symbol dictionary - simplified
      _DecodeHuffmanSymbols(data, ref offset, numNewSymbols, sdTemplate, newSymbols);
    } else {
      // Arithmetic-coded symbol dictionary
      _DecodeArithmeticSymbols(
        data, ref offset, numNewSymbols, sdTemplate, sdRefAgg, sdRTemplate,
        atX, atY, rAtX, rAtY, symCodeLen, inputSymbols, newSymbols
      );
    }

    // Build export list
    var allSymbols = new List<Symbol>(inputSymbols);
    allSymbols.AddRange(newSymbols);

    // Export flags determine which symbols are exported
    var exportedSymbols = _DecodeExportFlags(data, ref offset, allSymbols, numExportedSymbols, sdHuff);

    return new SymbolDictionaryResult { Symbols = exportedSymbols };
  }

  /// <summary>Encodes a symbol dictionary segment.</summary>
  internal static byte[] Encode(Symbol[] symbols, int template) {
    var result = new List<byte>();

    // Flags: arithmetic coding, no refinement, given template
    var flags = (template & 0x03) << 2;
    result.Add((byte)((flags >> 8) & 0xFF));
    result.Add((byte)(flags & 0xFF));

    // AT pixels (defaults)
    switch (template) {
      case 0:
        // 4 AT pixel pairs
        result.AddRange([(byte)3, (byte)0xFF, (byte)0xFF, (byte)0xFF, (byte)2, (byte)0xFF, (byte)2, (byte)0xFF]);
        break;
      case 1:
      case 2:
      case 3:
        result.AddRange([(byte)3, (byte)0xFF]);
        break;
    }

    // Number of exported symbols
    _WriteInt32BE(result, symbols.Length);

    // Number of new symbols
    _WriteInt32BE(result, symbols.Length);

    // Encode each symbol using generic region coding
    var encoder = new Jbig2ArithmeticEncoder();
    var intCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var atXDef = new sbyte[] { 3, -3, 2, -2 };
    var atYDef = new sbyte[] { -1, -1, -2, -2 };

    // Height class delta-coded
    var prevHeight = 0;
    foreach (var symbol in symbols) {
      var deltaHeight = symbol.Height - prevHeight;
      encoder.EncodeInteger(intCtx, deltaHeight);
      prevHeight = symbol.Height;

      // Delta width (first symbol in height class = width)
      encoder.EncodeInteger(intCtx, symbol.Width);

      // Encode bitmap using generic region
      var bitmapData = Jbig2GenericRegion.Encode(
        symbol.Bitmap, symbol.Width, symbol.Height,
        template, false, atXDef, atYDef
      );
      result.AddRange(bitmapData);
    }

    // OOB (out-of-band) marker
    encoder.EncodeInteger(intCtx, 0);

    // Export all symbols
    result.AddRange(encoder.Finish());

    return [.. result];
  }

  private static void _DecodeArithmeticSymbols(
    byte[] data, ref int offset,
    int numNewSymbols, int sdTemplate, bool sdRefAgg, int sdRTemplate,
    sbyte[] atX, sbyte[] atY, sbyte[] rAtX, sbyte[] rAtY,
    int symCodeLen,
    List<Symbol> inputSymbols,
    List<Symbol> newSymbols
  ) {
    var decoder = new Jbig2ArithmeticDecoder(data, offset);

    // Integer coding contexts
    var dhCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var dwCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var bmSizeCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
    var aggInstCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);

    // IAID contexts
    var iaidCtxSize = 1 << (symCodeLen + 1);
    var iaidCtxI = new int[iaidCtxSize];
    var iaidCtxMps = new int[iaidCtxSize];

    var hcHeight = 0;
    var nsym = 0;

    while (nsym < numNewSymbols) {
      // Decode HCDH (height class delta height)
      var dh = decoder.DecodeInteger(dhCtx);
      if (dh == null)
        break;
      hcHeight += dh.Value;

      var symWidth = 0;
      var totalWidth = 0;

      // Decode symbols in this height class
      while (true) {
        // Decode SYMWIDTH (delta width)
        var dw = decoder.DecodeInteger(dwCtx);
        if (dw == null)
          break; // OOB signals end of height class

        symWidth += dw.Value;
        totalWidth += symWidth;

        byte[] symbolBitmap;

        if (!sdRefAgg) {
          // Direct-coded symbol: decode using generic region
          symbolBitmap = Jbig2GenericRegion.Decode(
            decoder, symWidth, hcHeight,
            sdTemplate, false, atX, atY
          );
        } else {
          // Aggregate-coded symbol
          var aggInstCount = decoder.DecodeInteger(aggInstCtx);
          if (aggInstCount == null || aggInstCount.Value == 0)
            break;

          if (aggInstCount.Value == 1) {
            // Single-instance aggregate: refinement of an existing symbol
            var symbolId = decoder.DecodeIaid(symCodeLen, iaidCtxI, iaidCtxMps);
            var allSymbols = new List<Symbol>(inputSymbols);
            allSymbols.AddRange(newSymbols);

            if (symbolId >= 0 && symbolId < allSymbols.Count) {
              var refSymbol = allSymbols[symbolId];

              // Decode refinement DX and DY
              var rdxCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
              var rdyCtx = new Jbig2ContextModel(Jbig2ContextModel.IntegerSize);
              var rdx = decoder.DecodeInteger(rdxCtx) ?? 0;
              var rdy = decoder.DecodeInteger(rdyCtx) ?? 0;

              symbolBitmap = Jbig2RefinementRegion.Decode(
                decoder, symWidth, hcHeight,
                sdRTemplate, refSymbol.Bitmap,
                refSymbol.Width, refSymbol.Height,
                rdx, rdy, false, rAtX, rAtY
              );
            } else {
              // Invalid reference, create empty bitmap
              symbolBitmap = new byte[((symWidth + 7) / 8) * hcHeight];
            }
          } else {
            // Multi-instance aggregate: generic region decode
            symbolBitmap = Jbig2GenericRegion.Decode(
              decoder, symWidth, hcHeight,
              sdTemplate, false, atX, atY
            );
          }
        }

        newSymbols.Add(new Symbol {
          Width = symWidth,
          Height = hcHeight,
          Bitmap = symbolBitmap,
        });
        ++nsym;

        if (nsym >= numNewSymbols)
          break;
      }
    }

    offset = decoder.ByteOffset;
  }

  private static void _DecodeHuffmanSymbols(
    byte[] data, ref int offset,
    int numNewSymbols, int sdTemplate,
    List<Symbol> newSymbols
  ) {
    // Simplified Huffman symbol dictionary decoding
    // For complete Huffman support, use Jbig2HuffmanDecoder
    var bytesPerSymbol = 0;

    for (var i = 0; i < numNewSymbols; ++i) {
      // Read symbol dimensions from remaining data
      if (offset + 8 > data.Length)
        break;

      var w = _ReadInt32BE(data, offset);
      offset += 4;
      var h = _ReadInt32BE(data, offset);
      offset += 4;

      if (w <= 0 || h <= 0)
        continue;

      bytesPerSymbol = ((w + 7) / 8) * h;
      if (offset + bytesPerSymbol > data.Length)
        break;

      var bitmap = new byte[bytesPerSymbol];
      Array.Copy(data, offset, bitmap, 0, bytesPerSymbol);
      offset += bytesPerSymbol;

      newSymbols.Add(new Symbol {
        Width = w,
        Height = h,
        Bitmap = bitmap,
      });
    }
  }

  private static Symbol[] _DecodeExportFlags(
    byte[] data, ref int offset,
    List<Symbol> allSymbols,
    int numExportedSymbols,
    bool useHuffman
  ) {
    if (numExportedSymbols <= 0 || allSymbols.Count == 0) {
      // Export all new symbols if no explicit export
      if (allSymbols.Count <= numExportedSymbols)
        return [.. allSymbols];
      return [];
    }

    // For arithmetic coding, decode export flags using run-length coding
    // Simplified: export the last numExportedSymbols symbols
    var exportStart = Math.Max(0, allSymbols.Count - numExportedSymbols);
    var exported = new Symbol[Math.Min(numExportedSymbols, allSymbols.Count - exportStart)];
    for (var i = 0; i < exported.Length; ++i)
      exported[i] = allSymbols[exportStart + i];

    return exported;
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
