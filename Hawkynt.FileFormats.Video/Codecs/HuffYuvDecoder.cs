using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes HuffYUV and FFVHUFF: lossless Huffman coding over spatial prediction residuals.
/// </summary>
/// <remarks>
/// HuffYUV is intra only: every packet is one complete independent picture. The decoder supports
/// the eight-bit second-form interleaved YUV and packed RGB(A) layouts and FFVHUFF's third-form
/// planar grey, YUV and RGB(A) layouts, including interlaced prediction and per-frame Huffman tables.
/// The wire layout and row schedules are matched to FFmpeg's LGPL-2.1-or-later implementation.
/// <para/>
/// Samples deeper than eight bits are rejected explicitly. FFVHUFF's high-depth forms use a larger
/// Huffman alphabet and, at sixteen bits, split low residual bits out of the Huffman symbol; silently
/// narrowing those streams would not be decoding them.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HuffYuvDecoder : IVideoCodecDecoder<HuffYuvDecoder> {

  private static readonly CodecTag _HFYU = CodecTag.FromCharacters("HFYU");
  private static readonly CodecTag _FFVH = CodecTag.FromCharacters("FFVH");
  private static readonly CodecTag[] _Tags = [_HFYU, _FFVH];

  private readonly int _width;
  private readonly int _height;
  private readonly HuffYuvFormat _format;
  private readonly HuffYuvHuffmanTable[]? _tables;

  private HuffYuvDecoder(int width, int height, HuffYuvFormat format, HuffYuvHuffmanTable[]? tables) {
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
      throw new InvalidDataException($"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var extra = _DescriptionOf(stream);
    HuffYuvFormat.RefuseUnstatedInterlacing(extra, stream.Height, stream.Index);
    var format = HuffYuvFormat.Parse(extra, stream.BitsPerPixel, stream.Index);

    _RefusePackedColourMedian(format, stream.Index);
    _RefuseUndersizedInterleavedMedian(format, stream.Width, stream.Height, stream.Index);

    var tables = format.TablesPerFrame
      ? null
      : HuffYuvHuffmanTable.ReadAll(extra, 4, format.TableCount, out _);
    return new(stream.Width, stream.Height, format, tables);
  }

  /// <summary>
  /// Returns the codec bytes whether the container supplied them alone or behind a BITMAPINFOHEADER.
  /// </summary>
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

  private static void _RefusePackedColourMedian(HuffYuvFormat format, int streamIndex) {
    if (format.ColourSpace != HuffYuvColourSpace.PackedBgr || format.Predictor != HuffYuvPredictor.Median)
      return;
    throw new NotSupportedException($"Video stream {streamIndex} states median prediction with colour coded a pixel at a time, which HuffYUV does not combine.");
  }

  private static void _RefuseUndersizedInterleavedMedian(HuffYuvFormat format, int width, int height, int streamIndex) {
    if (format.Version >= 3 || format.ColourSpace != HuffYuvColourSpace.Yuv || format.Predictor != HuffYuvPredictor.Median)
      return;

    var rowsBeforeMedian = format.Interlaced ? 2 : 1;
    var chromaRows = format.BitstreamBitsPerPixel == 12 ? height >> 1 : height;
    if (width >= 4 && height > rowsBeforeMedian && chromaRows > rowsBeforeMedian)
      return;

    throw new NotSupportedException(
      $"Video stream {streamIndex} is {width}x{height}, which HuffYUV cannot code as interleaved rows with median prediction: the reference layout needs {rowsBeforeMedian + 1} picture rows, more than {rowsBeforeMedian} chrominance rows, and four luminance samples before its first median-coded sample.");
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var bits = new HuffYuvBitReader(packet.Data.Span);
    var tables = this._tables;
    if (tables == null) {
      tables = HuffYuvHuffmanTable.ReadAll(bits.Swapped, 0, this._format.TableCount, out var start);
      bits.SeekToByte(start);
    }

    frame = this._format.ColourSpace == HuffYuvColourSpace.PackedBgr
      ? this._DecodePackedColour(bits, tables)
      : this._DecodePlanes(bits, tables);
    return true;
  }

  // ============================================================================================
  // Planar shapes
  // ============================================================================================

  private RawImage _DecodePlanes(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._format.Version >= 3
      ? this._DecodePlaneAtATime(bits, tables)
      : this._DecodeInterleavedRows(bits, tables);
    return this._Compose(planes);
  }

  private HuffYuvPlane[] _DecodePlaneAtATime(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._AllocatePlanes();
    var differences = new byte[this._width];
    var above = this._format.Interlaced ? 2 : 1;

    for (var index = 0; index < planes.Length; ++index) {
      var plane = planes[index];
      var table = tables[index];
      var width = plane.Width;
      byte left = 0;
      byte leftAbove = 0;

      for (var y = 0; y < plane.Height; ++y) {
        this._ReadSymbols(bits, table, differences, width);
        if (y < above || this._format.Predictor != HuffYuvPredictor.Median) {
          left = HuffYuvPrediction.AddLeft(plane.Row(y), differences, width, left);
          if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= above)
            HuffYuvPrediction.AddAbove(plane.Row(y), plane.ReadRow(y - above), width);
          if (y == above - 1)
            leftAbove = plane.Samples[0];
          continue;
        }

        HuffYuvPrediction.AddMedian(plane.Row(y), plane.ReadRow(y - above), differences, width, ref left, ref leftAbove);
      }
    }

    bits.RefuseIfExhausted("a planar HuffYUV frame");
    return planes;
  }

  /// <summary>
  /// Decodes the second-form YUV layout. In 4:2:0 some rows carry only luma. With median prediction
  /// FFmpeg advances luma until <c>2 * chromaRow &lt;= lumaRow</c> before the next full Y/U/V row;
  /// that detail matters for interlaced 4:2:0, where three luma-only rows follow the first median
  /// row rather than the single row a progressive-only implementation suggests.
  /// </summary>
  private HuffYuvPlane[] _DecodeInterleavedRows(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._AllocatePlanes();
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var chromaWidth = cb.Width;
    var lumaDifferences = new byte[this._width];
    var cbDifferences = new byte[chromaWidth];
    var crDifferences = new byte[chromaWidth];
    var halfHeight = this._format.BitstreamBitsPerPixel == 12;

    if (this._width < 2 || chromaWidth < 1)
      throw new InvalidDataException($"A {this._width}-pixel wide picture cannot be coded as 4:2:2 groups, which are two pixels each.");

    var leftV = (byte)bits.Bits(8);
    var lumaOne = (byte)bits.Bits(8);
    var leftU = (byte)bits.Bits(8);
    var lumaZero = (byte)bits.Bits(8);
    luma.Samples[0] = lumaZero;
    luma.Samples[1] = lumaOne;
    cb.Samples[0] = leftU;
    cr.Samples[0] = leftV;
    var leftY = lumaOne;

    this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width - 2);
    leftY = HuffYuvPrediction.AddLeft(luma.Row(0)[2..], lumaDifferences, this._width - 2, leftY);
    leftU = HuffYuvPrediction.AddLeft(cb.Row(0)[1..], cbDifferences, chromaWidth - 1, leftU);
    leftV = HuffYuvPrediction.AddLeft(cr.Row(0)[1..], crDifferences, chromaWidth - 1, leftV);

    var leftAboveY = lumaZero;
    var leftAboveU = cb.Samples[0];
    var leftAboveV = cr.Samples[0];
    var y = 1;
    var chromaRow = 1;

    if (this._format.Predictor == HuffYuvPredictor.Median) {
      var above = this._format.Interlaced ? 2 : 1;
      if (this._format.Interlaced) {
        this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
        leftY = HuffYuvPrediction.AddLeft(luma.Row(1), lumaDifferences, this._width, leftY);
        leftU = HuffYuvPrediction.AddLeft(cb.Row(1), cbDifferences, chromaWidth, leftU);
        leftV = HuffYuvPrediction.AddLeft(cr.Row(1), crDifferences, chromaWidth, leftV);
        y = 2;
        chromaRow = 2;
      }

      const int _LUMA_LEFT = 4;
      const int _CHROMA_LEFT = 2;
      this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
      leftY = HuffYuvPrediction.AddLeft(luma.Row(y), lumaDifferences, _LUMA_LEFT, leftY);
      leftU = HuffYuvPrediction.AddLeft(cb.Row(chromaRow), cbDifferences, _CHROMA_LEFT, leftU);
      leftV = HuffYuvPrediction.AddLeft(cr.Row(chromaRow), crDifferences, _CHROMA_LEFT, leftV);
      leftAboveY = luma.ReadRow(y - above)[_LUMA_LEFT - 1];
      leftAboveU = cb.ReadRow(chromaRow - above)[_CHROMA_LEFT - 1];
      leftAboveV = cr.ReadRow(chromaRow - above)[_CHROMA_LEFT - 1];
      HuffYuvPrediction.AddMedian(luma.Row(y)[_LUMA_LEFT..], luma.ReadRow(y - above)[_LUMA_LEFT..], lumaDifferences.AsSpan(_LUMA_LEFT), this._width - _LUMA_LEFT, ref leftY, ref leftAboveY);
      HuffYuvPrediction.AddMedian(cb.Row(chromaRow)[_CHROMA_LEFT..], cb.ReadRow(chromaRow - above)[_CHROMA_LEFT..], cbDifferences.AsSpan(_CHROMA_LEFT), chromaWidth - _CHROMA_LEFT, ref leftU, ref leftAboveU);
      HuffYuvPrediction.AddMedian(cr.Row(chromaRow)[_CHROMA_LEFT..], cr.ReadRow(chromaRow - above)[_CHROMA_LEFT..], crDifferences.AsSpan(_CHROMA_LEFT), chromaWidth - _CHROMA_LEFT, ref leftV, ref leftAboveV);
      ++y;
      ++chromaRow;

      while (y < this._height) {
        if (halfHeight)
          while (2 * chromaRow > y && y < this._height) {
            this._ReadSymbols(bits, tables[0], lumaDifferences, this._width);
            HuffYuvPrediction.AddMedian(luma.Row(y), luma.ReadRow(y - above), lumaDifferences, this._width, ref leftY, ref leftAboveY);
            ++y;
          }

        if (y >= this._height)
          break;
        if (chromaRow >= cb.Height)
          throw new InvalidDataException("The interleaved 4:2:0 frame addresses a chrominance row beyond the picture.");

        this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
        HuffYuvPrediction.AddMedian(luma.Row(y), luma.ReadRow(y - above), lumaDifferences, this._width, ref leftY, ref leftAboveY);
        HuffYuvPrediction.AddMedian(cb.Row(chromaRow), cb.ReadRow(chromaRow - above), cbDifferences, chromaWidth, ref leftU, ref leftAboveU);
        HuffYuvPrediction.AddMedian(cr.Row(chromaRow), cr.ReadRow(chromaRow - above), crDifferences, chromaWidth, ref leftV, ref leftAboveV);
        ++y;
        ++chromaRow;
      }

      bits.RefuseIfExhausted("an interleaved median-predicted frame");
      return planes;
    }

    while (y < this._height) {
      if (halfHeight) {
        this._ReadSymbols(bits, tables[0], lumaDifferences, this._width);
        this._ApplyRow(luma, y, lumaDifferences, this._width, ref leftY, ref leftAboveY);
        ++y;
        if (y >= this._height)
          break;
      }

      if (chromaRow >= cb.Height)
        throw new InvalidDataException("The interleaved YUV frame addresses a chrominance row beyond the picture.");
      this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
      this._ApplyRow(luma, y, lumaDifferences, this._width, ref leftY, ref leftAboveY);
      this._ApplyRow(cb, chromaRow, cbDifferences, chromaWidth, ref leftU, ref leftAboveU);
      this._ApplyRow(cr, chromaRow, crDifferences, chromaWidth, ref leftV, ref leftAboveV);
      ++y;
      ++chromaRow;
    }

    bits.RefuseIfExhausted("a luminance and chrominance frame");
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

  private void _Read422(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables, Span<byte> luma, Span<byte> cb, Span<byte> cr, int count) {
    var groups = count / 2;
    for (var i = 0; i < groups; ++i) {
      luma[2 * i] = (byte)tables[0].Read(bits);
      cb[i] = (byte)tables[1].Read(bits);
      luma[2 * i + 1] = (byte)tables[0].Read(bits);
      cr[i] = (byte)tables[2].Read(bits);
    }
  }

  private void _ReadSymbols(HuffYuvBitReader bits, HuffYuvHuffmanTable table, Span<byte> into, int count) {
    for (var i = 0; i < count; ++i)
      into[i] = (byte)table.Read(bits);
  }

  private HuffYuvPlane[] _AllocatePlanes() {
    switch (this._format.ColourSpace) {
      case HuffYuvColourSpace.Grey:
        return [new(this._width, this._height)];
      case HuffYuvColourSpace.PlanarRgb: {
        var rgb = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
        for (var i = 0; i < rgb.Length; ++i)
          rgb[i] = new(this._width, this._height);
        return rgb;
      }
      default: {
        var chromaWidth = this._width >> this._format.ChromaHorizontalShift;
        var chromaHeight = this._height >> this._format.ChromaVerticalShift;
        if (chromaWidth <= 0 || chromaHeight <= 0)
          throw new InvalidDataException("The stated HuffYUV chroma subsampling leaves an empty chrominance plane.");
        var yuv = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
        yuv[0] = new(this._width, this._height);
        yuv[1] = new(chromaWidth, chromaHeight);
        yuv[2] = new(chromaWidth, chromaHeight);
        if (yuv.Length == 4)
          yuv[3] = new(this._width, this._height);
        return yuv;
      }
    }
  }

  // ============================================================================================
  // Packed colour
  // ============================================================================================

  private RawImage _DecodePackedColour(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    const int _B = 0;
    const int _G = 1;
    const int _R = 2;
    const int _A = 3;

    var hasAlpha = this._format.BitstreamBitsPerPixel == 32;
    var pixels = new byte[this._width * this._height * 4];
    var stride = this._width * 4;
    var differences = new byte[this._width * 4];
    var above = this._format.Interlaced ? 2 : 1;
    var bottom = (this._height - 1) * stride;

    if (hasAlpha)
      pixels[bottom + _A] = (byte)bits.Bits(8);
    var red = (byte)bits.Bits(8);
    var green = (byte)bits.Bits(8);
    var blue = (byte)bits.Bits(8);
    if (!hasAlpha)
      bits.Bits(8);

    if (this._format.Decorrelate) {
      red -= green;
      blue -= green;
    }
    pixels[bottom + _R] = red;
    pixels[bottom + _G] = green;
    pixels[bottom + _B] = blue;

    var left = new byte[4];
    left[_B] = pixels[bottom + _B];
    left[_G] = pixels[bottom + _G];
    left[_R] = pixels[bottom + _R];
    left[_A] = pixels[bottom + _A];

    this._ReadColour(bits, tables, differences, this._width - 1, hasAlpha);
    _AddLeftColour(pixels.AsSpan(bottom + 4), differences, this._width - 1, left);

    for (var y = this._height - 2; y >= 0; --y) {
      this._ReadColour(bits, tables, differences, this._width, hasAlpha);
      var row = pixels.AsSpan(y * stride, stride);
      _AddLeftColour(row, differences, this._width, left);
      if (this._format.Predictor == HuffYuvPredictor.Gradient && y <= this._height - 1 - above)
        HuffYuvPrediction.AddAbove(row, pixels.AsSpan((y + above) * stride, stride), stride);
    }

    if (this._format.Decorrelate)
      _Correlate(pixels);
    if (!hasAlpha)
      for (var i = _A; i < pixels.Length; i += 4)
        pixels[i] = 255;

    bits.RefuseIfExhausted("a packed-colour frame");
    return this._ToImage(pixels, hasAlpha);
  }

  private void _ReadColour(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables, Span<byte> into, int count, bool hasAlpha) {
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      into[at + 1] = (byte)tables[1].Read(bits);
      into[at] = (byte)tables[0].Read(bits);
      into[at + 2] = (byte)tables[2].Read(bits);
      into[at + 3] = hasAlpha ? (byte)tables[2].Read(bits) : (byte)0;
    }
  }

  private static void _AddLeftColour(Span<byte> row, ReadOnlySpan<byte> differences, int count, byte[] left) {
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      row[at] = left[0] = (byte)(left[0] + differences[at]);
      row[at + 1] = left[1] = (byte)(left[1] + differences[at + 1]);
      row[at + 2] = left[2] = (byte)(left[2] + differences[at + 2]);
      row[at + 3] = left[3] = (byte)(left[3] + differences[at + 3]);
    }
  }

  private static void _Correlate(Span<byte> pixels) {
    for (var i = 0; i < pixels.Length; i += 4) {
      var green = pixels[i + 1];
      pixels[i] += green;
      pixels[i + 2] += green;
    }
  }

  // ============================================================================================
  // Output composition
  // ============================================================================================

  private RawImage _ToImage(byte[] bgra, bool hasAlpha) {
    if (hasAlpha) {
      var rgba = new byte[this._width * this._height * 4];
      for (var i = 0; i < rgba.Length; i += 4) {
        rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = bgra[i + 3];
      }
      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    }

    var rgb = new byte[this._width * this._height * 3];
    for (var i = 0; i < this._width * this._height; ++i) {
      rgb[i * 3] = bgra[i * 4 + 2]; rgb[i * 3 + 1] = bgra[i * 4 + 1]; rgb[i * 3 + 2] = bgra[i * 4];
    }
    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private RawImage _Compose(HuffYuvPlane[] planes) => this._format.ColourSpace switch {
    HuffYuvColourSpace.Grey => new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray8, PixelData = planes[0].Samples },
    HuffYuvColourSpace.PlanarRgb => this._FromPlanarRgb(planes),
    _ => this._FromYuv(planes),
  };

  private RawImage _FromPlanarRgb(HuffYuvPlane[] planes) {
    var count = this._width * this._height;
    var green = planes[0].Samples;
    var blue = planes[1].Samples;
    var red = planes[2].Samples;
    if (this._format.HasAlpha) {
      var rgba = new byte[count * 4];
      var alpha = planes[3].Samples;
      for (var i = 0; i < count; ++i) {
        rgba[i * 4] = red[i]; rgba[i * 4 + 1] = green[i]; rgba[i * 4 + 2] = blue[i]; rgba[i * 4 + 3] = alpha[i];
      }
      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    }

    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = red[i]; rgb[i * 3 + 1] = green[i]; rgb[i * 3 + 2] = blue[i];
    }
    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private RawImage _FromYuv(HuffYuvPlane[] planes) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var alpha = this._format.HasAlpha ? planes[3] : null;
    var channels = alpha == null ? 3 : 4;
    var pixels = new byte[this._width * this._height * channels];

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> this._format.ChromaVerticalShift, cb.Height - 1);
      var lumaRow = y * luma.Width;
      var target = y * this._width * channels;
      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> this._format.ChromaHorizontalShift, cb.Width - 1);
        var at = chromaRow * cb.Width + chromaColumn;
        var scaledLuma = 298 * (luma.Samples[lumaRow + x] - 16);
        var blueDifference = cb.Samples[at] - 128;
        var redDifference = cr.Samples[at] - 128;
        pixels[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        if (alpha != null)
          pixels[target + 3] = alpha.Samples[lumaRow + x];
        target += channels;
      }
    }

    return new() { Width = this._width, Height = this._height, Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Rgba32, PixelData = pixels };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
