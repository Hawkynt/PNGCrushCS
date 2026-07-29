using System;
using System.Collections.Generic;

namespace FileFormat.Jbig2.Codec;

/// <summary>JBIG2 segment header and data parser (ITU-T T.88 section 7).
/// Handles all segment types (0-53, 62) and dispatches to the appropriate
/// codec for decoding segment data.</summary>
internal static class Jbig2SegmentParser {

  /// <summary>Page-level state maintained during segment processing.</summary>
  internal sealed class PageState {
    /// <summary>Page width in pixels (from PageInformation segment).</summary>
    internal int Width;

    /// <summary>Page height in pixels.</summary>
    internal int Height;

    /// <summary>Page bitmap (1bpp packed, MSB first).</summary>
    internal byte[]? Bitmap;

    /// <summary>Bytes per row of the page bitmap.</summary>
    internal int BytesPerRow;

    /// <summary>Default pixel value (0=white, 1=black).</summary>
    internal int DefaultPixel;

    /// <summary>Default combination operator.</summary>
    internal int CombinationOperator;

    /// <summary>Whether the page requires an auxiliary buffer.</summary>
    internal bool RequiresAuxBuffer;
  }

  /// <summary>Decoding context across all segments.</summary>
  internal sealed class DecodingContext {
    /// <summary>Currently active page state.</summary>
    internal PageState? CurrentPage;

    /// <summary>Symbol dictionaries indexed by segment number.</summary>
    internal readonly Dictionary<int, Jbig2SymbolDictionary.Symbol[]> SymbolDictionaries = new();

    /// <summary>Pattern dictionaries indexed by segment number.</summary>
    internal readonly Dictionary<int, (byte[][] Patterns, int Width, int Height)> PatternDictionaries = new();

    /// <summary>Intermediate generic region results indexed by segment number.</summary>
    internal readonly Dictionary<int, byte[]> IntermediateRegions = new();

    /// <summary>Segment lookup for referred segments.</summary>
    internal readonly Dictionary<int, Jbig2Segment> Segments = new();
  }

  /// <summary>Processes a single segment and updates the decoding context.</summary>
  internal static void ProcessSegment(Jbig2Segment segment, DecodingContext context) {
    context.Segments[segment.Number] = segment;

    switch (segment.Type) {
      case Jbig2SegmentType.PageInformation:
        _ProcessPageInfo(segment, context);
        break;

      case Jbig2SegmentType.SymbolDictionary:
        _ProcessSymbolDictionary(segment, context);
        break;

      case Jbig2SegmentType.ImmediateTextRegion:
      case Jbig2SegmentType.ImmediateLosslessTextRegion:
        _ProcessTextRegion(segment, context, immediate: true);
        break;

      case Jbig2SegmentType.IntermediateTextRegion:
        _ProcessTextRegion(segment, context, immediate: false);
        break;

      case Jbig2SegmentType.ImmediateGenericRegion:
      case Jbig2SegmentType.ImmediateLosslessGenericRegion:
        _ProcessGenericRegion(segment, context, immediate: true);
        break;

      case Jbig2SegmentType.IntermediateGenericRegion:
        _ProcessGenericRegion(segment, context, immediate: false);
        break;

      case Jbig2SegmentType.ImmediateGenericRefinementRegion:
      case Jbig2SegmentType.ImmediateLosslessGenericRefinementRegion:
        _ProcessRefinementRegion(segment, context, immediate: true);
        break;

      case Jbig2SegmentType.IntermediateGenericRefinementRegion:
        _ProcessRefinementRegion(segment, context, immediate: false);
        break;

      case Jbig2SegmentType.PatternDictionary:
        _ProcessPatternDictionary(segment, context);
        break;

      case Jbig2SegmentType.ImmediateHalftoneRegion:
      case Jbig2SegmentType.ImmediateLosslessHalftoneRegion:
        _ProcessHalftoneRegion(segment, context, immediate: true);
        break;

      case Jbig2SegmentType.IntermediateHalftoneRegion:
        _ProcessHalftoneRegion(segment, context, immediate: false);
        break;

      case Jbig2SegmentType.Tables:
        // Custom Huffman table - stored for later use by Huffman-coded segments
        break;

      case Jbig2SegmentType.EndOfPage:
      case Jbig2SegmentType.EndOfStripe:
      case Jbig2SegmentType.EndOfFile:
      case Jbig2SegmentType.Profiles:
      case Jbig2SegmentType.Extension:
        // No data processing needed
        break;
    }
  }

