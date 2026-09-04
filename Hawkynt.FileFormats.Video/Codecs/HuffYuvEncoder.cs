using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes HuffYUV and its extension FFVHUFF: each sample predicted from its neighbours and the
/// difference Huffman coded with one table per plane.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/huffyuvenc.c</c>, copyright (c) 2002-2014 Michael
/// Niedermayer, with the code construction of <c>libavcodec/huffman.c</c> and the table assignment
/// of <c>libavcodec/huffyuv.c</c>; all are distributed there under LGPL-2.1-or-later. This
/// adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// Lossless and intra only: every packet is one whole picture and a key frame, and what
/// <see cref="HuffYuvDecoder"/> hands back from it is the picture that went in, sample for sample.
/// <para/>
/// <b>What it writes.</b> Eight-bit samples, progressive frames, and the Huffman tables in the
/// stream description rather than in every frame. Five layouts, chosen by the depth the requested
/// stream states or by the description it carries: 4:2:2 luminance and chrominance coded as
/// <c>Y U Y V</c> groups along each row, from a <see cref="PixelFormat.Yuv422P8"/> picture; colour a
/// pixel at a time, bottom row first, at twenty-four or thirty-two bits with red and blue stored as
/// their distance from green; and the planar form of the extension — grey, or green, blue and red
/// planes with or without alpha, each coded through to its last row before the next begins. The
/// first two are what the original codec writes and are tagged <c>HFYU</c>; the planar form exists
/// only in the extension and is tagged <c>FFVH</c>. Either tag is written on request, since every
/// reader takes both.
/// <para/>
/// <b>The tables are made from the first picture.</b> Each plane's symbol counts over the first
/// frame become that plane's code lengths, and every frame after it is coded with the same tables —
/// they are in the description, which a container writes once. A caller that asks for the
/// description before handing over a picture fixes the tables at that moment instead, from the
/// distribution the reference encoder assumes when it has seen nothing, so the description handed
/// out is always the one the packets were coded against.
/// <para/>
/// <b>What refuses.</b> 4:2:0, which the packed form codes as rows that alternate carrying
/// chrominance and rows that do not and which nothing here has been measured on; the planar form's
/// luminance-and-chrominance layouts; interlaced frames; tables in every frame; samples deeper than
/// eight bits; median prediction with the packed colour layout, which the reference encoder refuses
/// too; and a 4:2:2 picture of odd width, which has no whole number of groups to a row.
/// </remarks>
public sealed class HuffYuvEncoder : IVideoCodecEncoder<HuffYuvEncoder> {

  /// <summary>The Matroska name for a track described by a <c>BITMAPINFOHEADER</c>.</summary>
  private const string _VFW_CODEC_ID = "V_MS/VFW/FOURCC";

  private const int _SYMBOL_COUNT = HuffYuvHuffmanTable.SYMBOL_COUNT;
  private const byte _PROGRESSIVE = 0x20;
  private const byte _DECORRELATE = 0x40;
  private const byte _PLANAR_RGB = 0x02;
  private const byte _ALPHA = 0x04;
  private const int _B = 0;
  private const int _G = 1;
  private const int _R = 2;
  private const int _A = 3;

  private static readonly CodecTag _HFYU = CodecTag.FromCharacters("HFYU");
  private static readonly CodecTag _FFVH = CodecTag.FromCharacters("FFVH");

  /// <summary>The frame layouts written, each with the form of description that names it.</summary>
  private enum _Layout {

    /// <summary>The second form at sixteen bits: <c>Y U Y V</c> groups along each row.</summary>
    Interleaved422,

    /// <summary>The second form at twenty-four bits: blue, green, red a pixel at a time, bottom row first.</summary>
    PackedBgr,

    /// <summary>The second form at thirty-two bits: the same with an alpha channel.</summary>
    PackedBgra,

    /// <summary>The third form with one plane.</summary>
    PlanarGrey,

    /// <summary>The third form with green, blue and red planes.</summary>
    PlanarRgb,

    /// <summary>The third form with green, blue, red and alpha planes.</summary>
    PlanarRgba,
  }

  private readonly MediaStreamInfo _requested;
  private readonly CodecTag _tag;
  private readonly _Layout _layout;
  private readonly HuffYuvPredictionMethod _prediction;
  private readonly int _width;
  private readonly int _height;
  private HuffYuvHuffmanCodes[]? _tables;
  private MediaStreamInfo? _description;

