using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Pdf.Filters;

/// <summary>The PDF <c>JBIG2Decode</c> filter: ITU-T T.88 embedded streams.</summary>
/// <remarks>
/// Converted from pdf.js <c>src/core/jbig2.js</c> and <c>src/core/arithmetic_decoder.js</c>
/// (Apache-2.0; see <c>THIRD-PARTY-NOTICE.pdfjs.txt</c> beside this file). The probability table,
/// the coding templates and the standard Huffman tables of Annex B are copied value for value.
/// <para/>
/// What pdf.js leaves out is filled in from the T.88 text itself: generic refinement region
/// segments and the typical prediction they can use (6.3.5.6), intermediate regions, the AND, XNOR
/// and REPLACE combination operators, halftone skipping (6.6.5.1), symbol dictionaries that reuse
/// the previous dictionary's coding statistics (7.4.2.2), stripes of a page whose height is not
/// known up front, and the long form of the referred-to segment count. Where pdf.js disagrees with
/// T.88 — it tests the whole flag byte for the long form, and stores an absolute position as the
/// length of a generic region of unknown length — T.88 wins.
/// <para/>
/// PDF embeds a JBIG2 page without the file header: the segments of the <c>JBIG2Globals</c> stream
/// come first, then the image's own, all decoded into one page. JBIG2 writes 1 for black; the PDF
/// filter's output is the other way round, 0 for black, so the page comes back inverted.
/// </remarks>
internal static class PdfJbig2Decoder {

  /// <summary>Decodes a <c>JBIG2Decode</c> stream.</summary>
  /// <param name="data">The image's own segments.</param>
  /// <param name="globals">The segments of the <c>JBIG2Globals</c> stream, or null.</param>
  /// <param name="width">The image's width, which sets the row length of the result.</param>
  /// <param name="height">The image's height, which sets the row count of the result.</param>
  /// <returns>
  /// Packed rows, most significant bit first, <c>(width + 7) / 8</c> bytes each, with 0 for black and
  /// 1 for white; whatever the page does not cover is white.
  /// </returns>
  /// <exception cref="NotSupportedException">The stream uses a JBIG2 feature this decoder does not implement.</exception>
  /// <exception cref="InvalidDataException">The stream is damaged.</exception>
  internal static byte[] Decode(byte[] data, byte[]? globals, int width, int height) {
    ArgumentNullException.ThrowIfNull(data);
    if (width < 0 || height < 0)
      throw new ArgumentOutOfRangeException(nameof(width), $"An image of {width}x{height} has no pixels.");

    var decoder = new _PageDecoder();
    try {
      if (globals is { Length: > 0 })
        decoder.Run(globals);

      decoder.Run(data);
    } catch (IndexOutOfRangeException failure) {
      throw new InvalidDataException("JBIG2 data ends inside a segment.", failure);
    } catch (ArgumentOutOfRangeException failure) {
      throw new InvalidDataException("JBIG2 data ends inside a segment.", failure);
    }

    var rowSize = (width + 7) >> 3;
    var output = new byte[rowSize * height];
    if (decoder.Page is { } page) {
      var copy = Math.Min(rowSize, page.RowSize);
      var rows = Math.Min(height, page.Height);
      for (var y = 0; y < rows; ++y)
        Array.Copy(page.Buffer, y * page.RowSize, output, y * rowSize, copy);
    }

    // JBIG2 has black as 1; the filter hands on black as 0.
    for (var i = 0; i < output.Length; ++i)
      output[i] ^= 0xFF;

    return output;
  }

  #region segments (T.88 7.2, 7.3)

  private const int _SymbolDictionary = 0;
  private const int _IntermediateTextRegion = 4;
  private const int _ImmediateTextRegion = 6;
  private const int _ImmediateLosslessTextRegion = 7;
  private const int _PatternDictionary = 16;
  private const int _IntermediateHalftoneRegion = 20;
  private const int _ImmediateHalftoneRegion = 22;
  private const int _ImmediateLosslessHalftoneRegion = 23;
  private const int _IntermediateGenericRegion = 36;
  private const int _ImmediateGenericRegion = 38;
  private const int _ImmediateLosslessGenericRegion = 39;
  private const int _IntermediateGenericRefinementRegion = 40;
  private const int _ImmediateGenericRefinementRegion = 42;
  private const int _ImmediateLosslessGenericRefinementRegion = 43;
  private const int _PageInformation = 48;
  private const int _EndOfPage = 49;
  private const int _EndOfStripe = 50;
  private const int _EndOfFile = 51;
  private const int _Profiles = 52;
  private const int _Tables = 53;
  private const int _Extension = 62;

  private const int _RegionInformationLength = 17;

  private sealed record _SegmentHeader(int Number, int Type, int[] ReferredTo, int PageAssociation, int Start, int End, long Length, bool UnknownLength);

  private readonly record struct _RegionInfo(int Width, int Height, int X, int Y, int CombinationOperator);

  private static _RegionInfo _ReadRegionInfo(byte[] data, int start)
    => new(_ReadInt32(data, start), _ReadInt32(data, start + 4), _ReadInt32(data, start + 8), _ReadInt32(data, start + 12), data[start + 16] & 7);

