using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes HuffYUV and FFVHUFF: lossless Huffman coding of spatial prediction residuals.
/// </summary>
/// <remarks>
/// HuffYUV is intra only. Every packet is a complete independent picture and therefore a key frame;
/// the format has no P- or B-pictures and no forward or backward reference pictures.
/// <para/>
/// The implementation follows FFmpeg's LGPL-2.1-or-later HuffYUV encoder for the wire layout and
/// row scheduling. Eight-bit 4:2:0 and 4:2:2 interleaved HuffYUV, packed RGB(A), and FFVHUFF's
/// planar grey, YUV 4:2:0/4:2:2/4:4:0/4:4:4 and planar RGB(A) forms are written. Progressive and
/// interlaced prediction and both stream-global and per-frame Huffman tables are supported.
/// <para/>
/// Samples deeper than eight bits remain deliberately outside this class for now: FFVHUFF expands
/// the Huffman alphabet from 256 residuals to as many as 16384 and the 16-bit form splits low bits
/// out of each residual. Treating that as an eight-bit mode with wider samples would create a
/// plausible-looking but incompatible codec.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HuffYuvEncoder : IVideoCodecEncoder<HuffYuvEncoder> {

  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";
  private const int _SYMBOL_COUNT = HuffYuvHuffmanTable.SYMBOL_COUNT;
  private const byte _INTERLACED = 0x10;
  private const byte _PROGRESSIVE = 0x20;
  private const byte _TABLES_PER_FRAME = 0x40;
  private const byte _DECORRELATE = 0x40;
  private const byte _CHROMA = 0x01;
  private const byte _PLANAR_RGB = 0x02;
  private const byte _ALPHA = 0x04;
  private const int _B = 0;
  private const int _G = 1;
  private const int _R = 2;
  private const int _A = 3;

  private static readonly CodecTag _HFYU = CodecTag.FromCharacters("HFYU");
  private static readonly CodecTag _FFVH = CodecTag.FromCharacters("FFVH");

  private enum _Layout {
    Interleaved420,
    Interleaved422,
    PackedBgr,
    PackedBgra,
    PlanarGrey,
    PlanarYuv420,
    PlanarYuv422,
    PlanarYuv440,
    PlanarYuv444,
    PlanarRgb,
    PlanarRgba,
  }

  private readonly MediaStreamInfo _requested;
  private readonly CodecTag _tag;
  private readonly _Layout _layout;
  private readonly HuffYuvPredictionMethod _prediction;
  private readonly bool _interlaced;
  private readonly bool _tablesPerFrame;
  private readonly int _width;
  private readonly int _height;
  private HuffYuvHuffmanCodes[]? _tables;
  private MediaStreamInfo? _description;

  private HuffYuvEncoder(MediaStreamInfo requested, CodecTag tag, _Layout layout, HuffYuvPredictionMethod prediction, bool interlaced, bool tablesPerFrame) {
    this._requested = requested;
    this._tag = tag;
    this._layout = layout;
    this._prediction = prediction;
    this._interlaced = interlaced;
    this._tablesPerFrame = tablesPerFrame;
    this._width = requested.Width;
    this._height = requested.Height;
  }

  public static string CodecName => "HuffYUV / FFVHUFF";
  public static CodecTag Codec => _HFYU;

  // ============================================================================================
  // Setting up
  // ============================================================================================

  public static HuffYuvEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);

    var description = _DescriptionOf(stream);
    if (description.IsEmpty)
      return _Build(stream, _LayoutOfDepth(stream, planar: false), HuffYuvPredictionMethod.Left, interlaced: false, tablesPerFrame: false);

    if (description.Length < 4)
      throw new NotSupportedException($"Video stream {stream.Index} carries a {description.Length}-byte HuffYUV description, where the codec description begins with four bytes.");

    var format = HuffYuvFormat.Parse(description, stream.BitsPerPixel, stream.Index);
    return _Build(stream, _LayoutOf(format, stream.Index), (HuffYuvPredictionMethod)format.Predictor, format.Interlaced, format.TablesPerFrame);
  }

  public static HuffYuvEncoder Create(MediaStreamInfo stream, HuffYuvPredictionMethod prediction, bool planar = false, bool interlaced = false, bool tablesPerFrame = false) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);

    if (prediction is not (HuffYuvPredictionMethod.Left or HuffYuvPredictionMethod.Gradient or HuffYuvPredictionMethod.Median))
      throw new NotSupportedException($"{(int)prediction} is not one of the three prediction methods HuffYUV codes with: left, gradient and median.");

    return _Build(stream, _LayoutOfDepth(stream, planar), prediction, interlaced, tablesPerFrame);
  }

  private static void _RefuseUnusableGeometry(MediaStreamInfo stream) {
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"HuffYUV codes pictures; stream {stream.Index} is {stream.Kind}.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, and the size has to be known before the first frame.");
  }

  private static HuffYuvEncoder _Build(MediaStreamInfo stream, _Layout layout, HuffYuvPredictionMethod prediction, bool interlaced, bool tablesPerFrame) {
    var horizontalShift = layout is _Layout.Interleaved420 or _Layout.Interleaved422 or _Layout.PlanarYuv420 or _Layout.PlanarYuv422 ? 1 : 0;
    var verticalShift = layout is _Layout.Interleaved420 or _Layout.PlanarYuv420 or _Layout.PlanarYuv440 ? 1 : 0;

    if (horizontalShift != 0 && (stream.Width & 1) != 0)
      throw new NotSupportedException($"Video stream {stream.Index} is {stream.Width} pixels wide, but this HuffYUV layout subsamples chrominance horizontally and requires an even width.");
    if (verticalShift != 0 && (stream.Height & 1) != 0)
      throw new NotSupportedException($"Video stream {stream.Index} is {stream.Height} pixels high, but this HuffYUV layout subsamples chrominance vertically and requires an even height.");

    if ((layout is _Layout.PackedBgr or _Layout.PackedBgra) && prediction == HuffYuvPredictionMethod.Median)
      throw new NotSupportedException($"Video stream {stream.Index} asks for median prediction with colour coded a pixel at a time, which HuffYUV does not combine.");

    if (layout is _Layout.Interleaved420 or _Layout.Interleaved422 && prediction == HuffYuvPredictionMethod.Median) {
      var rowsBeforeMedian = interlaced ? 2 : 1;
      var chromaRows = layout == _Layout.Interleaved420 ? stream.Height / 2 : stream.Height;
      if (stream.Width < 4 || stream.Height <= rowsBeforeMedian || chromaRows <= rowsBeforeMedian)
        throw new NotSupportedException($"Video stream {stream.Index} is {stream.Width}x{stream.Height}, which cannot be coded in the interleaved HuffYUV layout with {(interlaced ? "interlaced " : string.Empty)}median prediction.");
    }

    var version = _VersionOf(layout);
    var tag = stream.Codec.EqualsIgnoringCase(_FFVH) ? _FFVH
      : stream.Codec.EqualsIgnoringCase(_HFYU) ? _HFYU
      : version >= 3 ? _FFVH : _HFYU;
    return new(stream, tag, layout, prediction, interlaced, tablesPerFrame);
  }

  private static _Layout _LayoutOfDepth(MediaStreamInfo stream, bool planar) => stream.BitsPerPixel switch {
    8 => _Layout.PlanarGrey,
    12 => planar ? _Layout.PlanarYuv420 : _Layout.Interleaved420,
    16 => planar ? _Layout.PlanarYuv422 : _Layout.Interleaved422,
    0 or 24 => planar ? _Layout.PlanarRgb : _Layout.PackedBgr,
    32 => planar ? _Layout.PlanarRgba : _Layout.PackedBgra,
    _ => throw new NotSupportedException($"Video stream {stream.Index} states {stream.BitsPerPixel} bits a pixel. This encoder writes eight-bit HuffYUV/FFVHUFF layouts at 8, 12, 16, 24 and 32 stored bits per pixel."),
  };

  private static _Layout _LayoutOf(HuffYuvFormat format, int streamIndex) => format.ColourSpace switch {
    HuffYuvColourSpace.Grey => _Layout.PlanarGrey,
    HuffYuvColourSpace.PlanarRgb => format.HasAlpha ? _Layout.PlanarRgba : _Layout.PlanarRgb,
    HuffYuvColourSpace.PackedBgr => format.BitstreamBitsPerPixel == 32 ? _Layout.PackedBgra : _Layout.PackedBgr,
    HuffYuvColourSpace.Yuv when format.Version < 3 && format.BitstreamBitsPerPixel == 12 => _Layout.Interleaved420,
    HuffYuvColourSpace.Yuv when format.Version < 3 && format.BitstreamBitsPerPixel == 16 => _Layout.Interleaved422,
    HuffYuvColourSpace.Yuv when format.HasAlpha => throw new NotSupportedException($"Video stream {streamIndex} asks for planar YUV with alpha, for which the raw-image model has no planar YUVA pixel format."),
    HuffYuvColourSpace.Yuv when (format.ChromaHorizontalShift, format.ChromaVerticalShift) == (1, 1) => _Layout.PlanarYuv420,
    HuffYuvColourSpace.Yuv when (format.ChromaHorizontalShift, format.ChromaVerticalShift) == (1, 0) => _Layout.PlanarYuv422,
    HuffYuvColourSpace.Yuv when (format.ChromaHorizontalShift, format.ChromaVerticalShift) == (0, 1) => _Layout.PlanarYuv440,
    HuffYuvColourSpace.Yuv when (format.ChromaHorizontalShift, format.ChromaVerticalShift) == (0, 0) => _Layout.PlanarYuv444,
    HuffYuvColourSpace.Yuv => throw new NotSupportedException($"Video stream {streamIndex} asks for chroma shifts {format.ChromaHorizontalShift}:{format.ChromaVerticalShift}; the raw-image model represents 4:2:0, 4:2:2, 4:4:0 and 4:4:4, but not 4:1:x."),
    _ => throw new NotSupportedException($"Video stream {streamIndex} describes an unsupported HuffYUV layout."),
  };

  private static int _VersionOf(_Layout layout) => layout is _Layout.PlanarGrey or _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444 or _Layout.PlanarRgb or _Layout.PlanarRgba ? 3 : 2;

  private static ReadOnlySpan<byte> _DescriptionOf(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    if (data.IsEmpty)
      return data;
    if (data.Length >= BitmapInfoHeader.StructSize + 4) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data);
      var code = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
      if (size >= BitmapInfoHeader.StructSize && size <= (uint)data.Length && (code.EqualsIgnoringCase(_HFYU) || code.EqualsIgnoringCase(_FFVH)))
        return data[BitmapInfoHeader.StructSize..];
    }
    return data;
  }

  // ============================================================================================
  // Stream description and tables
  // ============================================================================================

  public MediaStreamInfo DescribeStream() {
    if (this._description != null)
      return this._description;
    var tables = this._tables;
    if (tables == null) {
      tables = _TablesFrom(_AssumedStatistics(this._TableCount));
      if (!this._tablesPerFrame)
        this._tables = tables;
    }
    return this._description = this._BuildDescription(tables);
  }

  private int _Version => _VersionOf(this._layout);
  private int _TableCount => this._layout switch { _Layout.PlanarGrey => 1, _Layout.PlanarRgba => 4, _ => 3 };
  private int _BitsPerPixel => this._layout switch {
    _Layout.PlanarGrey => 8,
    _Layout.Interleaved420 or _Layout.PlanarYuv420 => 12,
    _Layout.Interleaved422 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 => 16,
    _Layout.PackedBgra or _Layout.PlanarRgba => 32,
    _ => 24,
  };
  private PixelFormat _WorkingFormat => this._layout switch {
    _Layout.Interleaved420 or _Layout.PlanarYuv420 => PixelFormat.Yuv420P8,
    _Layout.Interleaved422 or _Layout.PlanarYuv422 => PixelFormat.Yuv422P8,
    _Layout.PlanarYuv440 => PixelFormat.Yuv440P8,
    _Layout.PlanarYuv444 => PixelFormat.Yuv444P8,
    _Layout.PackedBgr or _Layout.PackedBgra => PixelFormat.Bgra32,
    _Layout.PlanarGrey => PixelFormat.Gray8,
    _Layout.PlanarRgb => PixelFormat.Rgb24,
    _ => PixelFormat.Rgba32,
  };
  private (int Horizontal, int Vertical) _ChromaShift => this._layout switch {
    _Layout.PlanarYuv420 => (1, 1), _Layout.PlanarYuv422 => (1, 0), _Layout.PlanarYuv440 => (0, 1), _ => (0, 0),
  };

  private static ulong[][] _AssumedStatistics(int tables) {
    var statistics = new ulong[tables][];
    for (var table = 0; table < tables; ++table) {
      statistics[table] = new ulong[_SYMBOL_COUNT];
      for (var symbol = 0; symbol < _SYMBOL_COUNT; ++symbol) {
        var distance = Math.Min(symbol, _SYMBOL_COUNT - symbol);
        statistics[table][symbol] = 100000000UL / (ulong)(distance * distance + 1);
      }
    }
    return statistics;
  }

  private static HuffYuvHuffmanCodes[] _TablesFrom(ulong[][] statistics) {
    var tables = new HuffYuvHuffmanCodes[statistics.Length];
    for (var i = 0; i < tables.Length; ++i)
      tables[i] = HuffYuvHuffmanCodes.FromStatistics(statistics[i]);
    return tables;
  }

  private MediaStreamInfo _BuildDescription(HuffYuvHuffmanCodes[] tables) {
    var extra = new List<byte>();
    if (this._Version == 2) {
      var decorrelate = this._layout is _Layout.PackedBgr or _Layout.PackedBgra;
      extra.Add((byte)((int)this._prediction | (decorrelate ? _DECORRELATE : 0)));
      extra.Add((byte)this._BitsPerPixel);
      extra.Add((byte)((this._interlaced ? _INTERLACED : _PROGRESSIVE) | (this._tablesPerFrame ? _TABLES_PER_FRAME : 0)));
      extra.Add(0);
    } else {
      var flags = (byte)(this._interlaced ? _INTERLACED : _PROGRESSIVE);
      if (this._tablesPerFrame)
        flags |= _TABLES_PER_FRAME;
      var depthAndSubsampling = 0x70;
      if (this._layout is _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444) {
        flags |= _CHROMA;
        var (horizontal, vertical) = this._ChromaShift;
        depthAndSubsampling |= horizontal | (vertical << 2);
      } else if (this._layout is _Layout.PlanarRgb or _Layout.PlanarRgba) {
        flags |= _PLANAR_RGB;
        if (this._layout == _Layout.PlanarRgba)
          flags |= _ALPHA;
      }
      extra.Add((byte)this._prediction);
      extra.Add((byte)depthAndSubsampling);
      extra.Add(flags);
      extra.Add(1);
    }
    foreach (var table in tables)
      table.Store(extra);

    var format = new byte[BitmapInfoHeader.StructSize + extra.Count];
    var header = format.AsSpan();
    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)format.Length);
    BinaryPrimitives.WriteInt32LittleEndian(header[4..], this._width);
    BinaryPrimitives.WriteInt32LittleEndian(header[8..], this._height);
    BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)this._BitsPerPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(header[16..], this._tag.Value);
    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], checked((uint)((long)this._width * this._height * this._BitsPerPixel / 8)));
    extra.CopyTo(format, BitmapInfoHeader.StructSize);

    return new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = this._tag, Handler = this._tag,
      CodecId = _VFW_CODEC_ID, TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount, Width = this._width, Height = this._height,
      BitsPerPixel = this._BitsPerPixel, CodecPrivateData = format, Language = this._requested.Language, Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // A frame
  // ============================================================================================

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"The encoder was created for {this._width}x{this._height} pictures but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException($"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var working = this._WorkingFormat;
    var picture = frame.Format == working ? frame : FastRawImageConverter.Convert(frame, working);
    var symbols = this._Residuals(picture);
    var statistics = symbols.Statistics(this._TableCount);
    var tables = this._tablesPerFrame ? _TablesFrom(statistics) : this._tables ??= _TablesFrom(statistics);
    this._description ??= this._BuildDescription(tables);

    packet = new(this._requested.Index, this._Write(symbols, tables), presentationTimestamp, presentationTimestamp, IsKeyFrame: true);
    return true;
  }

  private byte[] _Write(_Symbols symbols, HuffYuvHuffmanCodes[] tables) {
    var tableBytes = new List<byte>();
    if (this._tablesPerFrame)
      foreach (var table in tables)
        table.Store(tableBytes);
    var bits = new HuffYuvBitWriter(symbols.Count + tableBytes.Count + 8);
    foreach (var value in tableBytes)
      bits.Write(value, 8);
    foreach (var raw in symbols.Raw)
      bits.Write(raw, 8);
    for (var i = 0; i < symbols.Count; ++i)
      tables[symbols.TableOf[i]].Write(bits, symbols.Values[i]);
    return bits.End();
  }

  private sealed class _Symbols {
    internal _Symbols(int capacity) {
      this.TableOf = new byte[Math.Max(1, capacity)];
      this.Values = new byte[Math.Max(1, capacity)];
    }
    internal byte[] Raw { get; set; } = [];
    internal byte[] TableOf { get; private set; }
    internal byte[] Values { get; private set; }
    internal int Count { get; private set; }
    internal void Add(int table, byte value) {
      if (this.Count == this.Values.Length) {
        Array.Resize(ref this.Values, this.Values.Length * 2);
        Array.Resize(ref this.TableOf, this.TableOf.Length * 2);
      }
      this.TableOf[this.Count] = (byte)table;
      this.Values[this.Count++] = value;
    }
    internal void AddRow(int table, ReadOnlySpan<byte> values) { foreach (var value in values) this.Add(table, value); }
    internal ulong[][] Statistics(int tables) {
      var statistics = new ulong[tables][];
      for (var i = 0; i < tables; ++i) statistics[i] = new ulong[_SYMBOL_COUNT];
      for (var i = 0; i < this.Count; ++i) ++statistics[this.TableOf[i]][this.Values[i]];
      return statistics;
    }
  }

  private _Symbols _Residuals(RawImage picture) => this._layout switch {
    _Layout.Interleaved420 => this._InterleavedYuv(picture, halfHeight: true),
    _Layout.Interleaved422 => this._InterleavedYuv(picture, halfHeight: false),
    _Layout.PackedBgr or _Layout.PackedBgra => this._Packed(picture),
    _ => this._Planes(picture),
  };

  // ============================================================================================
  // Interleaved YUV
  // ============================================================================================

  private _Symbols _InterleavedYuv(RawImage picture, bool halfHeight) {
    var width = this._width;
    var height = this._height;
    var chromaWidth = width / 2;
    var chromaHeight = halfHeight ? height / 2 : height;
    var luma = picture.GetPlaneData(0);
    var cb = picture.GetPlaneData(1);
    var cr = picture.GetPlaneData(2);
    var symbols = new _Symbols(width * height * 2);
    var dY = new byte[width]; var dU = new byte[chromaWidth]; var dV = new byte[chromaWidth];
    var tY = new byte[width]; var tU = new byte[chromaWidth]; var tV = new byte[chromaWidth];

    symbols.Raw = [cr[0], luma[1], cb[0], luma[0]];
    var leftY = _SubtractLeft(luma[..width], dY, width, 0);
    var leftU = _SubtractLeft(cb[..chromaWidth], dU, chromaWidth, 0);
    var leftV = _SubtractLeft(cr[..chromaWidth], dV, chromaWidth, 0);
    _AddGroups(symbols, dY, dU, dV, 2, width);

    var above = this._interlaced ? 2 : 1;
    var y = 1;
    var cy = 1;

    if (this._prediction == HuffYuvPredictionMethod.Median) {
      if (this._interlaced) {
        leftY = _SubtractLeft(luma.Slice(width, width), dY, width, leftY);
        leftU = _SubtractLeft(cb.Slice(chromaWidth, chromaWidth), dU, chromaWidth, leftU);
        leftV = _SubtractLeft(cr.Slice(chromaWidth, chromaWidth), dV, chromaWidth, leftV);
        _AddGroups(symbols, dY, dU, dV, 0, width);
        ++y; ++cy;
      }

      const int _LUMA_LEFT = 4;
      const int _CHROMA_LEFT = 2;
      var rowY = luma.Slice(y * width, width);
      var rowU = cb.Slice(cy * chromaWidth, chromaWidth);
      var rowV = cr.Slice(cy * chromaWidth, chromaWidth);
      var aboveY = luma.Slice((y - above) * width, width);
      var aboveU = cb.Slice((cy - above) * chromaWidth, chromaWidth);
      var aboveV = cr.Slice((cy - above) * chromaWidth, chromaWidth);
      leftY = _SubtractLeft(rowY, dY, _LUMA_LEFT, leftY);
      leftU = _SubtractLeft(rowU, dU, _CHROMA_LEFT, leftU);
      leftV = _SubtractLeft(rowV, dV, _CHROMA_LEFT, leftV);
      var leftAboveY = aboveY[_LUMA_LEFT - 1];
      var leftAboveU = aboveU[_CHROMA_LEFT - 1];
      var leftAboveV = aboveV[_CHROMA_LEFT - 1];
      _SubtractMedian(aboveY[_LUMA_LEFT..], rowY[_LUMA_LEFT..], dY.AsSpan(_LUMA_LEFT), width - _LUMA_LEFT, ref leftY, ref leftAboveY);
      _SubtractMedian(aboveU[_CHROMA_LEFT..], rowU[_CHROMA_LEFT..], dU.AsSpan(_CHROMA_LEFT), chromaWidth - _CHROMA_LEFT, ref leftU, ref leftAboveU);
      _SubtractMedian(aboveV[_CHROMA_LEFT..], rowV[_CHROMA_LEFT..], dV.AsSpan(_CHROMA_LEFT), chromaWidth - _CHROMA_LEFT, ref leftV, ref leftAboveV);
      _AddGroups(symbols, dY, dU, dV, 0, width);
      ++y; ++cy;

      while (y < height) {
        if (halfHeight)
          while (2 * cy > y && y < height) {
            _SubtractMedian(luma.Slice((y - above) * width, width), luma.Slice(y * width, width), dY, width, ref leftY, ref leftAboveY);
            symbols.AddRow(0, dY);
            ++y;
          }
        if (y >= height) break;
        if (cy >= chromaHeight) throw new InvalidDataException("The interleaved 4:2:0 row schedule needs a chrominance row beyond the picture.");
        _SubtractMedian(luma.Slice((y - above) * width, width), luma.Slice(y * width, width), dY, width, ref leftY, ref leftAboveY);
        _SubtractMedian(cb.Slice((cy - above) * chromaWidth, chromaWidth), cb.Slice(cy * chromaWidth, chromaWidth), dU, chromaWidth, ref leftU, ref leftAboveU);
        _SubtractMedian(cr.Slice((cy - above) * chromaWidth, chromaWidth), cr.Slice(cy * chromaWidth, chromaWidth), dV, chromaWidth, ref leftV, ref leftAboveV);
        _AddGroups(symbols, dY, dU, dV, 0, width);
        ++y; ++cy;
      }
      return symbols;
    }

    while (y < height) {
      if (halfHeight) {
        leftY = this._SubtractPredictedRow(luma, y, width, dY, tY, leftY, above);
        symbols.AddRow(0, dY);
        if (++y >= height) break;
      }
      if (cy >= chromaHeight) throw new InvalidDataException("The interleaved YUV row schedule needs a chrominance row beyond the picture.");
      leftY = this._SubtractPredictedRow(luma, y, width, dY, tY, leftY, above);
      leftU = this._SubtractPredictedRow(cb, cy, chromaWidth, dU, tU, leftU, above);
      leftV = this._SubtractPredictedRow(cr, cy, chromaWidth, dV, tV, leftV, above);
      _AddGroups(symbols, dY, dU, dV, 0, width);
      ++y; ++cy;
    }
    return symbols;
  }

  private byte _SubtractPredictedRow(ReadOnlySpan<byte> plane, int row, int width, Span<byte> differences, Span<byte> temp, byte left, int above) {
    var current = plane.Slice(row * width, width);
    if (this._prediction == HuffYuvPredictionMethod.Gradient && row >= above) {
      _SubtractAbove(current, plane.Slice((row - above) * width, width), temp, width);
      return _SubtractLeft(temp, differences, width, left);
    }
    return _SubtractLeft(current, differences, width, left);
  }

  private static void _AddGroups(_Symbols symbols, ReadOnlySpan<byte> dY, ReadOnlySpan<byte> dU, ReadOnlySpan<byte> dV, int from, int width) {
    for (var x = from; x < width; x += 2) {
      symbols.Add(0, dY[x]); symbols.Add(1, dU[x / 2]); symbols.Add(0, dY[x + 1]); symbols.Add(2, dV[x / 2]);
    }
  }

  // ============================================================================================
  // Packed RGB(A)
  // ============================================================================================

  private _Symbols _Packed(RawImage picture) {
    var width = this._width; var height = this._height; var stride = width * 4;
    var hasAlpha = this._layout == _Layout.PackedBgra;
    var pixels = picture.PixelData.AsSpan(0, stride * height);
    var symbols = new _Symbols(width * height * 4);
    var differences = new byte[stride]; var against = new byte[stride]; var left = new byte[4];
    var above = this._interlaced ? 2 : 1;
    var bottom = pixels.Slice((height - 1) * stride, stride);
    symbols.Raw = hasAlpha ? [bottom[_A], bottom[_R], bottom[_G], bottom[_B]] : [bottom[_R], bottom[_G], bottom[_B], 0];
    bottom[..4].CopyTo(left);
    _SubtractLeftPixels(bottom[4..], differences, width - 1, left);
    _AddPixels(symbols, differences, width - 1, hasAlpha);
    for (var encodedRow = 1; encodedRow < height; ++encodedRow) {
      var physicalRow = height - 1 - encodedRow;
      var row = pixels.Slice(physicalRow * stride, stride);
      if (this._prediction == HuffYuvPredictionMethod.Gradient && encodedRow >= above) {
        _SubtractAbove(row, pixels.Slice((physicalRow + above) * stride, stride), against, stride);
        _SubtractLeftPixels(against, differences, width, left);
      } else
        _SubtractLeftPixels(row, differences, width, left);
      _AddPixels(symbols, differences, width, hasAlpha);
    }
    return symbols;
  }

  private static void _SubtractLeftPixels(ReadOnlySpan<byte> row, Span<byte> into, int count, byte[] left) {
    for (var i = 0; i < count * 4; i += 4)
      for (var channel = 0; channel < 4; ++channel) {
        var value = row[i + channel]; into[i + channel] = (byte)(value - left[channel]); left[channel] = value;
      }
  }

  private static void _AddPixels(_Symbols symbols, ReadOnlySpan<byte> differences, int count, bool hasAlpha) {
    for (var i = 0; i < count * 4; i += 4) {
      var green = differences[i + _G];
      symbols.Add(1, green); symbols.Add(0, (byte)(differences[i + _B] - green)); symbols.Add(2, (byte)(differences[i + _R] - green));
      if (hasAlpha) symbols.Add(2, differences[i + _A]);
    }
  }

  // ============================================================================================
  // Planar FFVHUFF
  // ============================================================================================

  private _Symbols _Planes(RawImage picture) {
    var symbols = new _Symbols(checked(this._width * this._height * this._TableCount));
    if (this._layout == _Layout.PlanarGrey) {
      this._CodePlane(symbols, picture.PixelData, this._width, this._height, 0);
      return symbols;
    }
    if (this._layout is _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444) {
      for (var plane = 0; plane < 3; ++plane) {
        var (width, height) = picture.GetPlaneDimensions(plane);
        this._CodePlane(symbols, picture.GetPlaneData(plane), width, height, plane);
      }
      return symbols;
    }
    var count = this._width * this._height;
    var extracted = new byte[count];
    var pixels = picture.PixelData.AsSpan();
    if (this._layout == _Layout.PlanarRgb) {
      this._CodeChannel(symbols, pixels, 3, 1, extracted, 0); this._CodeChannel(symbols, pixels, 3, 2, extracted, 1); this._CodeChannel(symbols, pixels, 3, 0, extracted, 2);
    } else {
      this._CodeChannel(symbols, pixels, 4, 1, extracted, 0); this._CodeChannel(symbols, pixels, 4, 2, extracted, 1); this._CodeChannel(symbols, pixels, 4, 0, extracted, 2); this._CodeChannel(symbols, pixels, 4, 3, extracted, 3);
    }
    return symbols;
  }

  private void _CodeChannel(_Symbols symbols, ReadOnlySpan<byte> pixels, int channels, int channel, byte[] plane, int table) {
    for (int i = 0, at = channel; i < plane.Length; ++i, at += channels) plane[i] = pixels[at];
    this._CodePlane(symbols, plane, this._width, this._height, table);
  }

  private void _CodePlane(_Symbols symbols, ReadOnlySpan<byte> plane, int width, int height, int table) {
    var differences = new byte[width]; var against = new byte[width]; var above = this._interlaced ? 2 : 1;
    byte left = 0; byte leftAbove = 0;
    for (var y = 0; y < height; ++y) {
      var row = plane.Slice(y * width, width);
      if (y < above) {
        left = _SubtractLeft(row, differences, width, left); symbols.AddRow(table, differences);
        if (y == above - 1) leftAbove = plane[0];
        continue;
      }
      switch (this._prediction) {
        case HuffYuvPredictionMethod.Median:
          _SubtractMedian(plane.Slice((y - above) * width, width), row, differences, width, ref left, ref leftAbove);
          break;
        case HuffYuvPredictionMethod.Gradient:
          _SubtractAbove(row, plane.Slice((y - above) * width, width), against, width); left = _SubtractLeft(against, differences, width, left);
          break;
        default:
          left = _SubtractLeft(row, differences, width, left);
          break;
      }
      symbols.AddRow(table, differences);
    }
  }

  // ============================================================================================
  // Prediction residuals
  // ============================================================================================

  private static byte _SubtractLeft(ReadOnlySpan<byte> row, Span<byte> into, int count, byte left) {
    for (var i = 0; i < count; ++i) { var value = row[i]; into[i] = (byte)(value - left); left = value; }
    return left;
  }
  private static void _SubtractAbove(ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> into, int count) {
    for (var i = 0; i < count; ++i) into[i] = (byte)(row[i] - above[i]);
  }
  private static void _SubtractMedian(ReadOnlySpan<byte> above, ReadOnlySpan<byte> row, Span<byte> into, int count, ref byte left, ref byte leftAbove) {
    var l = left; var lt = leftAbove;
    for (var i = 0; i < count; ++i) {
      var top = above[i]; var predicted = _Median(l, top, (byte)(l + top - lt)); lt = top; l = row[i]; into[i] = (byte)(l - predicted);
    }
    left = l; leftAbove = lt;
  }
  private static byte _Median(byte a, byte b, byte c) {
    if (a > b) (a, b) = (b, a);
    return c < a ? a : c > b ? b : c;
  }
}