  private HuffYuvEncoder(MediaStreamInfo requested, CodecTag tag, _Layout layout, HuffYuvPredictionMethod prediction) {
    this._requested = requested;
    this._tag = tag;
    this._layout = layout;
    this._prediction = prediction;
    this._width = requested.Width;
    this._height = requested.Height;
  }

  public static string CodecName => "HuffYUV / FFVHUFF";

  public static CodecTag Codec => _HFYU;

  // ============================================================================================
  // Setting up
  // ============================================================================================

  /// <summary>
  /// Builds an encoder for the stream described.
  /// </summary>
  /// <remarks>
  /// The layout follows the description where the stream carries one — a <c>BITMAPINFOHEADER</c>
  /// with the codec's four bytes behind it, as a demuxer hands it over, or those four bytes alone —
  /// so that a HuffYUV stream read from one container is written into another in the same layout
  /// with the same predictor. Where it carries none, the depth decides: eight bits is grey, sixteen
  /// is 4:2:2, twenty-four and thirty-two are colour a pixel at a time, and a stream that states no
  /// depth is written at twenty-four. The predictor is then left, as the reference encoder's is.
  /// </remarks>
  public static HuffYuvEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);

    var description = _DescriptionOf(stream);
    if (description.IsEmpty)
      return _Build(stream, _LayoutOfDepth(stream, planar: false), HuffYuvPredictionMethod.Left);

    if (description.Length < 4)
      throw new NotSupportedException(
        $"Video stream {stream.Index} carries a {description.Length}-byte HuffYUV description, where the codec's description is four bytes followed by its tables. A stream with no description at all is written in the layout its depth implies.");

    var format = HuffYuvFormat.Parse(description, stream.BitsPerPixel, stream.Index);
    if (format.Interlaced)
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for interlaced frames. Only progressive HuffYUV is written here; a frame of two fields predicts each row from the one two rows up and nothing here has been measured against one.");

    if (format.TablesPerFrame)
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for Huffman tables in every frame. The tables are written once, in the stream description, and the adaptive form that carries them in each frame is not written here.");

    var layout = format.ColourSpace switch {
      HuffYuvColourSpace.Grey => _Layout.PlanarGrey,
      HuffYuvColourSpace.PlanarRgb => format.HasAlpha ? _Layout.PlanarRgba : _Layout.PlanarRgb,
      HuffYuvColourSpace.PackedBgr => format.BitstreamBitsPerPixel == 32 ? _Layout.PackedBgra : _Layout.PackedBgr,
      HuffYuvColourSpace.Yuv when format.Version == 2 && format.BitstreamBitsPerPixel == 16 => _Layout.Interleaved422,
      HuffYuvColourSpace.Yuv when format.Version == 2 => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for 4:2:0 coded as interleaved rows. Only 4:2:2 is written in that form; 4:2:0 alternates rows that carry chrominance with rows that do not, and nothing here has been measured against one."),
      _ => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for luminance and chrominance planes coded one after another. The planar form is written for grey and for green, blue and red only; 4:2:2 is written as interleaved groups instead."),
    };

    return _Build(stream, layout, (HuffYuvPredictionMethod)format.Predictor);
  }

  /// <summary>
  /// Builds an encoder with the predictor and form chosen outright, for a caller that has no
  /// description to hand over.
  /// </summary>
  /// <param name="stream">The stream to write, whose <see cref="MediaStreamInfo.BitsPerPixel"/>
  /// picks the layout: eight bits is grey, sixteen is 4:2:2, twenty-four and thirty-two are colour
  /// with and without alpha, and zero is taken as twenty-four.</param>
  /// <param name="prediction">How each sample is predicted.</param>
  /// <param name="planar">Whether colour is written as green, blue and red planes — the extension's
  /// form, tagged <c>FFVH</c> unless the stream asks for the other tag — rather than a pixel at a
  /// time as the original codec has it. Grey is always planar.</param>
  public static HuffYuvEncoder Create(MediaStreamInfo stream, HuffYuvPredictionMethod prediction, bool planar = false) {
    ArgumentNullException.ThrowIfNull(stream);
    _RefuseUnusableGeometry(stream);

    if (prediction is not (HuffYuvPredictionMethod.Left or HuffYuvPredictionMethod.Gradient or HuffYuvPredictionMethod.Median))
      throw new NotSupportedException($"{(int)prediction} is not one of the three prediction methods HuffYUV codes with: left, gradient and median.");

    return _Build(stream, _LayoutOfDepth(stream, planar), prediction);
  }

  private static void _RefuseUnusableGeometry(MediaStreamInfo stream) {
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"HuffYUV codes pictures; stream {stream.Index} is {stream.Kind}.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, and the size has to be known before the first frame because the stream description states it.");
  }

  private static HuffYuvEncoder _Build(MediaStreamInfo stream, _Layout layout, HuffYuvPredictionMethod prediction) {
    if (layout == _Layout.Interleaved422 && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} is {stream.Width} pixels wide, which 4:2:2 HuffYUV cannot code: a row is written as groups of two pixels, so the width has to be even.");

    if (layout == _Layout.Interleaved422 && prediction == HuffYuvPredictionMethod.Median && (stream.Width < 4 || stream.Height < 2))
      throw new NotSupportedException(
        $"Video stream {stream.Index} is {stream.Width}x{stream.Height}, which 4:2:2 HuffYUV cannot code with median prediction: the reference coder reads a second row and four luminance samples of it whatever the picture's size, so a picture with less has no defined coding. Left and gradient prediction code it.");

    if ((layout is _Layout.PackedBgr or _Layout.PackedBgra) && prediction == HuffYuvPredictionMethod.Median)
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for median prediction with colour coded a pixel at a time, which HuffYUV does not combine — the reference encoder refuses it as well. Left and gradient prediction are written in that layout, and median in the planar one.");

    var version = layout is _Layout.PlanarGrey or _Layout.PlanarRgb or _Layout.PlanarRgba ? 3 : 2;
    var tag = stream.Codec.EqualsIgnoringCase(_FFVH) ? _FFVH
      : stream.Codec.EqualsIgnoringCase(_HFYU) ? _HFYU
      : version == 3 ? _FFVH : _HFYU;

    return new(stream, tag, layout, prediction);
  }

  private static _Layout _LayoutOfDepth(MediaStreamInfo stream, bool planar) => stream.BitsPerPixel switch {
    8 => _Layout.PlanarGrey,
    16 when !planar => _Layout.Interleaved422,
    16 => throw new NotSupportedException(
      $"Video stream {stream.Index} asks for 4:2:2 in the planar form, which is not written here; 4:2:2 is written as interleaved groups, and the planar form is written for grey and for green, blue and red."),
    0 or 24 => planar ? _Layout.PlanarRgb : _Layout.PackedBgr,
    32 => planar ? _Layout.PlanarRgba : _Layout.PackedBgra,
    _ => throw new NotSupportedException(
      $"Video stream {stream.Index} states {stream.BitsPerPixel} bits a pixel, which is none of the depths HuffYUV is written at here: 8 for grey, 16 for 4:2:2, 24 and 32 for colour with and without alpha."),
  };

  /// <summary>
  /// The four description bytes a request carries, whether behind a <c>BITMAPINFOHEADER</c> or alone.
  /// </summary>
  /// <remarks>
  /// The two are told apart by the header's own fields rather than by length: a header states its
  /// size in its first four bytes and the codec's code in its seventeenth to twentieth, and no
  /// description begins with either.
  /// </remarks>
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
  // The stream
  // ============================================================================================

  /// <summary>
  /// Describes the stream the packets belong to: the tag, the depth, and a <c>BITMAPINFOHEADER</c>
  /// with the codec's four bytes and its Huffman tables behind it.
  /// </summary>
  /// <remarks>
  /// Fixes the tables if nothing has yet. A description handed out before the first picture is
  /// coded against the reference encoder's assumed distribution, which favours small differences;
  /// one handed out after it is coded against that picture's own counts.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._description == null)
      this._LockTables(_AssumedStatistics(this._TableCount));

    return this._description!;
  }

  private int _TableCount => this._layout switch {
    _Layout.PlanarGrey => 1,
    _Layout.PlanarRgba => 4,
    _ => 3,
  };

  private int _Version => this._layout is _Layout.PlanarGrey or _Layout.PlanarRgb or _Layout.PlanarRgba ? 3 : 2;

  private int _BitsPerPixel => this._layout switch {
    _Layout.PlanarGrey => 8,
    _Layout.Interleaved422 => 16,
    _Layout.PackedBgra or _Layout.PlanarRgba => 32,
    _ => 24,
  };

  private PixelFormat _WorkingFormat => this._layout switch {
    _Layout.Interleaved422 => PixelFormat.Yuv422P8,
    _Layout.PackedBgr or _Layout.PackedBgra => PixelFormat.Bgra32,
    _Layout.PlanarGrey => PixelFormat.Gray8,
    _Layout.PlanarRgb => PixelFormat.Rgb24,
    _ => PixelFormat.Rgba32,
  };

  /// <summary>
  /// What the reference encoder counts on when it has seen no picture: a difference of
  /// <i>d</i> either way is about <i>1/(d²+1)</i> as likely as a difference of nought.
  /// </summary>
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

  private void _LockTables(ulong[][] statistics) {
    var tables = new HuffYuvHuffmanCodes[statistics.Length];
    for (var i = 0; i < tables.Length; ++i)
      tables[i] = HuffYuvHuffmanCodes.FromStatistics(statistics[i]);

    var extra = new List<byte>();
    if (this._Version == 2) {
      var decorrelate = this._layout is _Layout.PackedBgr or _Layout.PackedBgra;
      extra.Add((byte)((int)this._prediction | (decorrelate ? _DECORRELATE : 0)));
      extra.Add((byte)this._BitsPerPixel);
      extra.Add(_PROGRESSIVE);
      extra.Add(0);
    } else {
      var flags = _PROGRESSIVE;
      if (this._layout != _Layout.PlanarGrey)
        flags |= _PLANAR_RGB;
      if (this._layout == _Layout.PlanarRgba)
        flags |= _ALPHA;

      extra.Add((byte)this._prediction);
      extra.Add(0x70);
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
    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)(this._width * this._height * this._BitsPerPixel / 8));
    extra.CopyTo(format, BitmapInfoHeader.StructSize);

    this._tables = tables;
    this._description = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = this._tag,
      Handler = this._tag,
      CodecId = _VFW_CODEC_ID,
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = this._BitsPerPixel,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // A frame
  // ============================================================================================

  /// <summary>Codes one picture as one packet, which for this codec is always a key frame.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"The encoder was created for {this._width}x{this._height} pictures and the stream description states that size, but received {frame.Width}x{frame.Height}.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var working = this._WorkingFormat;
    var picture = frame.Format == working ? frame : FastRawImageConverter.Convert(frame, working);
    var symbols = this._Residuals(picture);

    if (this._tables == null)
      this._LockTables(symbols.Statistics(this._TableCount));

    packet = new(
      StreamIndex: this._requested.Index,
      Data: this._Write(symbols),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  private byte[] _Write(_Symbols symbols) {
    var tables = this._tables!;
    var bits = new HuffYuvBitWriter(symbols.Count + 8);

    foreach (var raw in symbols.Raw)
      bits.Write(raw, 8);

    for (var i = 0; i < symbols.Count; ++i)
      tables[symbols.TableOf[i]].Write(bits, symbols.Values[i]);

    return bits.End();
  }

  /// <summary>
  /// A frame's coded differences in the order they are written, each with the table that codes it.
  /// </summary>
  /// <remarks>
  /// Held as a whole rather than written as it is made, because the first frame's differences are
  /// counted before they are coded — the tables come from them — and a second pass over a picture is
  /// simpler than a second prediction of it.
  /// </remarks>
  private sealed class _Symbols {

    internal _Symbols(int capacity) {
      this.TableOf = new byte[capacity];
      this.Values = new byte[capacity];
    }

    /// <summary>The bytes written raw in front of the first coded difference, where a layout has any.</summary>
    internal byte[] Raw { get; set; } = [];

    internal byte[] TableOf { get; }
    internal byte[] Values { get; }
    internal int Count { get; private set; }

    internal void Add(int table, byte value) {
      this.TableOf[this.Count] = (byte)table;
      this.Values[this.Count] = value;
      ++this.Count;
    }

    internal void AddRow(int table, ReadOnlySpan<byte> values) {
      foreach (var value in values)
        this.Add(table, value);
    }

    internal ulong[][] Statistics(int tables) {
      var statistics = new ulong[tables][];
      for (var i = 0; i < tables; ++i)
        statistics[i] = new ulong[_SYMBOL_COUNT];

      for (var i = 0; i < this.Count; ++i)
        ++statistics[this.TableOf[i]][this.Values[i]];

      return statistics;
    }
  }

  private _Symbols _Residuals(RawImage picture) => this._layout switch {
    _Layout.Interleaved422 => this._Interleaved422(picture),
    _Layout.PackedBgr or _Layout.PackedBgra => this._Packed(picture),
    _ => this._Planes(picture),
  };

  // ============================================================================================
  // The interleaved shape
  // ============================================================================================

  /// <summary>
  /// 4:2:2 as <c>Y U Y V</c> groups along each row, with the first four samples of the frame raw.
  /// </summary>
  /// <remarks>
  /// The raw samples are the second chrominance sample, the second luminance sample, the first
  /// chrominance sample and the first luminance sample, in that order — the <c>Y U Y V</c> of the
  /// first group read back to front, which is how the word swap leaves it. Every difference after
  /// them runs from those samples, so the first row is coded from its third luminance sample on.
  /// <para/>
  /// Median prediction has one more row that is not quite median: the second row's first four
  /// luminance samples and first two of each chrominance are differences from the left, because a
  /// median needs a sample above-left and the row above has only just begun. The decoder reads
  /// exactly that, and a picture busy enough for the two to disagree is what showed it.
  /// </remarks>
  private _Symbols _Interleaved422(RawImage picture) {
    var width = this._width;
    var height = this._height;
    var chromaWidth = width / 2;
    var luma = picture.GetPlaneData(0);
    var cb = picture.GetPlaneData(1);
    var cr = picture.GetPlaneData(2);
    var symbols = new _Symbols(width * height * 2);
    var dY = new byte[width];
    var dU = new byte[chromaWidth];
    var dV = new byte[chromaWidth];
    var tY = new byte[width];
    var tU = new byte[chromaWidth];
    var tV = new byte[chromaWidth];

    symbols.Raw = [cr[0], luma[1], cb[0], luma[0]];

    var leftY = _SubtractLeft(luma[..width], dY, width, 0);
    var leftU = _SubtractLeft(cb[..chromaWidth], dU, chromaWidth, 0);
    var leftV = _SubtractLeft(cr[..chromaWidth], dV, chromaWidth, 0);
    _AddGroups(symbols, dY, dU, dV, 2, width);

    var y = 1;
    if (this._prediction == HuffYuvPredictionMethod.Median && height > 1) {
      var lumaLeft = Math.Min(4, width);
      var chromaLeft = Math.Min(2, chromaWidth);
      var rowY = luma.Slice(width, width);
      var rowU = cb.Slice(chromaWidth, chromaWidth);
      var rowV = cr.Slice(chromaWidth, chromaWidth);
      var aboveY = luma[..width];
      var aboveU = cb[..chromaWidth];
      var aboveV = cr[..chromaWidth];

      leftY = _SubtractLeft(rowY, dY, lumaLeft, leftY);
      leftU = _SubtractLeft(rowU, dU, chromaLeft, leftU);
      leftV = _SubtractLeft(rowV, dV, chromaLeft, leftV);

      var leftAboveY = aboveY[lumaLeft - 1];
      var leftAboveU = aboveU[chromaLeft - 1];
      var leftAboveV = aboveV[chromaLeft - 1];
      _SubtractMedian(aboveY[lumaLeft..], rowY[lumaLeft..], dY.AsSpan(lumaLeft), width - lumaLeft, ref leftY, ref leftAboveY);
      _SubtractMedian(aboveU[chromaLeft..], rowU[chromaLeft..], dU.AsSpan(chromaLeft), chromaWidth - chromaLeft, ref leftU, ref leftAboveU);
      _SubtractMedian(aboveV[chromaLeft..], rowV[chromaLeft..], dV.AsSpan(chromaLeft), chromaWidth - chromaLeft, ref leftV, ref leftAboveV);
      _AddGroups(symbols, dY, dU, dV, 0, width);

      for (y = 2; y < height; ++y) {
        _SubtractMedian(luma.Slice((y - 1) * width, width), luma.Slice(y * width, width), dY, width, ref leftY, ref leftAboveY);
        _SubtractMedian(cb.Slice((y - 1) * chromaWidth, chromaWidth), cb.Slice(y * chromaWidth, chromaWidth), dU, chromaWidth, ref leftU, ref leftAboveU);
        _SubtractMedian(cr.Slice((y - 1) * chromaWidth, chromaWidth), cr.Slice(y * chromaWidth, chromaWidth), dV, chromaWidth, ref leftV, ref leftAboveV);
        _AddGroups(symbols, dY, dU, dV, 0, width);
      }

      return symbols;
    }

    for (; y < height; ++y) {
      var rowY = luma.Slice(y * width, width);
      var rowU = cb.Slice(y * chromaWidth, chromaWidth);
      var rowV = cr.Slice(y * chromaWidth, chromaWidth);

      if (this._prediction == HuffYuvPredictionMethod.Gradient) {
        _SubtractAbove(rowY, luma.Slice((y - 1) * width, width), tY, width);
        _SubtractAbove(rowU, cb.Slice((y - 1) * chromaWidth, chromaWidth), tU, chromaWidth);
        _SubtractAbove(rowV, cr.Slice((y - 1) * chromaWidth, chromaWidth), tV, chromaWidth);
        leftY = _SubtractLeft(tY, dY, width, leftY);
        leftU = _SubtractLeft(tU, dU, chromaWidth, leftU);
        leftV = _SubtractLeft(tV, dV, chromaWidth, leftV);
      } else {
        leftY = _SubtractLeft(rowY, dY, width, leftY);
        leftU = _SubtractLeft(rowU, dU, chromaWidth, leftU);
        leftV = _SubtractLeft(rowV, dV, chromaWidth, leftV);
      }

      _AddGroups(symbols, dY, dU, dV, 0, width);
    }

    return symbols;
  }

  /// <summary>Appends one row's differences as <c>Y U Y V</c> groups, from a given luminance sample on.</summary>
  private static void _AddGroups(_Symbols symbols, ReadOnlySpan<byte> dY, ReadOnlySpan<byte> dU, ReadOnlySpan<byte> dV, int from, int width) {
    for (var x = from; x < width; x += 2) {
      symbols.Add(0, dY[x]);
      symbols.Add(1, dU[x / 2]);
      symbols.Add(0, dY[x + 1]);
      symbols.Add(2, dV[x / 2]);
    }
  }

  // ============================================================================================
  // The packed shape
  // ============================================================================================

  /// <summary>
  /// Colour a pixel at a time, blue first, bottom row first, with red and blue coded as their
  /// distance from green.
  /// </summary>
  /// <remarks>
  /// The first pixel is raw and takes a whole word: alpha, red, green, blue where the stream has an
  /// alpha channel, and red, green, blue and a spare byte where it has not. Everything after it is a
  /// difference from the pixel to the left — or, under gradient prediction, from the left after the
  /// row below has been taken away — and the decorrelation is applied to those differences rather
  /// than to the samples, which comes to the same thing because both are additions.
  /// <para/>
  /// Alpha is coded with the red plane's table, as the reference encoder has it. Blue is table
  /// nought, green is table one, and the order within a pixel is green, blue, red, alpha.
  /// </remarks>
  private _Symbols _Packed(RawImage picture) {
    var width = this._width;
    var height = this._height;
    var stride = width * 4;
    var hasAlpha = this._layout == _Layout.PackedBgra;
    var pixels = picture.PixelData.AsSpan(0, stride * height);
    var symbols = new _Symbols(width * height * 4);
    var differences = new byte[stride];
    var against = new byte[stride];
    var left = new byte[4];

    var bottom = pixels.Slice((height - 1) * stride, stride);
    symbols.Raw = hasAlpha
      ? [bottom[_A], bottom[_R], bottom[_G], bottom[_B]]
      : [bottom[_R], bottom[_G], bottom[_B], 0];

    bottom[..4].CopyTo(left);
    _SubtractLeftPixels(bottom[4..], differences, width - 1, left);
    _AddPixels(symbols, differences, width - 1, hasAlpha);

    for (var y = 1; y < height; ++y) {
      var row = pixels.Slice((height - 1 - y) * stride, stride);
      if (this._prediction == HuffYuvPredictionMethod.Gradient) {
        _SubtractAbove(row, pixels.Slice((height - y) * stride, stride), against, stride);
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
        var value = row[i + channel];
        into[i + channel] = (byte)(value - left[channel]);
        left[channel] = value;
      }
  }

  private static void _AddPixels(_Symbols symbols, ReadOnlySpan<byte> differences, int count, bool hasAlpha) {
    for (var i = 0; i < count * 4; i += 4) {
      var green = differences[i + _G];
      symbols.Add(1, green);
      symbols.Add(0, (byte)(differences[i + _B] - green));
      symbols.Add(2, (byte)(differences[i + _R] - green));
      if (hasAlpha)
        symbols.Add(2, differences[i + _A]);
    }
  }

  // ============================================================================================
  // The planar shape
  // ============================================================================================

  /// <summary>
  /// The third form: every plane coded through to its last row before the next one begins, in
  /// the order green, blue, red, alpha — or the one grey plane.
  /// </summary>
  /// <remarks>
  /// Nothing is raw here; the first sample of a plane is its difference from nought. Median
  /// prediction begins on the second row with the first sample of the first row as its above-left,
  /// and the left carried in from the end of the row before, which is how the decoder starts too.
  /// </remarks>
  private _Symbols _Planes(RawImage picture) {
    var width = this._width;
    var height = this._height;
    var count = width * height;
    var symbols = new _Symbols(count * this._TableCount);
    var plane = new byte[count];
    var pixels = picture.PixelData.AsSpan();

    switch (this._layout) {
      case _Layout.PlanarGrey:
        this._CodePlane(symbols, pixels[..count], 0);
        break;
      case _Layout.PlanarRgb:
        this._CodeChannel(symbols, pixels, 3, 1, plane, 0);
        this._CodeChannel(symbols, pixels, 3, 2, plane, 1);
        this._CodeChannel(symbols, pixels, 3, 0, plane, 2);
        break;
      default:
        this._CodeChannel(symbols, pixels, 4, 1, plane, 0);
        this._CodeChannel(symbols, pixels, 4, 2, plane, 1);
        this._CodeChannel(symbols, pixels, 4, 0, plane, 2);
        this._CodeChannel(symbols, pixels, 4, 3, plane, 3);
        break;
    }

    return symbols;
  }

  private void _CodeChannel(_Symbols symbols, ReadOnlySpan<byte> pixels, int channels, int channel, byte[] plane, int table) {
    for (int i = 0, at = channel; i < plane.Length; ++i, at += channels)
      plane[i] = pixels[at];

    this._CodePlane(symbols, plane, table);
  }

  private void _CodePlane(_Symbols symbols, ReadOnlySpan<byte> plane, int table) {
    var width = this._width;
    var height = this._height;
    var differences = new byte[width];
    var against = new byte[width];

    var left = _SubtractLeft(plane[..width], differences, width, 0);
    symbols.AddRow(table, differences);

    if (this._prediction == HuffYuvPredictionMethod.Median) {
      var leftAbove = plane[0];
      for (var y = 1; y < height; ++y) {
        _SubtractMedian(plane.Slice((y - 1) * width, width), plane.Slice(y * width, width), differences, width, ref left, ref leftAbove);
        symbols.AddRow(table, differences);
      }

      return;
    }

    for (var y = 1; y < height; ++y) {
      var row = plane.Slice(y * width, width);
      if (this._prediction == HuffYuvPredictionMethod.Gradient) {
        _SubtractAbove(row, plane.Slice((y - 1) * width, width), against, width);
        left = _SubtractLeft(against, differences, width, left);
      } else
        left = _SubtractLeft(row, differences, width, left);

      symbols.AddRow(table, differences);
    }
  }

  // ============================================================================================
  // The three predictions, as differences
  // ============================================================================================

  /// <summary>Each sample less the one before it, running on from a starting value.</summary>
  /// <returns>The last sample, which is where the next row runs on from.</returns>
  private static byte _SubtractLeft(ReadOnlySpan<byte> row, Span<byte> into, int count, byte left) {
    for (var i = 0; i < count; ++i) {
      var value = row[i];
      into[i] = (byte)(value - left);
      left = value;
    }

    return left;
  }

  /// <summary>A row less the row above it, sample for sample.</summary>
  private static void _SubtractAbove(ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> into, int count) {
    for (var i = 0; i < count; ++i)
      into[i] = (byte)(row[i] - above[i]);
  }

  /// <summary>Each sample less the median of its left, its top and the plane through both.</summary>
  private static void _SubtractMedian(
    ReadOnlySpan<byte> above, ReadOnlySpan<byte> row, Span<byte> into, int count, ref byte left, ref byte leftAbove) {
    var l = left;
    var lt = leftAbove;

    for (var i = 0; i < count; ++i) {
      var t = above[i];
      var predicted = _Median(l, t, (byte)(l + t - lt));
      lt = t;
      l = row[i];
      into[i] = (byte)(l - predicted);
    }

    left = l;
    leftAbove = lt;
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }
}
