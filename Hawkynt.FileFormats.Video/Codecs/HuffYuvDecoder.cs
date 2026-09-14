using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes HuffYUV and FFVHUFF, including classic headerless streams and high-depth v3 planes.</summary>
/// <remarks>
/// The codec is lossless and intra-only. Version 1 has no codec description and therefore uses the
/// historic fixed Huffman books; versions 2 and 3 carry code lengths in the stream or, optionally,
/// in every frame. FFVHUFF v3 expands the residual alphabet with sample depth. At sixteen bits the
/// table still has 16384 symbols and each residual's two low bits follow its Huffman symbol literally.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HuffYuvDecoder : IVideoCodecDecoder<HuffYuvDecoder> {

  private static readonly CodecTag _HFYU = CodecTag.FromCharacters("HFYU");
  private static readonly CodecTag _FFVH = CodecTag.FromCharacters("FFVH");
  private static readonly CodecTag[] _Tags = [_HFYU, _FFVH];

  private readonly int _width;
  private readonly int _height;
  private readonly HuffYuvFormat _format;
  private readonly IHuffYuvDecoderTable[]? _tables;

  private sealed class _LegacyAdapter(HuffYuvLegacyTable table) : IHuffYuvDecoderTable {
    public int Read(HuffYuvBitReader bits) => table.Read(bits);
  }

  private HuffYuvDecoder(int width, int height, HuffYuvFormat format, IHuffYuvDecoderTable[]? tables) {
    this._width = width;
    this._height = height;
    this._format = format;
    this._tables = tables;
  }

  public static string CodecName => "HuffYUV / FFVHUFF";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;
    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;
    return false;
  }

  public static HuffYuvDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"Video stream {stream.Index} states an invalid HuffYUV picture size of {stream.Width}x{stream.Height}.");

    var extra = _DescriptionOf(stream);
    var format = HuffYuvFormat.Parse(extra, stream.BitsPerPixel, stream.Index, stream.Height);
    _RefuseUnrepresentableHighDepth(format, stream.Index);
    _RefusePackedColourMedian(format, stream.Index);
    _RefuseUndersizedInterleavedMedian(format, stream.Width, stream.Height, stream.Index);

    IHuffYuvDecoderTable[]? tables;
    if (format.UsesLegacyTables) {
      var classic = HuffYuvLegacyTable.ForBitstreamDepth(format.BitstreamBitsPerPixel);
      tables = new IHuffYuvDecoderTable[classic.Length];
      for (var i = 0; i < classic.Length; ++i)
        tables[i] = new _LegacyAdapter(classic[i]);
    } else if (format.TablesPerFrame)
      tables = null;
    else
      tables = HuffYuvHuffmanTable.ReadAll(extra, 4, format.TableCount, format.HuffmanSymbolCount, out _);

    return new(stream.Width, stream.Height, format, tables);
  }

  private static ReadOnlySpan<byte> _DescriptionOf(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData.Span;
    if (data.IsEmpty)
      return data;
    if (data.Length >= BitmapInfoHeader.StructSize) {
      var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
      var code = new CodecTag(BinaryPrimitives.ReadUInt32LittleEndian(data[16..]));
      if (headerSize >= BitmapInfoHeader.StructSize && headerSize <= (uint)data.Length && (code.EqualsIgnoringCase(_HFYU) || code.EqualsIgnoringCase(_FFVH)))
        return data[BitmapInfoHeader.StructSize..];
    }
    return data;
  }

  private static void _RefuseUnrepresentableHighDepth(HuffYuvFormat format, int streamIndex) {
    if (format.BitsPerSample <= 8)
      return;
    if (format.HasAlpha && format.ColourSpace == HuffYuvColourSpace.Yuv)
      throw new NotSupportedException($"Video stream {streamIndex} is high-depth planar YUVA; RawImage has no planar YUVA representation to preserve those samples losslessly.");

    var represented = format.ColourSpace switch {
      HuffYuvColourSpace.Yuv => format.BitsPerSample is 10 or 12 or 16 && format.ChromaHorizontalShift <= 1 && format.ChromaVerticalShift <= 1,
      HuffYuvColourSpace.Grey => format.BitsPerSample is 10 or 16,
      HuffYuvColourSpace.PlanarRgb => format.BitsPerSample is 10 or 16 && (!format.HasAlpha || format.BitsPerSample == 16),
      _ => false,
    };
    if (!represented)
      throw new NotSupportedException($"Video stream {streamIndex} uses {format.BitsPerSample}-bit {format.ColourSpace} FFVHUFF samples in a layout the current RawImage formats cannot represent losslessly.");
  }

  private static void _RefusePackedColourMedian(HuffYuvFormat format, int streamIndex) {
    if (format.ColourSpace == HuffYuvColourSpace.PackedBgr && format.Predictor == HuffYuvPredictor.Median)
      throw new NotSupportedException($"Video stream {streamIndex} states median prediction with packed colour, which HuffYUV does not combine.");
  }

  private static void _RefuseUndersizedInterleavedMedian(HuffYuvFormat format, int width, int height, int streamIndex) {
    if (format.Version >= 3 || format.ColourSpace != HuffYuvColourSpace.Yuv || format.Predictor != HuffYuvPredictor.Median)
      return;
    var rowsBeforeMedian = format.Interlaced ? 2 : 1;
    var chromaRows = format.BitstreamBitsPerPixel == 12 ? height >> 1 : height;
    if (width >= 4 && height > rowsBeforeMedian && chromaRows > rowsBeforeMedian)
      return;
    throw new NotSupportedException($"Video stream {streamIndex} is too small for the classic interleaved median-prediction prelude.");
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var bits = new HuffYuvBitReader(packet.Data.Span);
    var tables = this._tables;
    if (tables == null) {
      var read = HuffYuvHuffmanTable.ReadAll(bits.Swapped, 0, this._format.TableCount, this._format.HuffmanSymbolCount, out var start);
      tables = read;
      bits.SeekToByte(start);
    }

    frame = this._format.BitsPerSample > 8
      ? this._DecodeHighDepth(bits, tables)
      : this._format.ColourSpace == HuffYuvColourSpace.PackedBgr
        ? this._DecodePackedColour(bits, tables)
        : this._Compose(this._format.Version >= 3 ? this._DecodePlaneAtATime(bits, tables) : this._DecodeInterleavedRows(bits, tables));
    return true;
  }

  // ============================================================================================
  // High-depth v3 planes
  // ============================================================================================

  private RawImage _DecodeHighDepth(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables) {
    if (this._format.Version < 3)
      throw new InvalidDataException("High-depth samples require the planar FFVHUFF stream description.");

    var widths = new int[this._format.TableCount];
    var heights = new int[this._format.TableCount];
    for (var i = 0; i < widths.Length; ++i) {
      var chroma = this._format.ColourSpace == HuffYuvColourSpace.Yuv && i is 1 or 2;
      widths[i] = chroma ? this._width >> this._format.ChromaHorizontalShift : this._width;
      heights[i] = chroma ? this._height >> this._format.ChromaVerticalShift : this._height;
      if (widths[i] <= 0 || heights[i] <= 0)
        throw new InvalidDataException("The FFVHUFF subsampling leaves an empty plane.");
    }

    var planes = new ushort[widths.Length][];
    for (var plane = 0; plane < planes.Length; ++plane) {
      planes[plane] = new ushort[widths[plane] * heights[plane]];
      this._DecodeHighPlane(bits, tables[plane], planes[plane], widths[plane], heights[plane]);
    }
    bits.RefuseIfExhausted("a high-depth FFVHUFF frame");
    return this._ComposeHighDepth(planes, widths, heights);
  }

  private void _DecodeHighPlane(HuffYuvBitReader bits, IHuffYuvDecoderTable table, ushort[] samples, int width, int height) {
    var bps = this._format.BitsPerSample;
    var mask = (1 << bps) - 1;
    var aboveDistance = this._format.Interlaced ? 2 : 1;
    var differences = new int[width];
    var left = 0;
    var leftAbove = 0;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var symbol = table.Read(bits);
        differences[x] = bps == 16 ? (symbol << 2) | bits.Bits(2) : symbol;
      }

      var offset = y * width;
      if (y < aboveDistance || this._format.Predictor != HuffYuvPredictor.Median) {
        for (var x = 0; x < width; ++x) {
          left = (left + differences[x]) & mask;
          var value = left;
          if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= aboveDistance)
            value = (value + samples[(y - aboveDistance) * width + x]) & mask;
          samples[offset + x] = (ushort)value;
        }
        if (y == aboveDistance - 1)
          leftAbove = samples[0];
        continue;
      }

      for (var x = 0; x < width; ++x) {
        var top = samples[(y - aboveDistance) * width + x];
        var plane = (left + top - leftAbove) & mask;
        var predicted = _Median(left, top, plane);
        left = (predicted + differences[x]) & mask;
        leftAbove = top;
        samples[offset + x] = (ushort)left;
      }
    }
  }

  private RawImage _ComposeHighDepth(ushort[][] planes, int[] widths, int[] heights) {
    var bps = this._format.BitsPerSample;
    if (this._format.ColourSpace == HuffYuvColourSpace.Yuv) {
      var format = (this._format.ChromaHorizontalShift, this._format.ChromaVerticalShift, bps) switch {
        (1, 1, 10) => PixelFormat.Yuv420P10, (1, 0, 10) => PixelFormat.Yuv422P10, (0, 1, 10) => PixelFormat.Yuv440P10, (0, 0, 10) => PixelFormat.Yuv444P10,
        (1, 1, 12) => PixelFormat.Yuv420P12, (1, 0, 12) => PixelFormat.Yuv422P12, (0, 1, 12) => PixelFormat.Yuv440P12, (0, 0, 12) => PixelFormat.Yuv444P12,
        (1, 1, 16) => PixelFormat.Yuv420P16, (1, 0, 16) => PixelFormat.Yuv422P16, (0, 1, 16) => PixelFormat.Yuv440P16, (0, 0, 16) => PixelFormat.Yuv444P16,
        _ => throw new NotSupportedException("The decoded FFVHUFF YUV layout has no exact RawImage pixel format."),
      };
      var data = new byte[2 * (planes[0].Length + planes[1].Length + planes[2].Length)];
      var at = 0;
      for (var p = 0; p < 3; ++p)
        foreach (var sample in planes[p]) {
          BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at, 2), sample);
          at += 2;
        }
      return new() { Width = this._width, Height = this._height, Format = format, PixelData = data };
    }

    if (this._format.ColourSpace == HuffYuvColourSpace.Grey) {
      var format = bps == 10 ? PixelFormat.Gray10 : PixelFormat.Gray16;
      var data = new byte[planes[0].Length * 2];
      for (var i = 0; i < planes[0].Length; ++i) {
        var target = data.AsSpan(i * 2, 2);
        if (bps == 10)
          BinaryPrimitives.WriteUInt16LittleEndian(target, planes[0][i]);
        else
          BinaryPrimitives.WriteUInt16BigEndian(target, planes[0][i]);
      }
      return new() { Width = this._width, Height = this._height, Format = format, PixelData = data };
    }

    if (bps == 10) {
      var data = new byte[this._width * this._height * 4];
      for (var i = 0; i < planes[0].Length; ++i) {
        var packed = (uint)planes[2][i] | (uint)planes[0][i] << 10 | (uint)planes[1][i] << 20 | 3u << 30;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4, 4), packed);
      }
      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb30, PixelData = data };
    }

    var channels = this._format.HasAlpha ? 4 : 3;
    var pixels = new byte[this._width * this._height * channels * 2];
    for (var i = 0; i < planes[0].Length; ++i) {
      var at = i * channels * 2;
      BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan(at, 2), planes[2][i]);
      BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan(at + 2, 2), planes[0][i]);
      BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan(at + 4, 2), planes[1][i]);
      if (channels == 4)
        BinaryPrimitives.WriteUInt16BigEndian(pixels.AsSpan(at + 6, 2), planes[3][i]);
    }
    return new() { Width = this._width, Height = this._height, Format = channels == 4 ? PixelFormat.Rgba64 : PixelFormat.Rgb48, PixelData = pixels };
  }

  private static int _Median(int a, int b, int c) {
    if (a > b)
      (a, b) = (b, a);
    return c < a ? a : c > b ? b : c;
  }

  // ============================================================================================
  // Eight-bit planar / interleaved YUV
  // ============================================================================================

  private HuffYuvPlane[] _DecodePlaneAtATime(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables) {
    var planes = this._AllocatePlanes();
    var differences = new byte[this._width];
    var above = this._format.Interlaced ? 2 : 1;
    for (var index = 0; index < planes.Length; ++index) {
      var plane = planes[index];
      var table = tables[index];
      var width = plane.Width;
      byte left = 0, leftAbove = 0;
      for (var y = 0; y < plane.Height; ++y) {
        this._ReadSymbols(bits, table, differences, width);
        if (y < above || this._format.Predictor != HuffYuvPredictor.Median) {
          left = HuffYuvPrediction.AddLeft(plane.Row(y), differences, width, left);
          if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= above)
            HuffYuvPrediction.AddAbove(plane.Row(y), plane.ReadRow(y - above), width);
          if (y == above - 1)
            leftAbove = plane.Samples[0];
        } else
          HuffYuvPrediction.AddMedian(plane.Row(y), plane.ReadRow(y - above), differences, width, ref left, ref leftAbove);
      }
    }
    bits.RefuseIfExhausted("a planar HuffYUV frame");
    return planes;
  }

  private HuffYuvPlane[] _DecodeInterleavedRows(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables) {
    var planes = this._AllocatePlanes();
    var luma = planes[0]; var cb = planes[1]; var cr = planes[2];
    var chromaWidth = cb.Width;
    var dY = new byte[this._width]; var dU = new byte[chromaWidth]; var dV = new byte[chromaWidth];
    var halfHeight = this._format.BitstreamBitsPerPixel == 12;
    if (this._width < 2 || chromaWidth < 1)
      throw new InvalidDataException("HuffYUV interleaved YUV requires pairs of pixels.");

    var leftV = (byte)bits.Bits(8); var y1 = (byte)bits.Bits(8); var leftU = (byte)bits.Bits(8); var y0 = (byte)bits.Bits(8);
    luma.Samples[0] = y0; luma.Samples[1] = y1; cb.Samples[0] = leftU; cr.Samples[0] = leftV;
    var leftY = y1;
    this._Read422(bits, tables, dY, dU, dV, this._width - 2);
    leftY = HuffYuvPrediction.AddLeft(luma.Row(0)[2..], dY, this._width - 2, leftY);
    leftU = HuffYuvPrediction.AddLeft(cb.Row(0)[1..], dU, chromaWidth - 1, leftU);
    leftV = HuffYuvPrediction.AddLeft(cr.Row(0)[1..], dV, chromaWidth - 1, leftV);

    byte leftAboveY = y0, leftAboveU = cb.Samples[0], leftAboveV = cr.Samples[0];
    var y = 1; var cy = 1;
    if (this._format.Predictor == HuffYuvPredictor.Median) {
      var above = this._format.Interlaced ? 2 : 1;
      if (this._format.Interlaced) {
        this._Read422(bits, tables, dY, dU, dV, this._width);
        leftY = HuffYuvPrediction.AddLeft(luma.Row(1), dY, this._width, leftY);
        leftU = HuffYuvPrediction.AddLeft(cb.Row(1), dU, chromaWidth, leftU);
        leftV = HuffYuvPrediction.AddLeft(cr.Row(1), dV, chromaWidth, leftV);
        y = cy = 2;
      }
      const int LUMA_LEFT = 4, CHROMA_LEFT = 2;
      this._Read422(bits, tables, dY, dU, dV, this._width);
      leftY = HuffYuvPrediction.AddLeft(luma.Row(y), dY, LUMA_LEFT, leftY);
      leftU = HuffYuvPrediction.AddLeft(cb.Row(cy), dU, CHROMA_LEFT, leftU);
      leftV = HuffYuvPrediction.AddLeft(cr.Row(cy), dV, CHROMA_LEFT, leftV);
      leftAboveY = luma.ReadRow(y - above)[LUMA_LEFT - 1];
      leftAboveU = cb.ReadRow(cy - above)[CHROMA_LEFT - 1];
      leftAboveV = cr.ReadRow(cy - above)[CHROMA_LEFT - 1];
      HuffYuvPrediction.AddMedian(luma.Row(y)[LUMA_LEFT..], luma.ReadRow(y - above)[LUMA_LEFT..], dY.AsSpan(LUMA_LEFT), this._width - LUMA_LEFT, ref leftY, ref leftAboveY);
      HuffYuvPrediction.AddMedian(cb.Row(cy)[CHROMA_LEFT..], cb.ReadRow(cy - above)[CHROMA_LEFT..], dU.AsSpan(CHROMA_LEFT), chromaWidth - CHROMA_LEFT, ref leftU, ref leftAboveU);
      HuffYuvPrediction.AddMedian(cr.Row(cy)[CHROMA_LEFT..], cr.ReadRow(cy - above)[CHROMA_LEFT..], dV.AsSpan(CHROMA_LEFT), chromaWidth - CHROMA_LEFT, ref leftV, ref leftAboveV);
      ++y; ++cy;
      while (y < this._height) {
        if (halfHeight)
          while (2 * cy > y && y < this._height) {
            this._ReadSymbols(bits, tables[0], dY, this._width);
            HuffYuvPrediction.AddMedian(luma.Row(y), luma.ReadRow(y - above), dY, this._width, ref leftY, ref leftAboveY);
            ++y;
          }
        if (y >= this._height)
          break;
        this._Read422(bits, tables, dY, dU, dV, this._width);
        HuffYuvPrediction.AddMedian(luma.Row(y), luma.ReadRow(y - above), dY, this._width, ref leftY, ref leftAboveY);
        HuffYuvPrediction.AddMedian(cb.Row(cy), cb.ReadRow(cy - above), dU, chromaWidth, ref leftU, ref leftAboveU);
        HuffYuvPrediction.AddMedian(cr.Row(cy), cr.ReadRow(cy - above), dV, chromaWidth, ref leftV, ref leftAboveV);
        ++y; ++cy;
      }
      return planes;
    }

    while (y < this._height) {
      if (halfHeight) {
        this._ReadSymbols(bits, tables[0], dY, this._width);
        this._ApplyRow(luma, y, dY, this._width, ref leftY, ref leftAboveY);
        if (++y >= this._height)
          break;
      }
      this._Read422(bits, tables, dY, dU, dV, this._width);
      this._ApplyRow(luma, y, dY, this._width, ref leftY, ref leftAboveY);
      this._ApplyRow(cb, cy, dU, chromaWidth, ref leftU, ref leftAboveU);
      this._ApplyRow(cr, cy, dV, chromaWidth, ref leftV, ref leftAboveV);
      ++y; ++cy;
    }
    return planes;
  }

  private void _ApplyRow(HuffYuvPlane plane, int y, ReadOnlySpan<byte> differences, int count, ref byte left, ref byte leftAbove) {
    var above = this._format.Interlaced ? 2 : 1;
    if (this._format.Predictor == HuffYuvPredictor.Median && y >= above) {
      HuffYuvPrediction.AddMedian(plane.Row(y), plane.ReadRow(y - above), differences, count, ref left, ref leftAbove);
      return;
    }
    left = HuffYuvPrediction.AddLeft(plane.Row(y), differences, count, left);
    if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= above)
      HuffYuvPrediction.AddAbove(plane.Row(y), plane.ReadRow(y - above), count);
  }

  private void _Read422(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables, Span<byte> y, Span<byte> u, Span<byte> v, int count) {
    var groups = count / 2;
    for (var i = 0; i < groups; ++i) {
      y[2 * i] = (byte)tables[0].Read(bits); u[i] = (byte)tables[1].Read(bits);
      y[2 * i + 1] = (byte)tables[0].Read(bits); v[i] = (byte)tables[2].Read(bits);
    }
  }

  private void _ReadSymbols(HuffYuvBitReader bits, IHuffYuvDecoderTable table, Span<byte> into, int count) {
    for (var i = 0; i < count; ++i)
      into[i] = (byte)table.Read(bits);
  }

  private HuffYuvPlane[] _AllocatePlanes() {
    if (this._format.ColourSpace == HuffYuvColourSpace.Grey)
      return [new(this._width, this._height)];
    if (this._format.ColourSpace == HuffYuvColourSpace.PlanarRgb) {
      var result = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
      for (var i = 0; i < result.Length; ++i)
        result[i] = new(this._width, this._height);
      return result;
    }
    var cw = this._width >> this._format.ChromaHorizontalShift;
    var ch = this._height >> this._format.ChromaVerticalShift;
    var planes = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
    planes[0] = new(this._width, this._height); planes[1] = new(cw, ch); planes[2] = new(cw, ch);
    if (planes.Length == 4) planes[3] = new(this._width, this._height);
    return planes;
  }

  // ============================================================================================
  // Eight-bit packed RGB(A)
  // ============================================================================================

  private RawImage _DecodePackedColour(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables) {
    const int B = 0, G = 1, R = 2, A = 3;
    var hasAlpha = this._format.BitstreamBitsPerPixel == 32;
    var pixels = new byte[this._width * this._height * 4];
    var stride = this._width * 4;
    var differences = new byte[stride];
    var above = this._format.Interlaced ? 2 : 1;
    var bottom = (this._height - 1) * stride;
    if (hasAlpha) pixels[bottom + A] = (byte)bits.Bits(8);
    var red = (byte)bits.Bits(8); var green = (byte)bits.Bits(8); var blue = (byte)bits.Bits(8);
    if (!hasAlpha) bits.Bits(8);
    if (this._format.Decorrelate) { red -= green; blue -= green; }
    pixels[bottom + R] = red; pixels[bottom + G] = green; pixels[bottom + B] = blue;
    var left = new byte[4];
    left[B] = pixels[bottom + B]; left[G] = pixels[bottom + G]; left[R] = pixels[bottom + R]; left[A] = pixels[bottom + A];
    this._ReadColour(bits, tables, differences, this._width - 1, hasAlpha);
    _AddLeftColour(pixels.AsSpan(bottom + 4), differences, this._width - 1, left);
    for (var y = this._height - 2; y >= 0; --y) {
      this._ReadColour(bits, tables, differences, this._width, hasAlpha);
      var row = pixels.AsSpan(y * stride, stride);
      _AddLeftColour(row, differences, this._width, left);
      if (this._format.Predictor == HuffYuvPredictor.Gradient && y <= this._height - 1 - above)
        HuffYuvPrediction.AddAbove(row, pixels.AsSpan((y + above) * stride, stride), stride);
    }
    if (this._format.Decorrelate) _Correlate(pixels);
    if (!hasAlpha) for (var i = A; i < pixels.Length; i += 4) pixels[i] = 255;
    return this._ToImage(pixels, hasAlpha);
  }

  private void _ReadColour(HuffYuvBitReader bits, IHuffYuvDecoderTable[] tables, Span<byte> into, int count, bool alpha) {
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      into[at + 1] = (byte)tables[1].Read(bits); into[at] = (byte)tables[0].Read(bits); into[at + 2] = (byte)tables[2].Read(bits);
      into[at + 3] = alpha ? (byte)tables[2].Read(bits) : (byte)0;
    }
  }

  private static void _AddLeftColour(Span<byte> row, ReadOnlySpan<byte> differences, int count, byte[] left) {
    for (var i = 0; i < count; ++i) for (var c = 0; c < 4; ++c) { var at = i * 4 + c; row[at] = left[c] = (byte)(left[c] + differences[at]); }
  }

  private static void _Correlate(Span<byte> pixels) {
    for (var i = 0; i < pixels.Length; i += 4) { var green = pixels[i + 1]; pixels[i] += green; pixels[i + 2] += green; }
  }

  // ============================================================================================
  // Eight-bit output composition
  // ============================================================================================

  private RawImage _ToImage(byte[] bgra, bool alpha) {
    var channels = alpha ? 4 : 3;
    var data = new byte[this._width * this._height * channels];
    for (var i = 0; i < this._width * this._height; ++i) {
      data[i * channels] = bgra[i * 4 + 2]; data[i * channels + 1] = bgra[i * 4 + 1]; data[i * channels + 2] = bgra[i * 4];
      if (alpha) data[i * 4 + 3] = bgra[i * 4 + 3];
    }
    return new() { Width = this._width, Height = this._height, Format = alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = data };
  }

  private RawImage _Compose(HuffYuvPlane[] planes) => this._format.ColourSpace switch {
    HuffYuvColourSpace.Grey => new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray8, PixelData = planes[0].Samples },
    HuffYuvColourSpace.PlanarRgb => this._FromPlanarRgb(planes),
    _ => this._FromYuv(planes),
  };

  private RawImage _FromPlanarRgb(HuffYuvPlane[] p) {
    var channels = this._format.HasAlpha ? 4 : 3;
    var data = new byte[this._width * this._height * channels];
    for (var i = 0; i < p[0].Samples.Length; ++i) {
      data[i * channels] = p[2].Samples[i]; data[i * channels + 1] = p[0].Samples[i]; data[i * channels + 2] = p[1].Samples[i];
      if (channels == 4) data[i * channels + 3] = p[3].Samples[i];
    }
    return new() { Width = this._width, Height = this._height, Format = channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = data };
  }

  private RawImage _FromYuv(HuffYuvPlane[] p) {
    var channels = this._format.HasAlpha ? 4 : 3;
    var data = new byte[this._width * this._height * channels];
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var c = Math.Min(y >> this._format.ChromaVerticalShift, p[1].Height - 1) * p[1].Width + Math.Min(x >> this._format.ChromaHorizontalShift, p[1].Width - 1);
        var scaled = 298 * (p[0].Samples[y * this._width + x] - 16);
        var blue = p[1].Samples[c] - 128; var red = p[2].Samples[c] - 128; var at = (y * this._width + x) * channels;
        data[at] = _Clamp(scaled + 409 * red + 128); data[at + 1] = _Clamp(scaled - 100 * blue - 208 * red + 128); data[at + 2] = _Clamp(scaled + 516 * blue + 128);
        if (channels == 4) data[at + 3] = p[3].Samples[y * this._width + x];
      }
    return new() { Width = this._width, Height = this._height, Format = channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = data };
  }

  private static byte _Clamp(int scaled) { var value = scaled >> 8; return (byte)Math.Clamp(value, 0, 255); }
}