  private static void _ProcessPageInfo(Jbig2Segment segment, DecodingContext context) {
    var data = segment.Data;
    if (data.Length < 19)
      return;

    var width = _ReadInt32BE(data, 0);
    var height = _ReadInt32BE(data, 4);
    // Bytes 8-15: X and Y resolution (ignored)
    var pageFlags = data[16];
    var defaultPixel = pageFlags & 0x01;
    var combinationOp = (pageFlags >> 1) & 0x03;
    var requiresAuxBuffer = (pageFlags & 0x08) != 0;

    // If height is 0xFFFFFFFF, it's unknown (stripe mode)
    if (height < 0)
      height = 0;

    var bytesPerRow = (width + 7) / 8;
    var bitmap = new byte[bytesPerRow * height];

    if (defaultPixel != 0)
      Array.Fill(bitmap, (byte)0xFF);

    context.CurrentPage = new PageState {
      Width = width,
      Height = height,
      Bitmap = bitmap,
      BytesPerRow = bytesPerRow,
      DefaultPixel = defaultPixel,
      CombinationOperator = combinationOp,
      RequiresAuxBuffer = requiresAuxBuffer,
    };
  }

  private static void _ProcessSymbolDictionary(Jbig2Segment segment, DecodingContext context) {
    var referredDicts = new List<Jbig2SymbolDictionary.Symbol[]>();
    foreach (var refNum in segment.ReferredSegments) {
      if (context.SymbolDictionaries.TryGetValue(refNum, out var dict))
        referredDicts.Add(dict);
    }

    var result = Jbig2SymbolDictionary.Decode(segment.Data, 0, referredDicts);
    context.SymbolDictionaries[segment.Number] = result.Symbols;
  }

  private static void _ProcessTextRegion(Jbig2Segment segment, DecodingContext context, bool immediate) {
    var data = segment.Data;
    if (data.Length < 17)
      return;

    // Region segment information field (17 bytes)
    var regionWidth = _ReadInt32BE(data, 0);
    var regionHeight = _ReadInt32BE(data, 4);
    var regionX = _ReadInt32BE(data, 8);
    var regionY = _ReadInt32BE(data, 12);
    var combinationOp = data[16] & 0x07;

    // Collect symbols from referred dictionaries
    var allSymbols = new List<Jbig2SymbolDictionary.Symbol>();
    foreach (var refNum in segment.ReferredSegments) {
      if (context.SymbolDictionaries.TryGetValue(refNum, out var dict))
        allSymbols.AddRange(dict);
    }

    if (allSymbols.Count == 0)
      return;

    var page = context.CurrentPage;
    var defaultPixel = page?.DefaultPixel ?? 0;

    var regionBitmap = Jbig2TextRegion.Decode(
      data, 17, regionWidth, regionHeight,
      [.. allSymbols], defaultPixel, combinationOp
    );

    if (immediate && page?.Bitmap != null)
      _CompositeRegion(page.Bitmap, page.Width, page.Height, page.BytesPerRow, regionBitmap, regionWidth, regionHeight, regionX, regionY, combinationOp);
    else
      context.IntermediateRegions[segment.Number] = regionBitmap;
  }

