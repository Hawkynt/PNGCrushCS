using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes classic HuffYUV and FFVHUFF, including high-depth planar version 3.</summary>
/// <remarks>
/// Every frame is intra and independently decodable. Classic/headerless streams use the fixed
/// historic code books. Version 2/3 streams use generated Huffman tables, either once in the codec
/// description or in every frame. High-depth v3 residuals use an alphabet of <c>min(2^bps,16384)</c>;
/// sixteen-bit residuals Huffman-code their upper fourteen bits and carry two literal low bits.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HuffYuvEncoder : IVideoCodecEncoder<HuffYuvEncoder> {

  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";
  private const byte _INTERLACED = 0x10, _PROGRESSIVE = 0x20, _TABLES_PER_FRAME = 0x40;
  private const byte _DECORRELATE = 0x40, _CHROMA = 0x01, _PLANAR_RGB = 0x02, _ALPHA = 0x04;
  private const int _B = 0, _G = 1, _R = 2, _A = 3;

  private static readonly CodecTag _HFYU = CodecTag.FromCharacters("HFYU");
  private static readonly CodecTag _FFVH = CodecTag.FromCharacters("FFVH");

  private enum _Layout {
    Interleaved420, Interleaved422, PackedBgr, PackedBgra,
    PlanarGrey, PlanarYuv420, PlanarYuv422, PlanarYuv440, PlanarYuv444, PlanarRgb, PlanarRgba,
  }

  private readonly MediaStreamInfo _requested;
  private readonly CodecTag _tag;
  private readonly _Layout _layout;
  private readonly HuffYuvPredictionMethod _prediction;
  private readonly bool _interlaced;
  private readonly bool _tablesPerFrame;
  private readonly bool _legacy;
  private readonly bool _decorrelate;
  private readonly int _version;
  private readonly int _bitsPerSample;
  private readonly int _width;
  private readonly int _height;
  private HuffYuvHuffmanCodes[]? _tables;
  private MediaStreamInfo? _description;

  private HuffYuvEncoder(
    MediaStreamInfo requested, CodecTag tag, _Layout layout, HuffYuvPredictionMethod prediction,
    bool interlaced, bool tablesPerFrame, bool legacy, bool decorrelate, int version, int bitsPerSample) {
    this._requested = requested;
    this._tag = tag;
    this._layout = layout;
    this._prediction = prediction;
    this._interlaced = interlaced;
    this._tablesPerFrame = tablesPerFrame;
    this._legacy = legacy;
    this._decorrelate = decorrelate;
    this._version = version;
    this._bitsPerSample = bitsPerSample;
    this._width = requested.Width;
    this._height = requested.Height;
  }

  public static string CodecName => "HuffYUV / FFVHUFF";
  public static CodecTag Codec => _HFYU;

  public static HuffYuvEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);

    var headerOnly = _IsHeaderOnly(stream);
    var extra = _DescriptionOf(stream);
    if (headerOnly) {
      var format = HuffYuvFormat.Parse(default, stream.BitsPerPixel, stream.Index, stream.Height);
      return _BuildFromFormat(stream, format, legacy: true);
    }
    if (extra.IsEmpty)
      return _Build(stream, _LayoutOfDepth(stream, planar: false), HuffYuvPredictionMethod.Left, false, false, false, false, _VersionOf(_LayoutOfDepth(stream, false)), 8);
    if (extra.Length < 4)
      throw new NotSupportedException($"Video stream {stream.Index} carries only {extra.Length} HuffYUV description byte(s).");

    return _BuildFromFormat(stream, HuffYuvFormat.Parse(extra, stream.BitsPerPixel, stream.Index, stream.Height), legacy: false);
  }

  public static HuffYuvEncoder Create(MediaStreamInfo stream, HuffYuvPredictionMethod prediction, bool planar = false, bool interlaced = false, bool tablesPerFrame = false) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);
    if (prediction is not (HuffYuvPredictionMethod.Left or HuffYuvPredictionMethod.Gradient or HuffYuvPredictionMethod.Median))
      throw new NotSupportedException($"{(int)prediction} is not a HuffYUV prediction method.");
    var layout = _LayoutOfDepth(stream, planar);
    return _Build(stream, layout, prediction, interlaced, tablesPerFrame, false, layout is _Layout.PackedBgr or _Layout.PackedBgra, _VersionOf(layout), 8);
  }

  private static HuffYuvEncoder _BuildFromFormat(MediaStreamInfo stream, HuffYuvFormat format, bool legacy) {
    var layout = _LayoutOf(format, stream.Index);
    return _Build(
      stream, layout, (HuffYuvPredictionMethod)format.Predictor, format.Interlaced, format.TablesPerFrame,
      legacy, format.Decorrelate, format.Version, format.BitsPerSample);
  }

  private static HuffYuvEncoder _Build(
    MediaStreamInfo stream, _Layout layout, HuffYuvPredictionMethod prediction, bool interlaced,
    bool tablesPerFrame, bool legacy, bool decorrelate, int version, int bitsPerSample) {
    var horizontalShift = layout is _Layout.Interleaved420 or _Layout.Interleaved422 or _Layout.PlanarYuv420 or _Layout.PlanarYuv422 ? 1 : 0;
    var verticalShift = layout is _Layout.Interleaved420 or _Layout.PlanarYuv420 or _Layout.PlanarYuv440 ? 1 : 0;
    if (horizontalShift != 0 && (stream.Width & 1) != 0)
      throw new NotSupportedException("This HuffYUV chroma layout requires an even width.");
    if (verticalShift != 0 && (stream.Height & 1) != 0)
      throw new NotSupportedException("This HuffYUV chroma layout requires an even height.");
    if ((layout is _Layout.PackedBgr or _Layout.PackedBgra) && prediction == HuffYuvPredictionMethod.Median)
      throw new NotSupportedException("Packed RGB HuffYUV does not support median prediction.");
    if (version < 3 && bitsPerSample != 8)
      throw new NotSupportedException("Only FFVHUFF version 3 carries samples deeper than eight bits.");
    _RefuseUnrepresentableHighDepth(stream.Index, layout, bitsPerSample);

    if (layout is _Layout.Interleaved420 or _Layout.Interleaved422 && prediction == HuffYuvPredictionMethod.Median) {
      var rowsBeforeMedian = interlaced ? 2 : 1;
      var chromaRows = layout == _Layout.Interleaved420 ? stream.Height / 2 : stream.Height;
      if (stream.Width < 4 || stream.Height <= rowsBeforeMedian || chromaRows <= rowsBeforeMedian)
        throw new NotSupportedException("The picture is too small for interleaved median prediction.");
    }

    var tag = legacy ? _HFYU : stream.Codec.EqualsIgnoringCase(_HFYU) ? _HFYU : stream.Codec.EqualsIgnoringCase(_FFVH) ? _FFVH : version >= 3 ? _FFVH : _HFYU;
    return new(stream, tag, layout, prediction, interlaced, tablesPerFrame, legacy, decorrelate, version, bitsPerSample);
  }

  private static void _RefuseUnrepresentableHighDepth(int streamIndex, _Layout layout, int bits) {
    if (bits <= 8)
      return;
    var represented = layout switch {
      _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444 => bits is 10 or 12 or 16,
      _Layout.PlanarGrey => bits is 10 or 16,
      _Layout.PlanarRgb => bits is 10 or 16,
      _Layout.PlanarRgba => bits == 16,
      _ => false,
    };
    if (!represented)
      throw new NotSupportedException($"Video stream {streamIndex} uses {bits}-bit FFVHUFF in a layout RawImage cannot represent exactly.");
  }

  private static void _RefuseUnusableGeometry(MediaStreamInfo stream) {
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("HuffYUV codes video pictures only.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException("HuffYUV needs a positive picture size before encoding starts.");
  }

  private static _Layout _LayoutOfDepth(MediaStreamInfo stream, bool planar) => stream.BitsPerPixel switch {
    8 => _Layout.PlanarGrey,
    12 => planar ? _Layout.PlanarYuv420 : _Layout.Interleaved420,
    16 => planar ? _Layout.PlanarYuv422 : _Layout.Interleaved422,
    0 or 24 => planar ? _Layout.PlanarRgb : _Layout.PackedBgr,
    32 => planar ? _Layout.PlanarRgba : _Layout.PackedBgra,
    _ => throw new NotSupportedException($"{stream.BitsPerPixel} stored bits per pixel do not identify an eight-bit HuffYUV layout."),
  };

  private static _Layout _LayoutOf(HuffYuvFormat f, int streamIndex) => f.ColourSpace switch {
    HuffYuvColourSpace.Grey => _Layout.PlanarGrey,
    HuffYuvColourSpace.PlanarRgb => f.HasAlpha ? _Layout.PlanarRgba : _Layout.PlanarRgb,
    HuffYuvColourSpace.PackedBgr => f.BitstreamBitsPerPixel == 32 ? _Layout.PackedBgra : _Layout.PackedBgr,
    HuffYuvColourSpace.Yuv when f.Version < 3 && f.BitstreamBitsPerPixel == 12 => _Layout.Interleaved420,
    HuffYuvColourSpace.Yuv when f.Version < 3 && f.BitstreamBitsPerPixel == 16 => _Layout.Interleaved422,
    HuffYuvColourSpace.Yuv when f.HasAlpha => throw new NotSupportedException($"Video stream {streamIndex} is planar YUVA, for which RawImage has no lossless representation."),
    HuffYuvColourSpace.Yuv when (f.ChromaHorizontalShift, f.ChromaVerticalShift) == (1, 1) => _Layout.PlanarYuv420,
    HuffYuvColourSpace.Yuv when (f.ChromaHorizontalShift, f.ChromaVerticalShift) == (1, 0) => _Layout.PlanarYuv422,
    HuffYuvColourSpace.Yuv when (f.ChromaHorizontalShift, f.ChromaVerticalShift) == (0, 1) => _Layout.PlanarYuv440,
    HuffYuvColourSpace.Yuv when (f.ChromaHorizontalShift, f.ChromaVerticalShift) == (0, 0) => _Layout.PlanarYuv444,
    _ => throw new NotSupportedException($"Video stream {streamIndex} uses a HuffYUV plane layout RawImage cannot represent losslessly."),
  };

  private static int _VersionOf(_Layout layout) => layout is _Layout.PlanarGrey or _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444 or _Layout.PlanarRgb or _Layout.PlanarRgba ? 3 : 2;

  private static bool _IsHeaderOnly(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    if (data.Length != BitmapInfoHeader.StructSize)
      return false;
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data);
    var code = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
    return size >= BitmapInfoHeader.StructSize && (code.EqualsIgnoringCase(_HFYU) || code.EqualsIgnoringCase(_FFVH));
  }

  private static ReadOnlySpan<byte> _DescriptionOf(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    if (data.IsEmpty)
      return data;
    if (data.Length >= BitmapInfoHeader.StructSize) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data);
      var code = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
      if (size >= BitmapInfoHeader.StructSize && size <= (uint)data.Length && (code.EqualsIgnoringCase(_HFYU) || code.EqualsIgnoringCase(_FFVH)))
        return data[BitmapInfoHeader.StructSize..];
    }
    return data;
  }

  // ============================================================================================
  // Description / tables
  // ============================================================================================

  private int _TableCount => this._layout switch { _Layout.PlanarGrey => 1, _Layout.PlanarRgba => 4, _ => 3 };
  private int _SymbolCount => Math.Min(1 << this._bitsPerSample, HuffYuvHuffmanTable.MAX_SYMBOL_COUNT);
  private (int H, int V) _ChromaShift => this._layout switch {
    _Layout.PlanarYuv420 => (1, 1), _Layout.PlanarYuv422 => (1, 0), _Layout.PlanarYuv440 => (0, 1), _ => (0, 0),
  };

  private PixelFormat _WorkingFormat => (this._layout, this._bitsPerSample) switch {
    (_Layout.Interleaved420 or _Layout.PlanarYuv420, 8) => PixelFormat.Yuv420P8,
    (_Layout.Interleaved422 or _Layout.PlanarYuv422, 8) => PixelFormat.Yuv422P8,
    (_Layout.PlanarYuv440, 8) => PixelFormat.Yuv440P8,
    (_Layout.PlanarYuv444, 8) => PixelFormat.Yuv444P8,
    (_Layout.PlanarYuv420, 10) => PixelFormat.Yuv420P10, (_Layout.PlanarYuv422, 10) => PixelFormat.Yuv422P10, (_Layout.PlanarYuv440, 10) => PixelFormat.Yuv440P10, (_Layout.PlanarYuv444, 10) => PixelFormat.Yuv444P10,
    (_Layout.PlanarYuv420, 12) => PixelFormat.Yuv420P12, (_Layout.PlanarYuv422, 12) => PixelFormat.Yuv422P12, (_Layout.PlanarYuv440, 12) => PixelFormat.Yuv440P12, (_Layout.PlanarYuv444, 12) => PixelFormat.Yuv444P12,
    (_Layout.PlanarYuv420, 16) => PixelFormat.Yuv420P16, (_Layout.PlanarYuv422, 16) => PixelFormat.Yuv422P16, (_Layout.PlanarYuv440, 16) => PixelFormat.Yuv440P16, (_Layout.PlanarYuv444, 16) => PixelFormat.Yuv444P16,
    (_Layout.PlanarGrey, 8) => PixelFormat.Gray8, (_Layout.PlanarGrey, 10) => PixelFormat.Gray10, (_Layout.PlanarGrey, 16) => PixelFormat.Gray16,
    (_Layout.PackedBgr or _Layout.PackedBgra, 8) => PixelFormat.Bgra32,
    (_Layout.PlanarRgb, 8) => PixelFormat.Rgb24, (_Layout.PlanarRgba, 8) => PixelFormat.Rgba32,
    (_Layout.PlanarRgb, 10) => PixelFormat.Rgb30,
    (_Layout.PlanarRgb, 16) => PixelFormat.Rgb48, (_Layout.PlanarRgba, 16) => PixelFormat.Rgba64,
    _ => throw new NotSupportedException("No exact RawImage format exists for this FFVHUFF sample layout."),
  };

  private int _StoredBitsPerPixel => RawPixelFormats.Get(this._WorkingFormat).StorageBitsPerPixel;

  public MediaStreamInfo DescribeStream() {
    if (this._description != null)
      return this._description;
    if (this._legacy)
      return this._description = this._BuildDescription(null);

    var tables = this._tables;
    if (tables == null) {
      tables = _TablesFrom(_AssumedStatistics(this._TableCount, this._SymbolCount));
      if (!this._tablesPerFrame)
        this._tables = tables;
    }
    return this._description = this._BuildDescription(tables);
  }

  private static ulong[][] _AssumedStatistics(int tableCount, int symbolCount) {
    var result = new ulong[tableCount][];
    for (var t = 0; t < tableCount; ++t) {
      var stats = result[t] = new ulong[symbolCount];
      for (var symbol = 0; symbol < symbolCount; ++symbol) {
        var distance = Math.Min(symbol, symbolCount - symbol);
        stats[symbol] = 100000000UL / (ulong)(distance * distance + 1);
      }
    }
    return result;
  }

  private static HuffYuvHuffmanCodes[] _TablesFrom(ulong[][] statistics) {
    var result = new HuffYuvHuffmanCodes[statistics.Length];
    for (var i = 0; i < result.Length; ++i)
      result[i] = HuffYuvHuffmanCodes.FromStatistics(statistics[i]);
    return result;
  }

  private MediaStreamInfo _BuildDescription(HuffYuvHuffmanCodes[]? tables) {
    var extra = new List<byte>();
    if (!this._legacy) {
      if (this._version < 3) {
        extra.Add((byte)((int)this._prediction | (this._decorrelate ? _DECORRELATE : 0)));
        extra.Add((byte)(this._layout == _Layout.Interleaved420 ? 12 : this._layout == _Layout.Interleaved422 ? 16 : this._layout == _Layout.PackedBgra ? 32 : 24));
        extra.Add((byte)((this._interlaced ? _INTERLACED : _PROGRESSIVE) | (this._tablesPerFrame ? _TABLES_PER_FRAME : 0)));
        extra.Add(0);
      } else {
        var flags = (byte)(this._interlaced ? _INTERLACED : _PROGRESSIVE);
        if (this._tablesPerFrame) flags |= _TABLES_PER_FRAME;
        var depth = (this._bitsPerSample - 1) << 4;
        if (this._layout is _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444) {
          flags |= _CHROMA;
          var (h, v) = this._ChromaShift;
          depth |= h | v << 2;
        } else if (this._layout is _Layout.PlanarRgb or _Layout.PlanarRgba) {
          flags |= _PLANAR_RGB;
          if (this._layout == _Layout.PlanarRgba) flags |= _ALPHA;
        }
        extra.Add((byte)this._prediction); extra.Add((byte)depth); extra.Add(flags); extra.Add(1);
      }
      if (!this._tablesPerFrame && tables != null)
        foreach (var table in tables) table.Store(extra);
    }

    var format = new byte[BitmapInfoHeader.StructSize + extra.Count];
    var header = format.AsSpan();
    BinaryPrimitives.WriteUInt32LittleEndian(header, this._legacy ? BitmapInfoHeader.StructSize : (uint)format.Length);
    BinaryPrimitives.WriteInt32LittleEndian(header[4..], this._width);
    BinaryPrimitives.WriteInt32LittleEndian(header[8..], this._height);
    BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)(this._legacy ? this._requested.BitsPerPixel : this._StoredBitsPerPixel));
    BinaryPrimitives.WriteUInt32LittleEndian(header[16..], this._tag.Value);
    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], checked((uint)((long)this._width * this._height * this._StoredBitsPerPixel / 8)));
    extra.CopyTo(format, BitmapInfoHeader.StructSize);

    return new() {
      Index = this._requested.Index, Kind = MediaStreamKind.Video, Codec = this._tag, Handler = this._tag, CodecId = _VFW_CODEC_ID,
      TimeBase = this._requested.TimeBase, FrameRate = this._requested.FrameRate, DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width, Height = this._height, BitsPerPixel = this._legacy ? this._requested.BitsPerPixel : this._StoredBitsPerPixel,
      CodecPrivateData = format, Language = this._requested.Language, Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // Frame / symbols
  // ============================================================================================

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException($"Expected {this._width}x{this._height}, got {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The raw picture buffer is truncated.");

    var working = this._WorkingFormat;
    var picture = frame.Format == working ? frame : FastRawImageConverter.Convert(frame, working);
    var symbols = this._Residuals(picture);
    byte[] data;
    if (this._legacy)
      data = this._WriteLegacy(symbols);
    else {
      var statistics = symbols.Statistics(this._TableCount, this._SymbolCount, this._bitsPerSample);
      var tables = this._tablesPerFrame ? _TablesFrom(statistics) : this._tables ??= _TablesFrom(statistics);
      this._description ??= this._BuildDescription(tables);
      data = this._Write(symbols, tables);
    }
    this._description ??= this.DescribeStream();
    packet = new(this._requested.Index, data, presentationTimestamp, presentationTimestamp, IsKeyFrame: true);
    return true;
  }

  private byte[] _Write(_Symbols symbols, HuffYuvHuffmanCodes[] tables) {
    var tableBytes = new List<byte>();
    if (this._tablesPerFrame)
      foreach (var table in tables) table.Store(tableBytes);
    var bits = new HuffYuvBitWriter(symbols.Count * 2 + tableBytes.Count + symbols.Raw.Length + 8);
    foreach (var value in tableBytes) bits.Write(value, 8);
    foreach (var value in symbols.Raw) bits.Write(value, 8);
    for (var i = 0; i < symbols.Count; ++i) {
      var residual = symbols.Values[i];
      tables[symbols.TableOf[i]].Write(bits, this._bitsPerSample == 16 ? residual >> 2 : residual);
      if (this._bitsPerSample == 16) bits.Write((uint)(residual & 3), 2);
    }
    return bits.End();
  }

  private byte[] _WriteLegacy(_Symbols symbols) {
    var tables = HuffYuvLegacyTable.ForBitstreamDepth(this._layout is _Layout.PackedBgr ? 24 : this._layout is _Layout.PackedBgra ? 32 : this._layout is _Layout.Interleaved420 ? 12 : 16);
    var bits = new HuffYuvBitWriter(symbols.Count + symbols.Raw.Length + 8);
    foreach (var value in symbols.Raw) bits.Write(value, 8);
    for (var i = 0; i < symbols.Count; ++i) tables[symbols.TableOf[i]].Write(bits, symbols.Values[i]);
    return bits.End();
  }

  private sealed class _Symbols {
    internal byte[] TableOf;
    internal int[] Values;
    internal byte[] Raw = [];
    internal int Count;

    internal _Symbols(int capacity) {
      this.TableOf = new byte[Math.Max(1, capacity)];
      this.Values = new int[Math.Max(1, capacity)];
    }
    internal void Add(int table, int value) {
      if (this.Count == this.Values.Length) {
        Array.Resize(ref this.Values, this.Values.Length * 2);
        Array.Resize(ref this.TableOf, this.TableOf.Length * 2);
      }
      this.TableOf[this.Count] = (byte)table;
      this.Values[this.Count++] = value;
    }
    internal void AddRow(int table, ReadOnlySpan<byte> values) { foreach (var value in values) this.Add(table, value); }
    internal ulong[][] Statistics(int tables, int symbols, int bps) {
      var result = new ulong[tables][];
      for (var i = 0; i < tables; ++i) result[i] = new ulong[symbols];
      for (var i = 0; i < this.Count; ++i) ++result[this.TableOf[i]][bps == 16 ? this.Values[i] >> 2 : this.Values[i]];
      return result;
    }
  }

  private _Symbols _Residuals(RawImage picture) {
    if (this._bitsPerSample > 8)
      return this._WidePlanes(picture);
    return this._layout switch {
      _Layout.Interleaved420 => this._InterleavedYuv(picture, true),
      _Layout.Interleaved422 => this._InterleavedYuv(picture, false),
      _Layout.PackedBgr or _Layout.PackedBgra => this._Packed(picture),
      _ => this._Planes(picture),
    };
  }

  // ============================================================================================
  // High-depth v3
  // ============================================================================================

  private _Symbols _WidePlanes(RawImage picture) {
    var symbols = new _Symbols(checked(this._width * this._height * this._TableCount));
    if (this._layout is _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444) {
      for (var p = 0; p < 3; ++p) {
        var (w, h) = picture.GetPlaneDimensions(p);
        this._CodeWidePlane(symbols, _ReadLittleEndianPlane(picture.GetPlaneData(p)), w, h, p);
      }
      return symbols;
    }
    if (this._layout == _Layout.PlanarGrey) {
      var plane = new ushort[this._width * this._height];
      var bytes = picture.PixelData.AsSpan();
      for (var i = 0; i < plane.Length; ++i)
        plane[i] = this._bitsPerSample == 10 ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(i * 2, 2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(i * 2, 2));
      this._CodeWidePlane(symbols, plane, this._width, this._height, 0);
      return symbols;
    }

    var channels = this._layout == _Layout.PlanarRgba ? 4 : 3;
    var planes = new ushort[channels][];
    for (var i = 0; i < channels; ++i) planes[i] = new ushort[this._width * this._height];
    var source = picture.PixelData.AsSpan();
    if (this._bitsPerSample == 10) {
      for (var i = 0; i < planes[0].Length; ++i) {
        var packed = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(i * 4, 4));
        planes[0][i] = (ushort)((packed >> 10) & 1023); // G
        planes[1][i] = (ushort)((packed >> 20) & 1023); // B
        planes[2][i] = (ushort)(packed & 1023); // R
      }
    } else {
      for (var i = 0; i < planes[0].Length; ++i) {
        var at = i * channels * 2;
        planes[2][i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(at, 2));
        planes[0][i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(at + 2, 2));
        planes[1][i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(at + 4, 2));
        if (channels == 4) planes[3][i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(at + 6, 2));
      }
    }
    for (var p = 0; p < channels; ++p) this._CodeWidePlane(symbols, planes[p], this._width, this._height, p);
    return symbols;
  }

  private static ushort[] _ReadLittleEndianPlane(ReadOnlySpan<byte> bytes) {
    var result = new ushort[bytes.Length / 2];
    for (var i = 0; i < result.Length; ++i) result[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(i * 2, 2));
    return result;
  }

  private void _CodeWidePlane(_Symbols symbols, ReadOnlySpan<ushort> plane, int width, int height, int table) {
    var mask = (1 << this._bitsPerSample) - 1;
    var above = this._interlaced ? 2 : 1;
    var differences = new int[width];
    var temp = new int[width];
    var left = 0; var leftAbove = 0;
    for (var y = 0; y < height; ++y) {
      var row = plane.Slice(y * width, width);
      if (y < above) {
        for (var x = 0; x < width; ++x) { var value = row[x]; differences[x] = (value - left) & mask; left = value; }
        if (y == above - 1) leftAbove = plane[0];
      } else if (this._prediction == HuffYuvPredictionMethod.Median) {
        var topRow = plane.Slice((y - above) * width, width);
        for (var x = 0; x < width; ++x) {
          var top = topRow[x]; var predicted = _Median(left, top, (left + top - leftAbove) & mask);
          var value = row[x]; differences[x] = (value - predicted) & mask; left = value; leftAbove = top;
        }
      } else if (this._prediction == HuffYuvPredictionMethod.Gradient) {
        var topRow = plane.Slice((y - above) * width, width);
        for (var x = 0; x < width; ++x) temp[x] = (row[x] - topRow[x]) & mask;
        for (var x = 0; x < width; ++x) { var value = temp[x]; differences[x] = (value - left) & mask; left = value; }
      } else {
        for (var x = 0; x < width; ++x) { var value = row[x]; differences[x] = (value - left) & mask; left = value; }
      }
      for (var x = 0; x < width; ++x) symbols.Add(table, differences[x]);
    }
  }

  private static int _Median(int a, int b, int c) { if (a > b) (a, b) = (b, a); return c < a ? a : c > b ? b : c; }

  // ============================================================================================
  // Eight-bit interleaved YUV
  // ============================================================================================

  private _Symbols _InterleavedYuv(RawImage picture, bool halfHeight) {
    var width = this._width; var height = this._height; var cw = width / 2; var ch = halfHeight ? height / 2 : height;
    var yPlane = picture.GetPlaneData(0); var uPlane = picture.GetPlaneData(1); var vPlane = picture.GetPlaneData(2);
    var result = new _Symbols(width * height * 2);
    var dy = new byte[width]; var du = new byte[cw]; var dv = new byte[cw];
    var ty = new byte[width]; var tu = new byte[cw]; var tv = new byte[cw];
    result.Raw = [vPlane[0], yPlane[1], uPlane[0], yPlane[0]];
    var ly = _SubtractLeft(yPlane[..width], dy, width, 0); var lu = _SubtractLeft(uPlane[..cw], du, cw, 0); var lv = _SubtractLeft(vPlane[..cw], dv, cw, 0);
    _AddGroups(result, dy, du, dv, 2, width);
    var above = this._interlaced ? 2 : 1; var y = 1; var cy = 1;

    if (this._prediction == HuffYuvPredictionMethod.Median) {
      if (this._interlaced) {
        ly = _SubtractLeft(yPlane.Slice(width, width), dy, width, ly); lu = _SubtractLeft(uPlane.Slice(cw, cw), du, cw, lu); lv = _SubtractLeft(vPlane.Slice(cw, cw), dv, cw, lv);
        _AddGroups(result, dy, du, dv, 0, width); y = cy = 2;
      }
      const int YLEFT = 4, CLEFT = 2;
      var yr = yPlane.Slice(y * width, width); var ur = uPlane.Slice(cy * cw, cw); var vr = vPlane.Slice(cy * cw, cw);
      var ya = yPlane.Slice((y - above) * width, width); var ua = uPlane.Slice((cy - above) * cw, cw); var va = vPlane.Slice((cy - above) * cw, cw);
      ly = _SubtractLeft(yr, dy, YLEFT, ly); lu = _SubtractLeft(ur, du, CLEFT, lu); lv = _SubtractLeft(vr, dv, CLEFT, lv);
      byte lty = ya[YLEFT - 1], ltu = ua[CLEFT - 1], ltv = va[CLEFT - 1];
      _SubtractMedian(ya[YLEFT..], yr[YLEFT..], dy.AsSpan(YLEFT), width - YLEFT, ref ly, ref lty);
      _SubtractMedian(ua[CLEFT..], ur[CLEFT..], du.AsSpan(CLEFT), cw - CLEFT, ref lu, ref ltu);
      _SubtractMedian(va[CLEFT..], vr[CLEFT..], dv.AsSpan(CLEFT), cw - CLEFT, ref lv, ref ltv);
      _AddGroups(result, dy, du, dv, 0, width); ++y; ++cy;
      while (y < height) {
        if (halfHeight) while (2 * cy > y && y < height) { _SubtractMedian(yPlane.Slice((y - above) * width, width), yPlane.Slice(y * width, width), dy, width, ref ly, ref lty); result.AddRow(0, dy); ++y; }
        if (y >= height) break;
        if (cy >= ch) throw new InvalidDataException("The 4:2:0 row schedule overran chroma.");
        _SubtractMedian(yPlane.Slice((y - above) * width, width), yPlane.Slice(y * width, width), dy, width, ref ly, ref lty);
        _SubtractMedian(uPlane.Slice((cy - above) * cw, cw), uPlane.Slice(cy * cw, cw), du, cw, ref lu, ref ltu);
        _SubtractMedian(vPlane.Slice((cy - above) * cw, cw), vPlane.Slice(cy * cw, cw), dv, cw, ref lv, ref ltv);
        _AddGroups(result, dy, du, dv, 0, width); ++y; ++cy;
      }
      return result;
    }

    while (y < height) {
      if (halfHeight) { ly = this._SubtractPredictedRow(yPlane, y, width, dy, ty, ly, above); result.AddRow(0, dy); if (++y >= height) break; }
      if (cy >= ch) throw new InvalidDataException("The interleaved row schedule overran chroma.");
      ly = this._SubtractPredictedRow(yPlane, y, width, dy, ty, ly, above); lu = this._SubtractPredictedRow(uPlane, cy, cw, du, tu, lu, above); lv = this._SubtractPredictedRow(vPlane, cy, cw, dv, tv, lv, above);
      _AddGroups(result, dy, du, dv, 0, width); ++y; ++cy;
    }
    return result;
  }

  private byte _SubtractPredictedRow(ReadOnlySpan<byte> plane, int row, int width, Span<byte> d, Span<byte> temp, byte left, int above) {
    var current = plane.Slice(row * width, width);
    if (this._prediction == HuffYuvPredictionMethod.Gradient && row >= above) { _SubtractAbove(current, plane.Slice((row - above) * width, width), temp, width); return _SubtractLeft(temp, d, width, left); }
    return _SubtractLeft(current, d, width, left);
  }

  private static void _AddGroups(_Symbols result, ReadOnlySpan<byte> y, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, int from, int width) {
    for (var x = from; x < width; x += 2) { result.Add(0, y[x]); result.Add(1, u[x / 2]); result.Add(0, y[x + 1]); result.Add(2, v[x / 2]); }
  }

  // ============================================================================================
  // Eight-bit packed RGB(A)
  // ============================================================================================

  private _Symbols _Packed(RawImage picture) {
    var stride = this._width * 4; var alpha = this._layout == _Layout.PackedBgra; var pixels = picture.PixelData.AsSpan(0, stride * this._height);
    var result = new _Symbols(this._width * this._height * 4); var differences = new byte[stride]; var temp = new byte[stride]; var left = new byte[4];
    var above = this._interlaced ? 2 : 1; var bottom = pixels.Slice((this._height - 1) * stride, stride);
    result.Raw = alpha ? [bottom[_A], bottom[_R], bottom[_G], bottom[_B]] : [bottom[_R], bottom[_G], bottom[_B], 0];
    bottom[..4].CopyTo(left); _SubtractLeftPixels(bottom[4..], differences, this._width - 1, left); this._AddPixels(result, differences, this._width - 1, alpha);
    for (var encoded = 1; encoded < this._height; ++encoded) {
      var physical = this._height - 1 - encoded; var row = pixels.Slice(physical * stride, stride);
      if (this._prediction == HuffYuvPredictionMethod.Gradient && encoded >= above) { _SubtractAbove(row, pixels.Slice((physical + above) * stride, stride), temp, stride); _SubtractLeftPixels(temp, differences, this._width, left); }
      else _SubtractLeftPixels(row, differences, this._width, left);
      this._AddPixels(result, differences, this._width, alpha);
    }
    return result;
  }

  private static void _SubtractLeftPixels(ReadOnlySpan<byte> row, Span<byte> into, int count, byte[] left) {
    for (var i = 0; i < count * 4; i += 4) for (var c = 0; c < 4; ++c) { var value = row[i + c]; into[i + c] = (byte)(value - left[c]); left[c] = value; }
  }

  private void _AddPixels(_Symbols result, ReadOnlySpan<byte> d, int count, bool alpha) {
    for (var i = 0; i < count * 4; i += 4) {
      var green = d[i + _G]; result.Add(1, green);
      result.Add(0, this._decorrelate ? (byte)(d[i + _B] - green) : d[i + _B]);
      result.Add(2, this._decorrelate ? (byte)(d[i + _R] - green) : d[i + _R]);
      if (alpha) result.Add(2, d[i + _A]);
    }
  }

  // ============================================================================================
  // Eight-bit planar v3
  // ============================================================================================

  private _Symbols _Planes(RawImage picture) {
    var result = new _Symbols(checked(this._width * this._height * this._TableCount));
    if (this._layout == _Layout.PlanarGrey) { this._CodePlane(result, picture.PixelData, this._width, this._height, 0); return result; }
    if (this._layout is _Layout.PlanarYuv420 or _Layout.PlanarYuv422 or _Layout.PlanarYuv440 or _Layout.PlanarYuv444) {
      for (var p = 0; p < 3; ++p) { var (w, h) = picture.GetPlaneDimensions(p); this._CodePlane(result, picture.GetPlaneData(p), w, h, p); }
      return result;
    }
    var extracted = new byte[this._width * this._height]; var pixels = picture.PixelData.AsSpan();
    if (this._layout == _Layout.PlanarRgb) { this._CodeChannel(result, pixels, 3, 1, extracted, 0); this._CodeChannel(result, pixels, 3, 2, extracted, 1); this._CodeChannel(result, pixels, 3, 0, extracted, 2); }
    else { this._CodeChannel(result, pixels, 4, 1, extracted, 0); this._CodeChannel(result, pixels, 4, 2, extracted, 1); this._CodeChannel(result, pixels, 4, 0, extracted, 2); this._CodeChannel(result, pixels, 4, 3, extracted, 3); }
    return result;
  }

  private void _CodeChannel(_Symbols result, ReadOnlySpan<byte> pixels, int channels, int channel, byte[] plane, int table) {
    for (int i = 0, at = channel; i < plane.Length; ++i, at += channels) plane[i] = pixels[at];
    this._CodePlane(result, plane, this._width, this._height, table);
  }

  private void _CodePlane(_Symbols result, ReadOnlySpan<byte> plane, int width, int height, int table) {
    var d = new byte[width]; var temp = new byte[width]; var above = this._interlaced ? 2 : 1; byte left = 0, leftAbove = 0;
    for (var y = 0; y < height; ++y) {
      var row = plane.Slice(y * width, width);
      if (y < above) { left = _SubtractLeft(row, d, width, left); if (y == above - 1) leftAbove = plane[0]; }
      else if (this._prediction == HuffYuvPredictionMethod.Median) _SubtractMedian(plane.Slice((y - above) * width, width), row, d, width, ref left, ref leftAbove);
      else if (this._prediction == HuffYuvPredictionMethod.Gradient) { _SubtractAbove(row, plane.Slice((y - above) * width, width), temp, width); left = _SubtractLeft(temp, d, width, left); }
      else left = _SubtractLeft(row, d, width, left);
      result.AddRow(table, d);
    }
  }

  private static byte _SubtractLeft(ReadOnlySpan<byte> row, Span<byte> into, int count, byte left) { for (var i = 0; i < count; ++i) { var v = row[i]; into[i] = (byte)(v - left); left = v; } return left; }
  private static void _SubtractAbove(ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> into, int count) { for (var i = 0; i < count; ++i) into[i] = (byte)(row[i] - above[i]); }
  private static void _SubtractMedian(ReadOnlySpan<byte> above, ReadOnlySpan<byte> row, Span<byte> into, int count, ref byte left, ref byte leftAbove) {
    var l = left; var lt = leftAbove;
    for (var i = 0; i < count; ++i) { var top = above[i]; var predicted = _Median(l, top, (byte)(l + top - lt)); lt = top; l = row[i]; into[i] = (byte)(l - predicted); }
    left = l; leftAbove = lt;
  }
  private static byte _Median(byte a, byte b, byte c) { if (a > b) (a, b) = (b, a); return c < a ? a : c > b ? b : c; }
}