  /// <summary>The JBIG2 file header's signature (T.88 D.4.1).</summary>
  private static ReadOnlySpan<byte> _FileSignature => [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>
  /// Reads the segments of one embedded stream, which carry their data straight after each header.
  /// </summary>
  /// <remarks>
  /// PDF forbids the file header in an embedded stream, but some writers leave a whole JBIG2 file
  /// in there; pdf.js reads those and so does this. A file in the random-access organisation (D.2)
  /// has every segment header first and all their data after.
  /// </remarks>
  private static List<_SegmentHeader> _ReadSegments(byte[] data) {
    var position = 0;
    var randomAccess = false;
    if (data.AsSpan().StartsWith(_FileSignature) && data.Length > 8) {
      var flags = data[8];
      randomAccess = (flags & 1) == 0;
      position = 9 + ((flags & 2) == 0 ? 4 : 0);
    }

    var segments = new List<_SegmentHeader>();
    while (position < data.Length) {
      // Padding after the last segment is common enough not to be an error.
      if (data.Length - position < 11)
        break;

      var header = _ReadSegmentHeader(data, position);
      segments.Add(header);
      position = randomAccess ? header.Start : header.End;
      if (header.Type == _EndOfFile)
        break;
    }

    if (randomAccess)
      for (var i = 0; i < segments.Count; ++i) {
        var start = position;
        position = (int)Math.Min(position + segments[i].Length, data.Length);
        segments[i] = segments[i] with { Start = start, End = position };
      }

    return segments;
  }

  private static _SegmentHeader _ReadSegmentHeader(byte[] data, int start) {
    var number = _ReadInt32(data, start);
    var flags = data[start + 4];
    var type = flags & 0x3F;
    var pageAssociationIsLong = (flags & 0x40) != 0;

    var referredFlags = data[start + 5];
    var referredToCount = (referredFlags >> 5) & 7;
    var position = start + 6;
    if (referredToCount == 7) {
      // The long form: a 29-bit count, then one retain bit a referred segment plus one for this one.
      referredToCount = _ReadInt32(data, start + 5) & 0x1FFFFFFF;
      position = start + 9 + ((referredToCount + 8) >> 3);
    } else if (referredToCount is 5 or 6)
      throw new InvalidDataException($"JBIG2 segment {number} has an invalid referred-to segment count.");

    var numberSize = (uint)number <= 256 ? 1 : (uint)number <= 65536 ? 2 : 4;
    var referredTo = new int[referredToCount];
    for (var i = 0; i < referredToCount; ++i) {
      referredTo[i] = numberSize switch {
        1 => data[position],
        2 => _ReadUInt16(data, position),
        _ => _ReadInt32(data, position),
      };
      position += numberSize;
    }

    int pageAssociation;
    if (pageAssociationIsLong) {
      pageAssociation = _ReadInt32(data, position);
      position += 4;
    } else
      pageAssociation = data[position++];

    var length = (uint)_ReadInt32(data, position);
    position += 4;

    long end;
    if (length == 0xFFFFFFFF)
      end = _FindUnknownLengthEnd(data, number, type, position);
    else
      end = position + (long)length;

    var declared = end - position;
    if (end > data.Length)
      end = data.Length;

    return new(number, type, referredTo, pageAssociation, position, (int)end, declared, length == 0xFFFFFFFF);
  }

  /// <summary>
  /// The end of an immediate generic region whose length was not known when it was written (T.88
  /// 7.2.7): the data runs up to a marker followed by the region's row count.
  /// </summary>
  private static long _FindUnknownLengthEnd(byte[] data, int number, int type, int position) {
    if (type != _ImmediateGenericRegion)
      throw new InvalidDataException($"JBIG2 segment {number} of type {type} has an unknown length, which only an immediate generic region may have.");

    var info = _ReadRegionInfo(data, position);
    var mmr = (data[position + _RegionInformationLength] & 1) != 0;
    Span<byte> pattern = stackalloc byte[6];
    pattern[0] = mmr ? (byte)0x00 : (byte)0xFF;
    pattern[1] = mmr ? (byte)0x00 : (byte)0xAC;
    pattern[2] = (byte)(info.Height >>> 24);
    pattern[3] = (byte)(info.Height >> 16);
    pattern[4] = (byte)(info.Height >> 8);
    pattern[5] = (byte)info.Height;

    var at = data.AsSpan(position).IndexOf(pattern);
    if (at < 0)
      throw new InvalidDataException($"JBIG2 segment {number}: the end of the generic region of unknown length was not found.");

    return position + at + pattern.Length;
  }

  #endregion

  #region the page and the segment visitor

  /// <summary>The page buffer being built, one bit a pixel, 1 for black.</summary>
  private sealed class _Page(int width, int height, bool defaultPixel, int combinationOperator, bool combinationOperatorOverride) {
    public readonly int Width = width;
    public readonly int RowSize = (width + 7) >> 3;
    public readonly bool DefaultPixel = defaultPixel;
    public readonly int CombinationOperator = combinationOperator;
    public readonly bool CombinationOperatorOverride = combinationOperatorOverride;
    public int Height = height;
    public byte[] Buffer = _Allocate(width, height, defaultPixel);

    /// <summary>The largest page buffer, in bytes, so a page stating a silly size cannot exhaust memory.</summary>
    private const long _MaximumBytes = 1L << 28;

    private static byte[] _Allocate(int width, int height, bool defaultPixel) {
      if ((long)((width + 7) >> 3) * height > _MaximumBytes)
        throw new InvalidDataException($"A JBIG2 page of {width}x{height} is larger than this will decode.");

      var buffer = new byte[(long)((width + 7) >> 3) * height];
      if (defaultPixel)
        Array.Fill(buffer, (byte)0xFF);
      return buffer;
    }

    /// <summary>Grows a page of unknown height so it reaches the given row, filling with the default pixel.</summary>
    public void EnsureHeight(int rows) {
      if (rows <= this.Height)
        return;
      if ((long)this.RowSize * rows > _MaximumBytes)
        throw new InvalidDataException($"A JBIG2 page {rows} rows high is larger than this will decode.");

      var grown = new byte[(long)this.RowSize * rows];
      Array.Copy(this.Buffer, grown, this.Buffer.Length);
      if (this.DefaultPixel)
        grown.AsSpan(this.Buffer.Length).Fill(0xFF);

      this.Buffer = grown;
      this.Height = rows;
    }

    public bool GetPixel(int x, int y)
      => (uint)x < (uint)this.Width && (uint)y < (uint)this.Height && (this.Buffer[y * this.RowSize + (x >> 3)] & (0x80 >> (x & 7))) != 0;
  }

  private sealed class _PageDecoder {

    private readonly Dictionary<int, List<byte[][]>> _symbols = [];
    private readonly Dictionary<int, byte[][][]> _patterns = [];
    private readonly Dictionary<int, _HuffmanTable> _customTables = [];
    private readonly Dictionary<int, (_RegionInfo Info, byte[][] Bitmap)> _intermediate = [];
    private readonly Dictionary<int, (byte[] Generic, byte[] Refinement)> _retainedContexts = [];
    private bool _pageHeightUnknown;

    public _Page? Page { get; private set; }

    public void Run(byte[] data) {
      foreach (var segment in _ReadSegments(data))
        this._Process(segment, data);
    }

    private void _Process(_SegmentHeader header, byte[] data) {
      var position = header.Start;
      var end = header.End;
      switch (header.Type) {
        case _SymbolDictionary:
          this._OnSymbolDictionary(header, data, position, end);
          break;

        case _IntermediateTextRegion:
        case _ImmediateTextRegion:
        case _ImmediateLosslessTextRegion:
          this._OnTextRegion(header, data, position, end);
          break;

        case _PatternDictionary:
          this._OnPatternDictionary(header, data, position, end);
          break;

        case _IntermediateHalftoneRegion:
        case _ImmediateHalftoneRegion:
        case _ImmediateLosslessHalftoneRegion:
          this._OnHalftoneRegion(header, data, position, end);
          break;

        case _IntermediateGenericRegion:
        case _ImmediateGenericRegion:
        case _ImmediateLosslessGenericRegion:
          this._OnGenericRegion(header, data, position, end);
          break;

        case _IntermediateGenericRefinementRegion:
        case _ImmediateGenericRefinementRegion:
        case _ImmediateLosslessGenericRefinementRegion:
          this._OnRefinementRegion(header, data, position, end);
          break;

        case _PageInformation:
          this._OnPageInformation(data, position);
          break;

        case _EndOfStripe:
          // The row the stripe ends on, which fixes the height of a page that did not state one.
          if (this._pageHeightUnknown && this.Page != null && end - position >= 4)
            this.Page.EnsureHeight(_ReadInt32(data, position) + 1);
          break;

        case _Tables:
          this._customTables[header.Number] = _DecodeTablesSegment(data, position, end);
          break;

        case _EndOfPage:
        case _EndOfFile:
        case _Profiles:
        case _Extension:
          break;

        default:
          throw new NotSupportedException($"JBIG2 segment type {header.Type} (segment {header.Number}) is not supported.");
      }
    }

    private void _OnPageInformation(byte[] data, int position) {
      var width = _ReadInt32(data, position);
      var height = (uint)_ReadInt32(data, position + 4);
      var flags = data[position + 16];
      this._pageHeightUnknown = height == 0xFFFFFFFF;
      if (width <= 0 || width > 1 << 20)
        throw new InvalidDataException($"A JBIG2 page {width} pixels wide is not a page.");

      var rows = this._pageHeightUnknown ? 0 : (int)Math.Min(height, 1u << 20);
      this.Page = new(width, rows, ((flags >> 2) & 1) != 0, (flags >> 3) & 3, (flags & 64) != 0);
    }

    private _Page _RequirePage() => this.Page ?? throw new InvalidDataException("JBIG2 region before the page information segment.");

    /// <summary>Combines a region's bitmap into the page (T.88 7.4.1.5 and 6.2.5.10's operators).</summary>
    private void _Draw(_RegionInfo info, byte[][] bitmap) {
      var page = this._RequirePage();
      var height = Math.Min(info.Height, bitmap.Length);
      if (this._pageHeightUnknown)
        page.EnsureHeight(info.Y + height);

      var combination = page.CombinationOperatorOverride ? info.CombinationOperator : page.CombinationOperator;
      for (var i = 0; i < height; ++i) {
        var y = info.Y + i;
        if ((uint)y >= (uint)page.Height)
          continue;

        var row = bitmap[i];
        var width = Math.Min(info.Width, row.Length);
        var rowStart = y * page.RowSize;
        for (var j = 0; j < width; ++j) {
          var x = info.X + j;
          if ((uint)x >= (uint)page.Width)
            continue;

          var at = rowStart + (x >> 3);
          var mask = (byte)(0x80 >> (x & 7));
          var old = (page.Buffer[at] & mask) != 0;
          var bit = row[j] != 0;
          var result = combination switch {
            0 => old | bit,
            1 => old & bit,
            2 => old ^ bit,
            3 => !(old ^ bit),
            4 => bit,
            _ => throw new NotSupportedException($"JBIG2 combination operator {combination} is not supported."),
          };

          if (result)
            page.Buffer[at] |= mask;
          else
            page.Buffer[at] &= (byte)~mask;
        }
      }
    }

    private void _Emit(_SegmentHeader header, bool intermediate, _RegionInfo info, byte[][] bitmap) {
      if (intermediate)
        this._intermediate[header.Number] = (info, bitmap);
      else
        this._Draw(info, bitmap);
    }

    private List<byte[][]> _InputSymbols(int[] referredTo) {
      var input = new List<byte[][]>();
      foreach (var referred in referredTo)
        if (this._symbols.TryGetValue(referred, out var symbols))
          input.AddRange(symbols);

      return input;
    }

    private void _OnGenericRegion(_SegmentHeader header, byte[] data, int position, int end) {
      var info = _ReadRegionInfo(data, position);
      position += _RegionInformationLength;
      var flags = data[position++];
      var mmr = (flags & 1) != 0;
      var template = (flags >> 1) & 3;
      var prediction = (flags & 8) != 0;
      if ((flags & 16) != 0)
        throw new NotSupportedException("JBIG2 generic regions with the extended template are not supported.");

      _Point[] at = [];
      if (!mmr) {
        at = new _Point[template == 0 ? 4 : 1];
        for (var i = 0; i < at.Length; ++i, position += 2)
          at[i] = new((sbyte)data[position], (sbyte)data[position + 1]);
      }

      // A region of unknown length states its real row count in its last four bytes.
      var height = info.Height;
      if (header.UnknownLength && end - 6 >= position) {
        height = Math.Min(height, _ReadInt32(data, end - 4));
        end -= 6;
        info = info with { Height = height };
      }

      _CheckSize(info.Width, info.Height);
      var context = new _DecodingContext(data, position, end);
      var bitmap = _DecodeBitmap(mmr, info.Width, info.Height, template, prediction, null, at, context);
      this._Emit(header, header.Type == _IntermediateGenericRegion, info, bitmap);
    }

    private void _OnRefinementRegion(_SegmentHeader header, byte[] data, int position, int end) {
      var info = _ReadRegionInfo(data, position);
      position += _RegionInformationLength;
      var flags = data[position++];
      var template = flags & 1;
      var prediction = (flags & 2) != 0;
      var at = new _Point[2];
      if (template == 0)
        for (var i = 0; i < 2; ++i, position += 2)
          at[i] = new((sbyte)data[position], (sbyte)data[position + 1]);

      _CheckSize(info.Width, info.Height);

      // The reference is an intermediate region when one is named (7.4.7.4), the page otherwise.
      byte[][] reference;
      if (header.ReferredTo.Length > 0 && this._intermediate.TryGetValue(header.ReferredTo[0], out var referred)) {
        reference = referred.Bitmap;
        this._intermediate.Remove(header.ReferredTo[0]);
      } else {
        var page = this._RequirePage();
        reference = new byte[info.Height][];
        for (var i = 0; i < info.Height; ++i) {
          var row = reference[i] = new byte[info.Width];
          for (var j = 0; j < info.Width; ++j)
            row[j] = page.GetPixel(info.X + j, info.Y + i) ? (byte)1 : (byte)0;
        }
      }

      var context = new _DecodingContext(data, position, end);
      var bitmap = _DecodeRefinement(info.Width, info.Height, template, reference, 0, 0, prediction, at, context);
      this._Emit(header, header.Type == _IntermediateGenericRefinementRegion, info, bitmap);
    }

    private void _OnSymbolDictionary(_SegmentHeader header, byte[] data, int position, int end) {
      var flags = _ReadUInt16(data, position);
      position += 2;
      var huffman = (flags & 1) != 0;
      var refinement = (flags & 2) != 0;
      var deltaHeightSelector = (flags >> 2) & 3;
      var deltaWidthSelector = (flags >> 4) & 3;
      var bitmapSizeSelector = (flags >> 6) & 1;
      var aggregationInstancesSelector = (flags >> 7) & 1;
      var contextUsed = (flags & 256) != 0;
      var contextRetained = (flags & 512) != 0;
      var template = (flags >> 10) & 3;
      var refinementTemplate = (flags >> 12) & 1;

      _Point[] at = [];
      if (!huffman) {
        at = new _Point[template == 0 ? 4 : 1];
        for (var i = 0; i < at.Length; ++i, position += 2)
          at[i] = new((sbyte)data[position], (sbyte)data[position + 1]);
      }

      var refinementAt = new _Point[2];
      if (refinement && refinementTemplate == 0)
        for (var i = 0; i < 2; ++i, position += 2)
          refinementAt[i] = new((sbyte)data[position], (sbyte)data[position + 1]);

      var exported = _ReadInt32(data, position);
      position += 4;
      var newSymbols = _ReadInt32(data, position);
      position += 4;
      if (newSymbols < 0 || exported < 0)
        throw new InvalidDataException($"JBIG2 symbol dictionary {header.Number} has a negative symbol count.");

      _SymbolDictionaryTables? tables = null;
      _BitReader? huffmanInput = null;
      if (huffman) {
        tables = _SymbolDictionaryHuffmanTables(deltaHeightSelector, deltaWidthSelector, bitmapSizeSelector, aggregationInstancesSelector, header.ReferredTo, this._customTables);
        huffmanInput = new(data, position, end);
      }

      var context = new _DecodingContext(data, position, end);

      // 7.4.2.2: the coding statistics can carry over from the last dictionary referred to.
      if (contextUsed) {
        var carried = false;
        for (var i = header.ReferredTo.Length - 1; i >= 0 && !carried; --i)
          if (this._retainedContexts.TryGetValue(header.ReferredTo[i], out var retained)) {
            retained.Generic.CopyTo(context.Cache.Get("GB"), 0);
            retained.Refinement.CopyTo(context.Cache.Get("GR"), 0);
            carried = true;
          }

        if (!carried)
          throw new InvalidDataException($"JBIG2 symbol dictionary {header.Number} reuses coding statistics no dictionary it refers to retained.");
      }

      this._symbols[header.Number] = _DecodeSymbolDictionary(
        huffman, refinement, this._InputSymbols(header.ReferredTo), newSymbols, exported, tables,
        template, at, refinementTemplate, refinementAt, context, huffmanInput);

      if (contextRetained)
        this._retainedContexts[header.Number] = ((byte[])context.Cache.Get("GB").Clone(), (byte[])context.Cache.Get("GR").Clone());
    }

    private void _OnTextRegion(_SegmentHeader header, byte[] data, int position, int end) {
      var info = _ReadRegionInfo(data, position);
      position += _RegionInformationLength;
      var flags = _ReadUInt16(data, position);
      position += 2;
      var huffman = (flags & 1) != 0;
      var refinement = (flags & 2) != 0;
      var logStripSize = (flags >> 2) & 3;
      var stripSize = 1 << logStripSize;
      var referenceCorner = (flags >> 4) & 3;
      var transposed = (flags & 64) != 0;
      var combinationOperator = (flags >> 7) & 3;
      var defaultPixel = (flags >> 9) & 1;
      var dsOffset = (int)((uint)flags << 17) >> 27;
      var refinementTemplate = (flags >> 15) & 1;

      int fsSelector = 0, dsSelector = 0, dtSelector = 0;
      if (huffman) {
        var huffmanFlags = _ReadUInt16(data, position);
        position += 2;
        fsSelector = huffmanFlags & 3;
        dsSelector = (huffmanFlags >> 2) & 3;
        dtSelector = (huffmanFlags >> 4) & 3;
      }

      var refinementAt = new _Point[2];
      if (refinement && refinementTemplate == 0)
        for (var i = 0; i < 2; ++i, position += 2)
          refinementAt[i] = new((sbyte)data[position], (sbyte)data[position + 1]);

      var instances = _ReadInt32(data, position);
      position += 4;

      var symbols = this._InputSymbols(header.ReferredTo);
      var symbolCodeLength = _Log2(symbols.Count);

      _TextRegionTables? tables = null;
      _BitReader? huffmanInput = null;
      if (huffman) {
        if (refinement)
          throw new NotSupportedException("JBIG2 Huffman-coded text regions with refinement are not supported.");

        huffmanInput = new(data, position, end);
        tables = _TextRegionHuffmanTables(fsSelector, dsSelector, dtSelector, header.ReferredTo, this._customTables, symbols.Count, huffmanInput);
      }

      _CheckSize(info.Width, info.Height);
      var context = new _DecodingContext(data, position, end);
      var bitmap = _DecodeTextRegion(
        huffman, refinement, info.Width, info.Height, defaultPixel, instances, stripSize, symbols, symbolCodeLength,
        transposed, dsOffset, referenceCorner, combinationOperator, tables, refinementTemplate, refinementAt,
        context, logStripSize, huffmanInput);
      this._Emit(header, header.Type == _IntermediateTextRegion, info, bitmap);
    }

    private void _OnPatternDictionary(_SegmentHeader header, byte[] data, int position, int end) {
      var flags = data[position++];
      var mmr = (flags & 1) != 0;
      var template = (flags >> 1) & 3;
      int patternWidth = data[position++], patternHeight = data[position++];
      var maxPatternIndex = _ReadInt32(data, position);
      position += 4;
      if (patternWidth == 0 || patternHeight == 0 || maxPatternIndex < 0 || (long)(maxPatternIndex + 1L) * patternWidth > 1 << 24)
        throw new InvalidDataException($"JBIG2 pattern dictionary {header.Number} has an impossible size.");

      var context = new _DecodingContext(data, position, end);
      this._patterns[header.Number] = _DecodePatternDictionary(mmr, patternWidth, patternHeight, maxPatternIndex, template, context);
    }

    private void _OnHalftoneRegion(_SegmentHeader header, byte[] data, int position, int end) {
      var info = _ReadRegionInfo(data, position);
      position += _RegionInformationLength;
      var flags = data[position++];
      var mmr = (flags & 1) != 0;
      var template = (flags >> 1) & 3;
      var enableSkip = (flags & 8) != 0;
      var combinationOperator = (flags >> 4) & 7;
      var defaultPixel = (flags >> 7) & 1;
      var gridWidth = _ReadInt32(data, position);
      var gridHeight = _ReadInt32(data, position + 4);
      var gridOffsetX = _ReadInt32(data, position + 8);
      var gridOffsetY = _ReadInt32(data, position + 12);
      var gridVectorX = _ReadUInt16(data, position + 16);
      var gridVectorY = _ReadUInt16(data, position + 18);
      position += 20;

      if (header.ReferredTo.Length == 0 || !this._patterns.TryGetValue(header.ReferredTo[0], out var patterns))
        throw new InvalidDataException($"JBIG2 halftone region {header.Number} refers to no pattern dictionary.");

      _CheckSize(info.Width, info.Height);
      _CheckSize(gridWidth, gridHeight);
      var context = new _DecodingContext(data, position, end);
      var bitmap = _DecodeHalftoneRegion(
        mmr, patterns, template, info.Width, info.Height, defaultPixel, enableSkip, combinationOperator,
        gridWidth, gridHeight, gridOffsetX, gridOffsetY, gridVectorX, gridVectorY, context);
      this._Emit(header, header.Type == _IntermediateHalftoneRegion, info, bitmap);
    }
  }

  private static void _CheckSize(int width, int height) {
    if (width < 0 || height < 0 || (long)width * height > 1L << 28)
      throw new InvalidDataException($"A JBIG2 region of {width}x{height} is not a region.");
  }

  #endregion

  #region arithmetic decoding (T.88 Annex E, A)

  /// <summary>The MQ coder's probability estimation table (T.88 Table E.1).</summary>
  private static readonly (ushort Qe, byte Nmps, byte Nlps, bool Switch)[] _QeTable = [
    (0x5601, 1, 1, true), (0x3401, 2, 6, false), (0x1801, 3, 9, false), (0x0AC1, 4, 12, false),
    (0x0521, 5, 29, false), (0x0221, 38, 33, false), (0x5601, 7, 6, true), (0x5401, 8, 14, false),
    (0x4801, 9, 14, false), (0x3801, 10, 14, false), (0x3001, 11, 17, false), (0x2401, 12, 18, false),
    (0x1C01, 13, 20, false), (0x1601, 29, 21, false), (0x5601, 15, 14, true), (0x5401, 16, 14, false),
    (0x5101, 17, 15, false), (0x4801, 18, 16, false), (0x3801, 19, 17, false), (0x3401, 20, 18, false),
    (0x3001, 21, 19, false), (0x2801, 22, 19, false), (0x2401, 23, 20, false), (0x2201, 24, 21, false),
    (0x1C01, 25, 22, false), (0x1801, 26, 23, false), (0x1601, 27, 24, false), (0x1401, 28, 25, false),
    (0x1201, 29, 26, false), (0x1101, 30, 27, false), (0x0AC1, 31, 28, false), (0x09C1, 32, 29, false),
    (0x08A1, 33, 30, false), (0x0521, 34, 31, false), (0x0441, 35, 32, false), (0x02A1, 36, 33, false),
    (0x0221, 37, 34, false), (0x0141, 38, 35, false), (0x0111, 39, 36, false), (0x0085, 40, 37, false),
    (0x0049, 41, 38, false), (0x0025, 42, 39, false), (0x0015, 43, 40, false), (0x0009, 44, 41, false),
    (0x0005, 45, 42, false), (0x0001, 45, 43, false), (0x5601, 46, 46, false),
  ];

  /// <summary>The MQ arithmetic decoder, with each context packed as its state index and its MPS bit.</summary>
  private sealed class _ArithmeticDecoder {
    private readonly byte[] _data;
    private readonly int _end;
    private int _bp;
    private int _chigh, _clow, _ct, _a;

    public _ArithmeticDecoder(byte[] data, int start, int end) {
      this._data = data;
      this._bp = start;
      this._end = end;
      this._chigh = this._At(start) & 0xFF;
      this._clow = 0;
      this._ByteIn();
      this._chigh = ((this._chigh << 7) & 0xFFFF) | ((this._clow >> 9) & 0x7F);
      this._clow = (this._clow << 7) & 0xFFFF;
      this._ct -= 7;
      this._a = 0x8000;
    }

    /// <summary>A byte of the data, or -1 past the array, which no test below takes for a marker.</summary>
    private int _At(int index) => (uint)index < (uint)this._data.Length ? this._data[index] : -1;

    private void _ByteIn() {
      var bp = this._bp;
      if (this._At(bp) == 0xFF) {
        if (this._At(bp + 1) > 0x8F) {
          this._clow += 0xFF00;
          this._ct = 8;
        } else {
          ++bp;
          this._clow += (this._At(bp) & 0xFF) << 9;
          this._ct = 7;
          this._bp = bp;
        }
      } else {
        ++bp;
        this._clow += bp < this._end ? (this._At(bp) & 0xFF) << 8 : 0xFF00;
        this._ct = 8;
        this._bp = bp;
      }

      if (this._clow > 0xFFFF) {
        this._chigh += this._clow >> 16;
        this._clow &= 0xFFFF;
      }
    }

    public int ReadBit(byte[] contexts, int position) {
      var index = contexts[position] >> 1;
      var mps = contexts[position] & 1;
      var entry = _QeTable[index];
      var qe = entry.Qe;
      int d;
      var a = this._a - qe;

      if (this._chigh < qe) {
        if (a < qe) {
          a = qe;
          d = mps;
          index = entry.Nmps;
        } else {
          a = qe;
          d = 1 ^ mps;
          if (entry.Switch)
            mps = d;
          index = entry.Nlps;
        }
      } else {
        this._chigh -= qe;
        if ((a & 0x8000) != 0) {
          this._a = a;
          return mps;
        }

        if (a < qe) {
          d = 1 ^ mps;
          if (entry.Switch)
            mps = d;
          index = entry.Nlps;
        } else {
          d = mps;
          index = entry.Nmps;
        }
      }

      do {
        if (this._ct == 0)
          this._ByteIn();

        a <<= 1;
        this._chigh = ((this._chigh << 1) & 0xFFFF) | ((this._clow >> 15) & 1);
        this._clow = (this._clow << 1) & 0xFFFF;
        --this._ct;
      } while ((a & 0x8000) == 0);

      this._a = a;
      contexts[position] = (byte)((index << 1) | mps);
      return d;
    }
  }

  /// <summary>One context array per decoding procedure, made when first asked for.</summary>
  private sealed class _ContextCache {
    private readonly Dictionary<string, byte[]> _contexts = [];

    public byte[] Get(string id) {
      if (!this._contexts.TryGetValue(id, out var contexts))
        this._contexts[id] = contexts = new byte[1 << 16];
      return contexts;
    }
  }

  /// <summary>A segment's coded data, with the arithmetic decoder and its contexts made when first used.</summary>
  private sealed class _DecodingContext(byte[] data, int start, int end) {
    private _ArithmeticDecoder? _decoder;
    private _ContextCache? _cache;

    public byte[] Data { get; } = data;
    public int Start { get; } = start;
    public int End { get; } = end;
    public _ArithmeticDecoder Decoder => this._decoder ??= new(this.Data, this.Start, this.End);
    public _ContextCache Cache => this._cache ??= new();
  }

  /// <summary>The integer decoding procedure (A.2); null is the out-of-band value.</summary>
  private static int? _DecodeInteger(_ContextCache cache, string procedure, _ArithmeticDecoder decoder) {
    var contexts = cache.Get(procedure);
    var prev = 1;

    long ReadBits(int length) {
      long v = 0;
      for (var i = 0; i < length; ++i) {
        var bit = decoder.ReadBit(contexts, prev);
        prev = prev < 256 ? (prev << 1) | bit : (((prev << 1) | bit) & 511) | 256;
        v = (v << 1) | (uint)bit;
      }

      return v;
    }

    var sign = ReadBits(1);
    var value = ReadBits(1) != 0
      ? ReadBits(1) != 0
        ? ReadBits(1) != 0
          ? ReadBits(1) != 0
            ? ReadBits(1) != 0
              ? ReadBits(32) + 4436
              : ReadBits(12) + 340
            : ReadBits(8) + 84
          : ReadBits(6) + 20
        : ReadBits(4) + 4
      : ReadBits(2);

    if (sign != 0) {
      if (value == 0)
        return null;
      value = -value;
    }

    return value is >= int.MinValue and <= int.MaxValue ? (int)value : null;
  }

  private static int _RequireInteger(_ContextCache cache, string procedure, _ArithmeticDecoder decoder)
    => _DecodeInteger(cache, procedure, decoder) ?? throw new InvalidDataException($"JBIG2 {procedure} value is out of band where a number is required.");

  /// <summary>The symbol ID decoding procedure (A.3).</summary>
  private static int _DecodeIaid(_ContextCache cache, _ArithmeticDecoder decoder, int codeLength) {
    var contexts = cache.Get("IAID");
    var prev = 1;
    for (var i = 0; i < codeLength; ++i)
      prev = (prev << 1) | decoder.ReadBit(contexts, prev);

    return codeLength < 31 ? prev & ((1 << codeLength) - 1) : prev & 0x7FFFFFFF;
  }

  #endregion

  #region generic and refinement region decoding (T.88 6.2, 6.3)

  private readonly record struct _Point(int X, int Y);

  private static readonly _Point[][] _CodingTemplates = [
    [new(-1, -2), new(0, -2), new(1, -2), new(-2, -1), new(-1, -1), new(0, -1), new(1, -1), new(2, -1), new(-4, 0), new(-3, 0), new(-2, 0), new(-1, 0)],
    [new(-1, -2), new(0, -2), new(1, -2), new(2, -2), new(-2, -1), new(-1, -1), new(0, -1), new(1, -1), new(2, -1), new(-3, 0), new(-2, 0), new(-1, 0)],
    [new(-1, -2), new(0, -2), new(1, -2), new(-2, -1), new(-1, -1), new(0, -1), new(1, -1), new(-2, 0), new(-1, 0)],
    [new(-3, -1), new(-2, -1), new(-1, -1), new(0, -1), new(1, -1), new(-4, 0), new(-3, 0), new(-2, 0), new(-1, 0)],
  ];

  private static readonly (_Point[] Coding, _Point[] Reference)[] _RefinementTemplates = [
    ([new(0, -1), new(1, -1), new(-1, 0)], [new(0, -1), new(1, -1), new(-1, 0), new(0, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)]),
    ([new(-1, -1), new(0, -1), new(1, -1), new(-1, 0)], [new(0, -1), new(-1, 0), new(0, 0), new(1, 0), new(0, 1), new(1, 1)]),
  ];

  /// <summary>The contexts of the typical-prediction pseudo pixel for each generic template (6.2.5.7).</summary>
  private static readonly int[] _ReusedContexts = [0x9B25, 0x0795, 0x00E5, 0x0195];

  /// <summary>The contexts of the typical-prediction pseudo pixel for each refinement template (6.3.5.6).</summary>
  private static readonly int[] _RefinementReusedContexts = [0x0020, 0x0008];

  private static byte[][] _DecodeBitmapTemplate0(int width, int height, _DecodingContext context) {
    var decoder = context.Decoder;
    var contexts = context.Cache.Get("GB");
    var bitmap = new byte[height][];

    // 01111 0111111 0111: the pixels that stay in the template as it moves one to the right.
    const int oldPixelMask = 0x7BF7;

    for (var i = 0; i < height; ++i) {
      var row = bitmap[i] = new byte[width];
      var row1 = i < 1 ? row : bitmap[i - 1];
      var row2 = i < 2 ? row : bitmap[i - 2];

      var contextLabel = (_PixelOf(row2, 0) << 13) | (_PixelOf(row2, 1) << 12) | (_PixelOf(row2, 2) << 11)
        | (_PixelOf(row1, 0) << 7) | (_PixelOf(row1, 1) << 6) | (_PixelOf(row1, 2) << 5) | (_PixelOf(row1, 3) << 4);

      for (var j = 0; j < width; ++j) {
        var pixel = decoder.ReadBit(contexts, contextLabel);
        row[j] = (byte)pixel;
        contextLabel = ((contextLabel & oldPixelMask) << 1)
          | (j + 3 < width ? row2[j + 3] << 11 : 0)
          | (j + 4 < width ? row1[j + 4] << 4 : 0)
          | pixel;
      }
    }

    return bitmap;
  }

  private static int _PixelOf(byte[] row, int x) => x < row.Length ? row[x] : 0;

  /// <summary>The generic region decoding procedure (6.2).</summary>
  private static byte[][] _DecodeBitmap(bool mmr, int width, int height, int templateIndex, bool prediction, byte[][]? skip, _Point[] at, _DecodingContext context) {
    if (mmr)
      return PdfCcittFaxDecoder.DecodeMmrBitmap(context.Data, context.Start, context.End, width, height, false, out _);

    // The nominal adaptive pixels of template 0 have a faster loop.
    if (templateIndex == 0 && skip == null && !prediction && at.Length == 4
        && at[0] == new _Point(3, -1) && at[1] == new _Point(-3, -1) && at[2] == new _Point(2, -2) && at[3] == new _Point(-2, -2))
      return _DecodeBitmapTemplate0(width, height, context);

    var template = new List<_Point>(_CodingTemplates[templateIndex]);
    template.AddRange(at);

    // Not part of T.88, but it lets more of the label carry over from one pixel to the next.
    template.Sort(static (a, b) => a.Y != b.Y ? a.Y - b.Y : a.X - b.X);

    var templateLength = template.Count;
    var templateX = new int[templateLength];
    var templateY = new int[templateLength];
    var changing = new List<int>();
    int reuseMask = 0, minX = 0, maxX = 0, minY = 0;

    for (var k = 0; k < templateLength; ++k) {
      templateX[k] = template[k].X;
      templateY[k] = template[k].Y;
      minX = Math.Min(minX, template[k].X);
      maxX = Math.Max(maxX, template[k].X);
      minY = Math.Min(minY, template[k].Y);
      if (k < templateLength - 1 && template[k].Y == template[k + 1].Y && template[k].X == template[k + 1].X - 1)
        reuseMask |= 1 << (templateLength - 1 - k);
      else
        changing.Add(k);
    }

    var changingLength = changing.Count;
    var changingX = new int[changingLength];
    var changingY = new int[changingLength];
    var changingBit = new int[changingLength];
    for (var c = 0; c < changingLength; ++c) {
      var k = changing[c];
      changingX[c] = template[k].X;
      changingY[c] = template[k].Y;
      changingBit[c] = 1 << (templateLength - 1 - k);
    }

    // Inside this box every template pixel lies within the bitmap.
    var safeLeft = -minX;
    var safeTop = -minY;
    var safeRight = width - maxX;

    var pseudoPixelContext = _ReusedContexts[templateIndex];
    var row = new byte[width];
    var bitmap = new byte[height][];
    var decoder = context.Decoder;
    var contexts = context.Cache.Get("GB");

    var ltp = 0;
    var contextLabel = 0;
    for (var i = 0; i < height; ++i) {
      if (prediction) {
        ltp ^= decoder.ReadBit(contexts, pseudoPixelContext);
        if (ltp != 0) {
          // A typical row is a copy of the one above.
          bitmap[i] = row;
          continue;
        }
      }

      row = (byte[])row.Clone();
      bitmap[i] = row;
      for (var j = 0; j < width; ++j) {
        if (skip != null && skip[i][j] != 0) {
          row[j] = 0;
          continue;
        }

        if (j >= safeLeft && j < safeRight && i >= safeTop) {
          contextLabel = (contextLabel << 1) & reuseMask;
          for (var k = 0; k < changingLength; ++k)
            if (bitmap[i + changingY[k]][j + changingX[k]] != 0)
              contextLabel |= changingBit[k];
        } else {
          contextLabel = 0;
          var shift = templateLength - 1;
          for (var k = 0; k < templateLength; ++k, --shift) {
            var j0 = j + templateX[k];
            if (j0 < 0 || j0 >= width)
              continue;

            var i0 = i + templateY[k];
            if (i0 >= 0 && bitmap[i0][j0] != 0)
              contextLabel |= 1 << shift;
          }
        }

        row[j] = (byte)decoder.ReadBit(contexts, contextLabel);
      }
    }

    return bitmap;
  }

  /// <summary>The generic refinement region decoding procedure (6.3), with typical prediction (6.3.5.6).</summary>
  private static byte[][] _DecodeRefinement(int width, int height, int templateIndex, byte[][] referenceBitmap, int offsetX, int offsetY, bool prediction, _Point[] at, _DecodingContext context) {
    var (coding, referenceTemplate) = _RefinementTemplates[templateIndex];
    if (templateIndex == 0) {
      coding = [.. coding, at[0]];
      referenceTemplate = [.. referenceTemplate, at[1]];
    }

    var referenceHeight = referenceBitmap.Length;
    var referenceWidth = referenceHeight > 0 ? referenceBitmap[0].Length : 0;
    var pseudoPixelContext = _RefinementReusedContexts[templateIndex];
    var bitmap = new byte[height][];
    var decoder = context.Decoder;
    var contexts = context.Cache.Get("GR");

    int Reference(int y, int x) => (uint)y < (uint)referenceHeight && (uint)x < (uint)referenceWidth ? referenceBitmap[y][x] : 0;

    var ltp = 0;
    for (var i = 0; i < height; ++i) {
      if (prediction)
        ltp ^= decoder.ReadBit(contexts, pseudoPixelContext);

      var row = bitmap[i] = new byte[width];
      for (var j = 0; j < width; ++j) {
        if (ltp != 0) {
          // Where the reference is uniform around the pixel, the pixel is the reference (TPGRON).
          var centreY = i - offsetY;
          var centreX = j - offsetX;
          var first = Reference(centreY - 1, centreX - 1);
          var uniform = true;
          for (var dy = -1; dy <= 1 && uniform; ++dy)
            for (var dx = -1; dx <= 1 && uniform; ++dx)
              uniform = Reference(centreY + dy, centreX + dx) == first;

          if (uniform) {
            row[j] = (byte)first;
            continue;
          }
        }

        var contextLabel = 0;
        foreach (var point in coding) {
          var i0 = i + point.Y;
          var j0 = j + point.X;
          contextLabel <<= 1;
          if (i0 >= 0 && j0 >= 0 && j0 < width && i0 <= i)
            contextLabel |= bitmap[i0][j0];
        }

        foreach (var point in referenceTemplate) {
          contextLabel <<= 1;
          contextLabel |= Reference(i + point.Y - offsetY, j + point.X - offsetX);
        }

        row[j] = (byte)decoder.ReadBit(contexts, contextLabel);
      }
    }

    return bitmap;
  }

  #endregion

  #region symbol dictionaries and text regions (T.88 6.4, 6.5)

  private static List<byte[][]> _DecodeSymbolDictionary(
    bool huffman, bool refinement, List<byte[][]> symbols, int numberOfNewSymbols, int numberOfExportedSymbols,
    _SymbolDictionaryTables? huffmanTables, int templateIndex, _Point[] at, int refinementTemplateIndex,
    _Point[] refinementAt, _DecodingContext context, _BitReader? huffmanInput) {
    if (huffman && refinement)
      throw new NotSupportedException("JBIG2 Huffman-coded symbol dictionaries with refinement are not supported.");

    var newSymbols = new List<byte[][]>();
    var currentHeight = 0;
    var symbolCodeLength = _Log2(symbols.Count + numberOfNewSymbols);
    var decoder = context.Decoder;
    var cache = context.Cache;
    List<int>? symbolWidths = null;
    if (huffman) {
      symbolWidths = [];
      symbolCodeLength = Math.Max(symbolCodeLength, 1);
    }

    while (newSymbols.Count < numberOfNewSymbols) {
      var deltaHeight = huffman
        ? huffmanTables!.DeltaHeight.Decode(huffmanInput!)
        : _DecodeInteger(cache, "IADH", decoder);
      currentHeight += deltaHeight ?? throw new InvalidDataException("JBIG2 symbol dictionary height class is out of band.");
      if (currentHeight < 0 || currentHeight > 1 << 16)
        throw new InvalidDataException($"A JBIG2 symbol {currentHeight} pixels high is not a symbol.");

      int currentWidth = 0, totalWidth = 0;
      var firstSymbol = huffman ? symbolWidths!.Count : 0;
      while (true) {
        var deltaWidth = huffman
          ? huffmanTables!.DeltaWidth.Decode(huffmanInput!)
          : _DecodeInteger(cache, "IADW", decoder);
        if (deltaWidth == null)
          break;

        currentWidth += deltaWidth.Value;
        totalWidth += currentWidth;
        if (currentWidth < 0 || totalWidth < 0 || currentWidth > 1 << 16)
          throw new InvalidDataException($"A JBIG2 symbol {currentWidth} pixels wide is not a symbol.");

        if (newSymbols.Count + (symbolWidths?.Count ?? 0) - firstSymbol >= numberOfNewSymbols)
          throw new InvalidDataException("JBIG2 symbol dictionary holds more symbols than it declares.");

        if (refinement) {
          // 6.5.8.2: a refinement or an aggregate of symbols already known.
          var instances = _RequireInteger(cache, "IAAI", decoder);
          byte[][] bitmap;
          if (instances > 1) {
            bitmap = _DecodeTextRegion(
              false, true, currentWidth, currentHeight, 0, instances, 1, [.. symbols, .. newSymbols], symbolCodeLength,
              false, 0, 1, 0, null, refinementTemplateIndex, refinementAt, context, 0, null);
          } else {
            var symbolId = _DecodeIaid(cache, decoder, symbolCodeLength);
            var rdx = _RequireInteger(cache, "IARDX", decoder);
            var rdy = _RequireInteger(cache, "IARDY", decoder);
            var symbol = symbolId < symbols.Count
              ? symbols[symbolId]
              : symbolId - symbols.Count < newSymbols.Count
                ? newSymbols[symbolId - symbols.Count]
                : throw new InvalidDataException($"JBIG2 symbol {symbolId} does not exist yet.");
            bitmap = _DecodeRefinement(currentWidth, currentHeight, refinementTemplateIndex, symbol, rdx, rdy, false, refinementAt, context);
          }

          newSymbols.Add(bitmap);
        } else if (huffman)
          symbolWidths!.Add(currentWidth);
        else
          newSymbols.Add(_DecodeBitmap(false, currentWidth, currentHeight, templateIndex, false, null, at, context));
      }

      if (!huffman || refinement)
        continue;

      // 6.5.9: a Huffman-coded height class shares one bitmap, cut apart by the symbol widths.
      var input = huffmanInput!;
      var bitmapSize = huffmanTables!.BitmapSize.Decode(input) ?? throw new InvalidDataException("JBIG2 collective bitmap size is out of band.");
      input.ByteAlign();
      byte[][] collective;
      if (bitmapSize == 0)
        collective = _ReadUncompressedBitmap(input, totalWidth, currentHeight);
      else {
        var bitmapEnd = input.Position + bitmapSize;
        collective = PdfCcittFaxDecoder.DecodeMmrBitmap(input.Data, input.Position, Math.Min(bitmapEnd, input.End), totalWidth, currentHeight, false, out _);
        input.Position = bitmapEnd;
      }

      var decoded = symbolWidths!.Count;
      var xMin = 0;
      for (var i = firstSymbol; i < decoded; ++i) {
        var symbolWidth = symbolWidths[i];
        var symbol = new byte[currentHeight][];
        for (var y = 0; y < currentHeight; ++y)
          symbol[y] = collective[y].AsSpan(xMin, symbolWidth).ToArray();

        newSymbols.Add(symbol);
        xMin += symbolWidth;
      }
    }

    // 6.5.10: which of the input and new symbols the dictionary exports.
    var totalSymbols = symbols.Count + numberOfNewSymbols;
    var flags = new bool[totalSymbols];
    var flagCount = 0;
    var currentFlag = false;
    var tableB1 = huffman ? _StandardTable(1) : null;
    while (flagCount < totalSymbols) {
      var runLength = huffman ? tableB1!.Decode(huffmanInput!) : _DecodeInteger(cache, "IAEX", decoder);
      if (runLength is not { } run || run < 0)
        throw new InvalidDataException("JBIG2 exported-symbol run is out of range.");

      // A run past the last symbol is an encoder's slip, not a reason to lose the page.
      run = Math.Min(run, totalSymbols - flagCount);

      flags.AsSpan(flagCount, run).Fill(currentFlag);
      flagCount += run;
      currentFlag = !currentFlag;
    }

    var exported = new List<byte[][]>(numberOfExportedSymbols);
    for (var i = 0; i < symbols.Count; ++i)
      if (flags[i])
        exported.Add(symbols[i]);

    for (var j = 0; j < newSymbols.Count; ++j)
      if (flags[symbols.Count + j])
        exported.Add(newSymbols[j]);

    return exported;
  }

  /// <summary>The text region decoding procedure (6.4).</summary>
  private static byte[][] _DecodeTextRegion(
    bool huffman, bool refinement, int width, int height, int defaultPixelValue, int numberOfSymbolInstances,
    int stripSize, List<byte[][]> inputSymbols, int symbolCodeLength, bool transposed, int dsOffset,
    int referenceCorner, int combinationOperator, _TextRegionTables? huffmanTables, int refinementTemplateIndex,
    _Point[] refinementAt, _DecodingContext context, int logStripSize, _BitReader? huffmanInput) {
    if (huffman && refinement)
      throw new NotSupportedException("JBIG2 Huffman-coded text regions with refinement are not supported.");

    var bitmap = new byte[height][];
    for (var i = 0; i < height; ++i) {
      bitmap[i] = new byte[width];
      if (defaultPixelValue != 0)
        Array.Fill(bitmap[i], (byte)1);
    }

    var decoder = context.Decoder;
    var cache = context.Cache;

    int ReadInteger(_HuffmanTable? table, string procedure)
      => (huffman ? table!.Decode(huffmanInput!) : _DecodeInteger(cache, procedure, decoder))
        ?? throw new InvalidDataException($"JBIG2 {procedure} value is out of band where a number is required.");

    var stripT = -ReadInteger(huffmanTables?.DeltaT, "IADT");
    var firstS = 0;
    var instance = 0;
    while (instance < numberOfSymbolInstances) {
      stripT += ReadInteger(huffmanTables?.DeltaT, "IADT");
      firstS += ReadInteger(huffmanTables?.FirstS, "IAFS");
      var currentS = firstS;
      while (true) {
        var currentT = 0;
        if (stripSize > 1)
          currentT = huffman ? huffmanInput!.ReadBits(logStripSize) : _RequireInteger(cache, "IAIT", decoder);

        var t = stripSize * stripT + currentT;
        var symbolId = huffman
          ? huffmanTables!.SymbolId.Decode(huffmanInput!) ?? throw new InvalidDataException("JBIG2 symbol ID is out of band.")
          : _DecodeIaid(cache, decoder, symbolCodeLength);
        if ((uint)symbolId >= (uint)inputSymbols.Count)
          throw new InvalidDataException($"JBIG2 text region uses symbol {symbolId} of {inputSymbols.Count}.");

        var applyRefinement = refinement && (huffman ? huffmanInput!.ReadBit() != 0 : _RequireInteger(cache, "IARI", decoder) != 0);
        var symbolBitmap = inputSymbols[symbolId];
        var symbolHeight = symbolBitmap.Length;
        var symbolWidth = symbolHeight > 0 ? symbolBitmap[0].Length : 0;
        if (applyRefinement) {
          var rdw = _RequireInteger(cache, "IARDW", decoder);
          var rdh = _RequireInteger(cache, "IARDH", decoder);
          var rdx = _RequireInteger(cache, "IARDX", decoder);
          var rdy = _RequireInteger(cache, "IARDY", decoder);
          symbolWidth += rdw;
          symbolHeight += rdh;
          _CheckSize(symbolWidth, symbolHeight);
          symbolBitmap = _DecodeRefinement(symbolWidth, symbolHeight, refinementTemplateIndex, symbolBitmap, (rdw >> 1) + rdx, (rdh >> 1) + rdy, false, refinementAt, context);
        }

        var increment = 0;
        if (!transposed) {
          if (referenceCorner > 1)
            currentS += symbolWidth - 1;
          else
            increment = symbolWidth - 1;
        } else if ((referenceCorner & 1) == 0)
          currentS += symbolHeight - 1;
        else
          increment = symbolHeight - 1;

        var offsetT = t - ((referenceCorner & 1) != 0 ? 0 : symbolHeight - 1);
        var offsetS = currentS - ((referenceCorner & 2) != 0 ? symbolWidth - 1 : 0);
        if (transposed)
          _Place(bitmap, symbolBitmap, offsetT, offsetS, combinationOperator);
        else
          _Place(bitmap, symbolBitmap, offsetS, offsetT, combinationOperator);

        ++instance;
        var deltaS = huffman ? huffmanTables!.DeltaS.Decode(huffmanInput!) : _DecodeInteger(cache, "IADS", decoder);
        if (deltaS == null || instance >= numberOfSymbolInstances && huffman)
          break;

        currentS += increment + deltaS.Value + dsOffset;
      }
    }

    return bitmap;
  }

  /// <summary>Combines a symbol into a region bitmap with its top-left corner at (x, y).</summary>
  private static void _Place(byte[][] target, byte[][] symbol, int x, int y, int combinationOperator) {
    for (var row = 0; row < symbol.Length; ++row) {
      var ty = y + row;
      if ((uint)ty >= (uint)target.Length)
        continue;

      var targetRow = target[ty];
      var symbolRow = symbol[row];
      for (var column = 0; column < symbolRow.Length; ++column) {
        var tx = x + column;
        if ((uint)tx >= (uint)targetRow.Length)
          continue;

        targetRow[tx] = combinationOperator switch {
          0 => (byte)(targetRow[tx] | symbolRow[column]),
          1 => (byte)(targetRow[tx] & symbolRow[column]),
          2 => (byte)(targetRow[tx] ^ symbolRow[column]),
          3 => (byte)((targetRow[tx] ^ symbolRow[column]) ^ 1),
          _ => throw new NotSupportedException($"JBIG2 text region combination operator {combinationOperator} is not supported."),
        };
      }
    }
  }

  #endregion

  #region pattern dictionaries and halftone regions (T.88 6.6, 6.7, Annex C)

  private static byte[][][] _DecodePatternDictionary(bool mmr, int patternWidth, int patternHeight, int maxPatternIndex, int template, _DecodingContext context) {
    _Point[] at = [];
    if (!mmr)
      at = template == 0
        ? [new(-patternWidth, 0), new(-3, -1), new(2, -2), new(-2, -2)]
        : [new(-patternWidth, 0)];

    var collectiveWidth = (maxPatternIndex + 1) * patternWidth;
    var collective = _DecodeBitmap(mmr, collectiveWidth, patternHeight, template, false, null, at, context);
    var patterns = new byte[maxPatternIndex + 1][][];
    for (var i = 0; i <= maxPatternIndex; ++i) {
      var pattern = patterns[i] = new byte[patternHeight][];
      for (var y = 0; y < patternHeight; ++y)
        pattern[y] = collective[y].AsSpan(patternWidth * i, patternWidth).ToArray();
    }

    return patterns;
  }

  private static byte[][] _DecodeHalftoneRegion(
    bool mmr, byte[][][] patterns, int template, int regionWidth, int regionHeight, int defaultPixelValue,
    bool enableSkip, int combinationOperator, int gridWidth, int gridHeight, int gridOffsetX, int gridOffsetY,
    int gridVectorX, int gridVectorY, _DecodingContext context) {
    var region = new byte[regionHeight][];
    for (var i = 0; i < regionHeight; ++i) {
      region[i] = new byte[regionWidth];
      if (defaultPixelValue != 0)
        Array.Fill(region[i], (byte)1);
    }

    if (patterns.Length == 0)
      return region;

    var patternHeight = patterns[0].Length;
    var patternWidth = patternHeight > 0 ? patterns[0][0].Length : 0;
    var bitsPerValue = _Log2(patterns.Length);

    (int X, int Y) Cell(int mg, int ng) => (
      (int)((gridOffsetX + (long)mg * gridVectorY + (long)ng * gridVectorX) >> 8),
      (int)((gridOffsetY + (long)mg * gridVectorX - (long)ng * gridVectorY) >> 8));

    // 6.6.5.1: cells whose pattern would land wholly outside the region are not coded at all.
    byte[][]? skip = null;
    if (enableSkip) {
      skip = new byte[gridHeight][];
      for (var mg = 0; mg < gridHeight; ++mg) {
        skip[mg] = new byte[gridWidth];
        for (var ng = 0; ng < gridWidth; ++ng) {
          var (x, y) = Cell(mg, ng);
          if (x + patternWidth <= 0 || x >= regionWidth || y + patternHeight <= 0 || y >= regionHeight)
            skip[mg][ng] = 1;
        }
      }
    }

    _Point[] at = [];
    if (!mmr)
      at = template == 0
        ? [new(template <= 1 ? 3 : 2, -1), new(-3, -1), new(2, -2), new(-2, -2)]
        : [new(template <= 1 ? 3 : 2, -1)];

    // Annex C: the grey-scale image, one Gray-coded bit plane at a time, most significant first.
    var planes = new byte[bitsPerValue][][];
    var mmrPosition = context.Start;
    for (var i = bitsPerValue - 1; i >= 0; --i) {
      if (mmr) {
        planes[i] = PdfCcittFaxDecoder.DecodeMmrBitmap(context.Data, mmrPosition, context.End, gridWidth, gridHeight, true, out var next);
        mmrPosition = next;
      } else
        planes[i] = _DecodeBitmap(false, gridWidth, gridHeight, template, false, skip, at, context);
    }

    for (var mg = 0; mg < gridHeight; ++mg)
      for (var ng = 0; ng < gridWidth; ++ng) {
        var bit = 0;
        var patternIndex = 0;
        for (var j = bitsPerValue - 1; j >= 0; --j) {
          bit ^= planes[j][mg][ng];
          patternIndex |= bit << j;
        }

        var (x, y) = Cell(mg, ng);
        _PlacePattern(region, patterns[Math.Min(patternIndex, patterns.Length - 1)], x, y, combinationOperator);
      }

    return region;
  }

  private static void _PlacePattern(byte[][] target, byte[][] pattern, int x, int y, int combinationOperator) {
    for (var row = 0; row < pattern.Length; ++row) {
      var ty = y + row;
      if ((uint)ty >= (uint)target.Length)
        continue;

      var targetRow = target[ty];
      var patternRow = pattern[row];
      for (var column = 0; column < patternRow.Length; ++column) {
        var tx = x + column;
        if ((uint)tx >= (uint)targetRow.Length)
          continue;

        targetRow[tx] = combinationOperator switch {
          0 => (byte)(targetRow[tx] | patternRow[column]),
          1 => (byte)(targetRow[tx] & patternRow[column]),
          2 => (byte)(targetRow[tx] ^ patternRow[column]),
          3 => (byte)((targetRow[tx] ^ patternRow[column]) ^ 1),
          4 => patternRow[column],
          _ => throw new NotSupportedException($"JBIG2 halftone combination operator {combinationOperator} is not supported."),
        };
      }
    }
  }

  #endregion

  #region Huffman tables (T.88 Annex B)

  /// <summary>A bit reader over a segment's data, most significant bit first.</summary>
  private sealed class _BitReader(byte[] data, int start, int end) {
    private int _shift = -1;
    private int _currentByte;

    public byte[] Data { get; } = data;
    public int End { get; } = end;
    public int Position { get; set; } = start;

    public int ReadBit() {
      if (this._shift < 0) {
        if (this.Position >= this.End || this.Position >= this.Data.Length)
          throw new InvalidDataException("JBIG2 data ends while reading a bit.");

        this._currentByte = this.Data[this.Position++];
        this._shift = 7;
      }

      return (this._currentByte >> this._shift--) & 1;
    }

    public int ReadBits(int count) {
      var result = 0;
      for (var i = count - 1; i >= 0; --i)
        result |= this.ReadBit() << i;
      return result;
    }

    public void ByteAlign() => this._shift = -1;
  }

  /// <summary>One line of a code table: a value range, its prefix and how many bits follow it.</summary>
  private sealed class _HuffmanLine {
    public bool IsOob;
    public long RangeLow;
    public int PrefixLength;
    public int RangeLength;
    public int PrefixCode;
    public bool IsLowerRange;

    public static _HuffmanLine Oob(int prefixLength, int prefixCode) => new() { IsOob = true, PrefixLength = prefixLength, PrefixCode = prefixCode };

    public static _HuffmanLine Range(long rangeLow, int prefixLength, int rangeLength, int prefixCode, bool lower = false)
      => new() { RangeLow = rangeLow, PrefixLength = prefixLength, RangeLength = rangeLength, PrefixCode = prefixCode, IsLowerRange = lower };
  }

  private sealed class _HuffmanNode {
    public readonly _HuffmanNode?[] Children = new _HuffmanNode?[2];
    public _HuffmanLine? Leaf;
  }

  private sealed class _HuffmanTable {
    private readonly _HuffmanNode _root = new();

    public _HuffmanTable(List<_HuffmanLine> lines, bool prefixCodesDone) {
      if (!prefixCodesDone)
        _AssignPrefixCodes(lines);

      foreach (var line in lines) {
        if (line.PrefixLength <= 0)
          continue;

        var node = this._root;
        for (var shift = line.PrefixLength - 1; shift >= 0; --shift) {
          var bit = (line.PrefixCode >> shift) & 1;
          if (shift == 0)
            node.Children[bit] = new() { Leaf = line };
          else
            node = node.Children[bit] ??= new();
        }
      }
    }

    /// <summary>The next value, or null for the out-of-band line.</summary>
    public int? Decode(_BitReader reader) {
      var node = this._root;
      while (node.Leaf == null)
        node = node.Children[reader.ReadBit()] ?? throw new InvalidDataException("JBIG2 Huffman code matches no table line.");

      var line = node.Leaf;
      if (line.IsOob)
        return null;

      long offset = line.RangeLength >= 32 ? (uint)reader.ReadBits(32) : reader.ReadBits(line.RangeLength);
      var value = line.RangeLow + (line.IsLowerRange ? -offset : offset);
      return value is >= int.MinValue and <= int.MaxValue ? (int)value : throw new InvalidDataException("JBIG2 Huffman value overflows.");
    }

    /// <summary>B.3: canonical prefix codes from the prefix lengths.</summary>
    private static void _AssignPrefixCodes(List<_HuffmanLine> lines) {
      var maxLength = 0;
      foreach (var line in lines)
        maxLength = Math.Max(maxLength, line.PrefixLength);

      var histogram = new int[maxLength + 1];
      foreach (var line in lines)
        ++histogram[line.PrefixLength];

      histogram[0] = 0;
      var firstCode = 0;
      for (var length = 1; length <= maxLength; ++length) {
        firstCode = (firstCode + histogram[length - 1]) << 1;
        var code = firstCode;
        foreach (var line in lines)
          if (line.PrefixLength == length)
            line.PrefixCode = code++;
      }
    }
  }

  /// <summary>B.2: a code table carried in a tables segment.</summary>
  private static _HuffmanTable _DecodeTablesSegment(byte[] data, int start, int end) {
    var flags = data[start];
    long lowest = _ReadInt32(data, start + 1);
    long highest = _ReadInt32(data, start + 5);
    var reader = new _BitReader(data, start + 9, end);
    var prefixSizeBits = ((flags >> 1) & 7) + 1;
    var rangeSizeBits = ((flags >> 4) & 7) + 1;
    var lines = new List<_HuffmanLine>();

    var currentRangeLow = lowest;
    do {
      var prefixLength = reader.ReadBits(prefixSizeBits);
      var rangeLength = reader.ReadBits(rangeSizeBits);
      lines.Add(_HuffmanLine.Range(currentRangeLow, prefixLength, rangeLength, 0));
      currentRangeLow += 1L << rangeLength;
      if (lines.Count > 1 << 16)
        throw new InvalidDataException("JBIG2 code table never reaches its highest value.");
    } while (currentRangeLow < highest);

    lines.Add(_HuffmanLine.Range(lowest - 1, reader.ReadBits(prefixSizeBits), 32, 0, lower: true));
    lines.Add(_HuffmanLine.Range(highest, reader.ReadBits(prefixSizeBits), 32, 0));
    if ((flags & 1) != 0)
      lines.Add(_HuffmanLine.Oob(reader.ReadBits(prefixSizeBits), 0));

    return new(lines, false);
  }

  private static readonly Dictionary<int, _HuffmanTable> _StandardTables = [];

  /// <summary>B.5: the standard tables, as (low, prefix length, range length, prefix code) and (prefix length, code) for out-of-band.</summary>
  private static _HuffmanTable _StandardTable(int number) {
    lock (_StandardTables) {
      if (_StandardTables.TryGetValue(number, out var cached))
        return cached;

      _HuffmanLine R(long low, int prefixLength, int rangeLength, int code) => _HuffmanLine.Range(low, prefixLength, rangeLength, code);
      _HuffmanLine L(long low, int prefixLength, int rangeLength, int code) => _HuffmanLine.Range(low, prefixLength, rangeLength, code, lower: true);
      _HuffmanLine O(int prefixLength, int code) => _HuffmanLine.Oob(prefixLength, code);

      List<_HuffmanLine> lines = number switch {
        1 => [R(0, 1, 4, 0x0), R(16, 2, 8, 0x2), R(272, 3, 16, 0x6), R(65808, 3, 32, 0x7)],
        2 => [R(0, 1, 0, 0x0), R(1, 2, 0, 0x2), R(2, 3, 0, 0x6), R(3, 4, 3, 0xE), R(11, 5, 6, 0x1E), R(75, 6, 32, 0x3E), O(6, 0x3F)],
        3 => [R(-256, 8, 8, 0xFE), R(0, 1, 0, 0x0), R(1, 2, 0, 0x2), R(2, 3, 0, 0x6), R(3, 4, 3, 0xE), R(11, 5, 6, 0x1E), L(-257, 8, 32, 0xFF), R(75, 7, 32, 0x7E), O(6, 0x3E)],
        4 => [R(1, 1, 0, 0x0), R(2, 2, 0, 0x2), R(3, 3, 0, 0x6), R(4, 4, 3, 0xE), R(12, 5, 6, 0x1E), R(76, 5, 32, 0x1F)],
        5 => [R(-255, 7, 8, 0x7E), R(1, 1, 0, 0x0), R(2, 2, 0, 0x2), R(3, 3, 0, 0x6), R(4, 4, 3, 0xE), R(12, 5, 6, 0x1E), L(-256, 7, 32, 0x7F), R(76, 6, 32, 0x3E)],
        6 => [
          R(-2048, 5, 10, 0x1C), R(-1024, 4, 9, 0x8), R(-512, 4, 8, 0x9), R(-256, 4, 7, 0xA), R(-128, 5, 6, 0x1D), R(-64, 5, 5, 0x1E),
          R(-32, 4, 5, 0xB), R(0, 2, 7, 0x0), R(128, 3, 7, 0x2), R(256, 3, 8, 0x3), R(512, 4, 9, 0xC), R(1024, 4, 10, 0xD),
          L(-2049, 6, 32, 0x3E), R(2048, 6, 32, 0x3F),
        ],
        7 => [
          R(-1024, 4, 9, 0x8), R(-512, 3, 8, 0x0), R(-256, 4, 7, 0x9), R(-128, 5, 6, 0x1A), R(-64, 5, 5, 0x1B), R(-32, 4, 5, 0xA),
          R(0, 4, 5, 0xB), R(32, 5, 5, 0x1C), R(64, 5, 6, 0x1D), R(128, 4, 7, 0xC), R(256, 3, 8, 0x1), R(512, 3, 9, 0x2),
          R(1024, 3, 10, 0x3), L(-1025, 5, 32, 0x1E), R(2048, 5, 32, 0x1F),
        ],
        8 => [
          R(-15, 8, 3, 0xFC), R(-7, 9, 1, 0x1FC), R(-5, 8, 1, 0xFD), R(-3, 9, 0, 0x1FD), R(-2, 7, 0, 0x7C), R(-1, 4, 0, 0xA),
          R(0, 2, 1, 0x0), R(2, 5, 0, 0x1A), R(3, 6, 0, 0x3A), R(4, 3, 4, 0x4), R(20, 6, 1, 0x3B), R(22, 4, 4, 0xB),
          R(38, 4, 5, 0xC), R(70, 5, 6, 0x1B), R(134, 5, 7, 0x1C), R(262, 6, 7, 0x3C), R(390, 7, 8, 0x7D), R(646, 6, 10, 0x3D),
          L(-16, 9, 32, 0x1FE), R(1670, 9, 32, 0x1FF), O(2, 0x1),
        ],
        9 => [
          R(-31, 8, 4, 0xFC), R(-15, 9, 2, 0x1FC), R(-11, 8, 2, 0xFD), R(-7, 9, 1, 0x1FD), R(-5, 7, 1, 0x7C), R(-3, 4, 1, 0xA),
          R(-1, 3, 1, 0x2), R(1, 3, 1, 0x3), R(3, 5, 1, 0x1A), R(5, 6, 1, 0x3A), R(7, 3, 5, 0x4), R(39, 6, 2, 0x3B),
          R(43, 4, 5, 0xB), R(75, 4, 6, 0xC), R(139, 5, 7, 0x1B), R(267, 5, 8, 0x1C), R(523, 6, 8, 0x3C), R(779, 7, 9, 0x7D),
          R(1291, 6, 11, 0x3D), L(-32, 9, 32, 0x1FE), R(3339, 9, 32, 0x1FF), O(2, 0x0),
        ],
        10 => [
          R(-21, 7, 4, 0x7A), R(-5, 8, 0, 0xFC), R(-4, 7, 0, 0x7B), R(-3, 5, 0, 0x18), R(-2, 2, 2, 0x0), R(2, 5, 0, 0x19),
          R(3, 6, 0, 0x36), R(4, 7, 0, 0x7C), R(5, 8, 0, 0xFD), R(6, 2, 6, 0x1), R(70, 5, 5, 0x1A), R(102, 6, 5, 0x37),
          R(134, 6, 6, 0x38), R(198, 6, 7, 0x39), R(326, 6, 8, 0x3A), R(582, 6, 9, 0x3B), R(1094, 6, 10, 0x3C), R(2118, 7, 11, 0x7D),
          L(-22, 8, 32, 0xFE), R(4166, 8, 32, 0xFF), O(2, 0x2),
        ],
        11 => [
          R(1, 1, 0, 0x0), R(2, 2, 1, 0x2), R(4, 4, 0, 0xC), R(5, 4, 1, 0xD), R(7, 5, 1, 0x1C), R(9, 5, 2, 0x1D),
          R(13, 6, 2, 0x3C), R(17, 7, 2, 0x7A), R(21, 7, 3, 0x7B), R(29, 7, 4, 0x7C), R(45, 7, 5, 0x7D), R(77, 7, 6, 0x7E),
          R(141, 7, 32, 0x7F),
        ],
        12 => [
          R(1, 1, 0, 0x0), R(2, 2, 0, 0x2), R(3, 3, 1, 0x6), R(5, 5, 0, 0x1C), R(6, 5, 1, 0x1D), R(8, 6, 1, 0x3C),
          R(10, 7, 0, 0x7A), R(11, 7, 1, 0x7B), R(13, 7, 2, 0x7C), R(17, 7, 3, 0x7D), R(25, 7, 4, 0x7E), R(41, 8, 5, 0xFE),
          R(73, 8, 32, 0xFF),
        ],
        13 => [
          R(1, 1, 0, 0x0), R(2, 3, 0, 0x4), R(3, 4, 0, 0xC), R(4, 5, 0, 0x1C), R(5, 4, 1, 0xD), R(7, 3, 3, 0x5),
          R(15, 6, 1, 0x3A), R(17, 6, 2, 0x3B), R(21, 6, 3, 0x3C), R(29, 6, 4, 0x3D), R(45, 6, 5, 0x3E), R(77, 7, 6, 0x7E),
          R(141, 7, 32, 0x7F),
        ],
        14 => [R(-2, 3, 0, 0x4), R(-1, 3, 0, 0x5), R(0, 1, 0, 0x0), R(1, 3, 0, 0x6), R(2, 3, 0, 0x7)],
        15 => [
          R(-24, 7, 4, 0x7C), R(-8, 6, 2, 0x3C), R(-4, 5, 1, 0x1C), R(-2, 4, 0, 0xC), R(-1, 3, 0, 0x4), R(0, 1, 0, 0x0),
          R(1, 3, 0, 0x5), R(2, 4, 0, 0xD), R(3, 5, 1, 0x1D), R(5, 6, 2, 0x3D), R(9, 7, 4, 0x7D), L(-25, 7, 32, 0x7E),
          R(25, 7, 32, 0x7F),
        ],
        _ => throw new InvalidDataException($"JBIG2 standard table B.{number} does not exist."),
      };

      var table = new _HuffmanTable(lines, true);
      _StandardTables[number] = table;
      return table;
    }
  }

  /// <summary>A custom table: the index-th tables segment among those referred to (7.4.2.1.6, 7.4.3.1.6).</summary>
  private static _HuffmanTable _CustomTable(int index, int[] referredTo, Dictionary<int, _HuffmanTable> customTables) {
    var current = 0;
    foreach (var referred in referredTo)
      if (customTables.TryGetValue(referred, out var table)) {
        if (current == index)
          return table;
        ++current;
      }

    throw new InvalidDataException("JBIG2 segment selects a custom Huffman table it does not refer to.");
  }

  private sealed record _TextRegionTables(_HuffmanTable SymbolId, _HuffmanTable FirstS, _HuffmanTable DeltaS, _HuffmanTable DeltaT);

  private sealed record _SymbolDictionaryTables(_HuffmanTable DeltaHeight, _HuffmanTable DeltaWidth, _HuffmanTable BitmapSize, _HuffmanTable AggregateInstances);

  private static _TextRegionTables _TextRegionHuffmanTables(int fsSelector, int dsSelector, int dtSelector, int[] referredTo, Dictionary<int, _HuffmanTable> customTables, int numberOfSymbols, _BitReader reader) {
    // 7.4.3.1.7: the symbol ID table is itself coded, by run codes 0-34 with 4-bit lengths.
    var runCodes = new List<_HuffmanLine>();
    for (var i = 0; i <= 34; ++i)
      runCodes.Add(_HuffmanLine.Range(i, reader.ReadBits(4), 0, 0));
    var runCodesTable = new _HuffmanTable(runCodes, false);

    var codes = new List<_HuffmanLine>();
    for (var i = 0; i < numberOfSymbols;) {
      var codeLength = runCodesTable.Decode(reader) ?? throw new InvalidDataException("JBIG2 symbol ID table run code is out of band.");
      if (codeLength < 32) {
        codes.Add(_HuffmanLine.Range(i++, codeLength, 0, 0));
        continue;
      }

      var (repeats, repeated) = codeLength switch {
        32 when i == 0 => throw new InvalidDataException("JBIG2 symbol ID table repeats a length before giving one."),
        32 => (reader.ReadBits(2) + 3, codes[i - 1].PrefixLength),
        33 => (reader.ReadBits(3) + 3, 0),
        34 => (reader.ReadBits(7) + 11, 0),
        _ => throw new InvalidDataException("JBIG2 symbol ID table has an invalid run code."),
      };

      for (var j = 0; j < repeats; ++j)
        codes.Add(_HuffmanLine.Range(i++, repeated, 0, 0));
    }

    reader.ByteAlign();
    var symbolIdTable = new _HuffmanTable(codes, false);

    var customIndex = 0;
    _HuffmanTable Select(int selector, int firstStandard, int standardCount, string what) {
      if (selector < standardCount)
        return _StandardTable(firstStandard + selector);
      if (selector == 3)
        return _CustomTable(customIndex++, referredTo, customTables);
      throw new InvalidDataException($"JBIG2 text region has an invalid Huffman {what} selector.");
    }

    var firstS = Select(fsSelector, 6, 2, "FS");
    var deltaS = Select(dsSelector, 8, 3, "DS");
    var deltaT = Select(dtSelector, 11, 3, "DT");
    return new(symbolIdTable, firstS, deltaS, deltaT);
  }

  private static _SymbolDictionaryTables _SymbolDictionaryHuffmanTables(int deltaHeightSelector, int deltaWidthSelector, int bitmapSizeSelector, int aggregationInstancesSelector, int[] referredTo, Dictionary<int, _HuffmanTable> customTables) {
    var customIndex = 0;
    _HuffmanTable Select(int selector, int firstStandard, string what) => selector switch {
      0 or 1 => _StandardTable(firstStandard + selector),
      3 => _CustomTable(customIndex++, referredTo, customTables),
      _ => throw new InvalidDataException($"JBIG2 symbol dictionary has an invalid Huffman {what} selector."),
    };

    var deltaHeight = Select(deltaHeightSelector, 4, "DH");
    var deltaWidth = Select(deltaWidthSelector, 2, "DW");
    var bitmapSize = bitmapSizeSelector != 0 ? _CustomTable(customIndex++, referredTo, customTables) : _StandardTable(1);
    var aggregate = aggregationInstancesSelector != 0 ? _CustomTable(customIndex, referredTo, customTables) : _StandardTable(1);
    return new(deltaHeight, deltaWidth, bitmapSize, aggregate);
  }

  private static byte[][] _ReadUncompressedBitmap(_BitReader reader, int width, int height) {
    var bitmap = new byte[height][];
    for (var y = 0; y < height; ++y) {
      var row = bitmap[y] = new byte[width];
      for (var x = 0; x < width; ++x)
        row[x] = (byte)reader.ReadBit();
      reader.ByteAlign();
    }

    return bitmap;
  }

  #endregion

  /// <summary>The bits needed to number <paramref name="count"/> things: ⌈log₂ count⌉, and 0 for none or one.</summary>
  private static int _Log2(int count) {
    var bits = 0;
    while (count > 1L << bits)
      ++bits;
    return bits;
  }

  private static int _ReadInt32(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

  private static int _ReadUInt16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
}