  private static void _ProcessGenericRegion(Jbig2Segment segment, DecodingContext context, bool immediate) {
    var data = segment.Data;
    if (data.Length < 18)
      return;

    var regionWidth = _ReadInt32BE(data, 0);
    var regionHeight = _ReadInt32BE(data, 4);
    var regionX = _ReadInt32BE(data, 8);
    var regionY = _ReadInt32BE(data, 12);
    var combinationOp = data[16] & 0x07;

    var regionFlags = data[17];
    var useMmr = (regionFlags & 0x01) != 0;
    var template = (regionFlags >> 1) & 0x03;
    var useTypicalPrediction = (regionFlags & 0x08) != 0;

    byte[]? regionBitmap;

    if (useMmr) {
      var compressedData = new byte[data.Length - 18];
      Array.Copy(data, 18, compressedData, 0, compressedData.Length);
      regionBitmap = MmrCodec.Decode(compressedData, regionWidth, regionHeight);
    } else {
      // Arithmetic coding
      var offset = 18;

      // Read AT pixels
      var atX = new sbyte[4];
      var atY = new sbyte[4];
      switch (template) {
        case 0:
          if (offset + 8 > data.Length)
            return;
          for (var i = 0; i < 4; ++i) {
            atX[i] = (sbyte)data[offset++];
            atY[i] = (sbyte)data[offset++];
          }
          break;
        case 1:
        case 2:
        case 3:
          if (offset + 2 > data.Length)
            return;
          atX[0] = (sbyte)data[offset++];
          atY[0] = (sbyte)data[offset++];
          break;
      }

      var decoder = new Jbig2ArithmeticDecoder(data, offset);
      regionBitmap = Jbig2GenericRegion.Decode(
        decoder, regionWidth, regionHeight,
        template, useTypicalPrediction, atX, atY
      );
    }

    if (regionBitmap == null)
      return;

    var page = context.CurrentPage;
    if (immediate && page?.Bitmap != null)
      _CompositeRegion(page.Bitmap, page.Width, page.Height, page.BytesPerRow, regionBitmap, regionWidth, regionHeight, regionX, regionY, combinationOp);
    else
      context.IntermediateRegions[segment.Number] = regionBitmap;
  }

  private static void _ProcessRefinementRegion(Jbig2Segment segment, DecodingContext context, bool immediate) {
    var data = segment.Data;
    if (data.Length < 18)
      return;

    var regionWidth = _ReadInt32BE(data, 0);
    var regionHeight = _ReadInt32BE(data, 4);
    var regionX = _ReadInt32BE(data, 8);
    var regionY = _ReadInt32BE(data, 12);
    var combinationOp = data[16] & 0x07;

    var refinementFlags = data[17];
    var grTemplate = refinementFlags & 0x01;
    var useTypicalPrediction = (refinementFlags & 0x02) != 0;

    var offset = 18;

    // AT pixels for template 0
    var atX = new sbyte[2];
    var atY = new sbyte[2];
    if (grTemplate == 0) {
      if (offset + 4 > data.Length)
        return;
      for (var i = 0; i < 2; ++i) {
        atX[i] = (sbyte)data[offset++];
        atY[i] = (sbyte)data[offset++];
      }
    }

    // Find reference bitmap from referred segments
    byte[]? referenceBitmap = null;
    var refWidth = 0;
    var refHeight = 0;

    foreach (var refNum in segment.ReferredSegments) {
      if (context.IntermediateRegions.TryGetValue(refNum, out var refBmp)) {
        referenceBitmap = refBmp;
        // Approximate dimensions from referred segment
        if (context.Segments.TryGetValue(refNum, out var refSeg) && refSeg.Data.Length >= 8) {
          refWidth = _ReadInt32BE(refSeg.Data, 0);
          refHeight = _ReadInt32BE(refSeg.Data, 4);
        }
        break;
      }
    }

    if (referenceBitmap == null) {
      // Use page bitmap as reference if no intermediate region
      var page = context.CurrentPage;
      if (page?.Bitmap != null) {
        referenceBitmap = page.Bitmap;
        refWidth = page.Width;
        refHeight = page.Height;
      } else {
        return;
      }
    }

    var decoder = new Jbig2ArithmeticDecoder(data, offset);
    var regionBitmap = Jbig2RefinementRegion.Decode(
      decoder, regionWidth, regionHeight,
      grTemplate, referenceBitmap,
      refWidth, refHeight,
      0, 0, useTypicalPrediction, atX, atY
    );

    var curPage = context.CurrentPage;
    if (immediate && curPage?.Bitmap != null)
      _CompositeRegion(curPage.Bitmap, curPage.Width, curPage.Height, curPage.BytesPerRow, regionBitmap, regionWidth, regionHeight, regionX, regionY, combinationOp);
    else
      context.IntermediateRegions[segment.Number] = regionBitmap;
  }

  private static void _ProcessPatternDictionary(Jbig2Segment segment, DecodingContext context) {
    var (patterns, patWidth, patHeight) = Jbig2HalftoneRegion.DecodePatternDictionary(segment.Data, 0);
    if (patterns.Length > 0)
      context.PatternDictionaries[segment.Number] = (patterns, patWidth, patHeight);
  }

  private static void _ProcessHalftoneRegion(Jbig2Segment segment, DecodingContext context, bool immediate) {
    var data = segment.Data;
    if (data.Length < 17)
      return;

    var regionWidth = _ReadInt32BE(data, 0);
    var regionHeight = _ReadInt32BE(data, 4);
    var regionX = _ReadInt32BE(data, 8);
    var regionY = _ReadInt32BE(data, 12);
    var combinationOp = data[16] & 0x07;

    // Find pattern dictionary from referred segments
    byte[][]? patterns = null;
    var patWidth = 0;
    var patHeight = 0;

    foreach (var refNum in segment.ReferredSegments) {
      if (context.PatternDictionaries.TryGetValue(refNum, out var pd)) {
        patterns = pd.Patterns;
        patWidth = pd.Width;
        patHeight = pd.Height;
        break;
      }
    }

    if (patterns == null || patterns.Length == 0)
      return;

    var regionBitmap = Jbig2HalftoneRegion.Decode(
      data, 17, regionWidth, regionHeight,
      patterns, patWidth, patHeight
    );

    var page = context.CurrentPage;
    if (immediate && page?.Bitmap != null)
      _CompositeRegion(page.Bitmap, page.Width, page.Height, page.BytesPerRow, regionBitmap, regionWidth, regionHeight, regionX, regionY, combinationOp);
    else
      context.IntermediateRegions[segment.Number] = regionBitmap;
  }

  /// <summary>Composites a region bitmap onto the page bitmap.</summary>
  private static void _CompositeRegion(
    byte[] pageBitmap, int pageWidth, int pageHeight, int pageBytesPerRow,
    byte[] regionBitmap, int regionWidth, int regionHeight,
    int regionX, int regionY, int combinationOp
  ) {
    var regionBytesPerRow = (regionWidth + 7) / 8;

    for (var ry = 0; ry < regionHeight; ++ry) {
      var py = regionY + ry;
      if (py < 0 || py >= pageHeight)
        continue;

      for (var rx = 0; rx < regionWidth; ++rx) {
        var px = regionX + rx;
        if (px < 0 || px >= pageWidth)
          continue;

        var srcIdx = ry * regionBytesPerRow + (rx >> 3);
        if (srcIdx >= regionBitmap.Length)
          continue;

        var srcBit = (regionBitmap[srcIdx] >> (7 - (rx & 7))) & 1;
        var dstIdx = py * pageBytesPerRow + (px >> 3);
        var dstShift = 7 - (px & 7);
        var dstBit = (pageBitmap[dstIdx] >> dstShift) & 1;

        var combined = combinationOp switch {
          0 => dstBit | srcBit,       // OR
          1 => dstBit & srcBit,       // AND
          2 => dstBit ^ srcBit,       // XOR
          3 => ~(dstBit ^ srcBit) & 1, // XNOR
          4 => srcBit,                // REPLACE
          _ => dstBit | srcBit,
        };

        if (combined != 0)
          pageBitmap[dstIdx] |= (byte)(1 << dstShift);
        else
          pageBitmap[dstIdx] &= (byte)~(1 << dstShift);
      }
    }
  }

  private static int _ReadInt32BE(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
