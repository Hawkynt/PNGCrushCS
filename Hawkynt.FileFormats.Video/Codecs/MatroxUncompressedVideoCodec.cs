using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>The common stream layout behind Matrox Uncompressed SD (<c>M101</c>) and HD (<c>M102</c>).</summary>
/// <remarks>
/// FFmpeg maps both RIFF tags to its <c>m101</c> decoder. That decoder is LGPL-2.1-or-later and is the
/// source of the 24-byte trailer interpretation and ten-bit unpacking used here: byte 8 is the sample
/// depth, byte 12 carries scan/field flags and the little-endian word at byte 20 is the stored row
/// stride. The encoder below is the exact inverse of that packing rather than a second format variant.
/// </remarks>
internal readonly record struct MatroxUncompressedStreamFormat(
  int Bits,
  int Stride,
  byte FieldFlags,
  byte[] TrailerTemplate
) {

  private const int _TRAILER_SIZE = 24;

  public static MatroxUncompressedStreamFormat ForDecoder(MediaStreamInfo stream, string codecName) {
    ArgumentNullException.ThrowIfNull(stream);
    _ValidateGeometry(stream, codecName);

    var trailer = _Trailer(stream.CodecPrivateData.Span, stream.Index, codecName);
    return _FromTrailer(stream, trailer, codecName);
  }

  public static MatroxUncompressedStreamFormat ForEncoder(MediaStreamInfo stream, string codecName) {
    ArgumentNullException.ThrowIfNull(stream);
    _ValidateGeometry(stream, codecName);

    if (!stream.CodecPrivateData.IsEmpty) {
      var trailer = _Trailer(stream.CodecPrivateData.Span, stream.Index, codecName);
      return _FromTrailer(stream, trailer, codecName);
    }

    var bits = stream.BitsPerPixel switch {
      0 or 16 => 8,
      20 => 10,
      _ => throw new NotSupportedException(
        $"{codecName} stream {stream.Index} asks for {stream.BitsPerPixel} stored bits per pixel; "
        + "use 16 for 8-bit 4:2:2 or 20 for 10-bit 4:2:2."),
    };
    var stride = _MinimumStride(stream.Width, bits, stream.Index, codecName);
    return new(bits, stride, 3, new byte[_TRAILER_SIZE]);
  }

  public int StoredRow(int outputRow, int height) {
    var flags = this.FieldFlags & 3;
    if (flags == 3)
      return outputRow;

    var topFieldFirst = (flags & 1) != 0;
    return ((outputRow & 1) ^ (topFieldFirst ? 1 : 0)) != 0
      ? outputRow / 2
      : outputRow / 2 + height / 2;
  }

  public byte[] BuildBitmapInfoHeader(MediaStreamInfo stream, CodecTag codec) {
    var result = new byte[BitmapInfoHeader.StructSize + _TRAILER_SIZE];
    BinaryPrimitives.WriteUInt32LittleEndian(result, BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), stream.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), stream.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 1);

    // Matrox advertises both layouts through a 16-bpp VFW BITMAPINFOHEADER; the actual 8/10-bit
    // choice lives in its private trailer and is what the decoder consumes.
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), codec.Value);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20), checked((uint)(this.Stride * (long)stream.Height)));

    this.TrailerTemplate.AsSpan(0, Math.Min(_TRAILER_SIZE, this.TrailerTemplate.Length))
      .CopyTo(result.AsSpan(BitmapInfoHeader.StructSize, _TRAILER_SIZE));
    var trailer = result.AsSpan(BitmapInfoHeader.StructSize, _TRAILER_SIZE);
    trailer[8] = checked((byte)this.Bits);
    trailer[12] = this.FieldFlags;
    BinaryPrimitives.WriteUInt32LittleEndian(trailer[20..], checked((uint)this.Stride));
    return result;
  }

  private static MatroxUncompressedStreamFormat _FromTrailer(
    MediaStreamInfo stream,
    ReadOnlySpan<byte> trailer,
    string codecName
  ) {
    var bits = trailer[8];
    if (bits is not (8 or 10))
      throw new NotSupportedException(
        $"{codecName} stream {stream.Index} states {bits} bits per sample; the defined layouts are 8 and 10.");

    var strideValue = BinaryPrimitives.ReadUInt32LittleEndian(trailer[20..]);
    if (strideValue > int.MaxValue)
      throw new InvalidDataException($"{codecName} stream {stream.Index} states a row stride too large to address.");

    var stride = (int)strideValue;
    var minimumStride = _MinimumStride(stream.Width, bits, stream.Index, codecName);
    if (stride < minimumStride)
      throw new InvalidDataException(
        $"{codecName} stream {stream.Index} states a {stride}-byte row stride, below the {minimumStride} byte minimum "
        + $"for {stream.Width} pixels at {bits} bits.");

    if ((long)stride * stream.Height > int.MaxValue)
      throw new InvalidDataException($"{codecName} stream {stream.Index}'s coded frame is too large to address.");

    return new(bits, stride, trailer[12], trailer.ToArray());
  }

  private static ReadOnlySpan<byte> _Trailer(ReadOnlySpan<byte> privateData, int streamIndex, string codecName) {
    if (privateData.Length >= sizeof(uint)
      && BinaryPrimitives.ReadUInt32LittleEndian(privateData) >= BitmapInfoHeader.StructSize) {
      if (privateData.Length < BitmapInfoHeader.StructSize + _TRAILER_SIZE)
        throw new InvalidDataException(
          $"{codecName} stream {streamIndex} carries {Math.Max(0, privateData.Length - BitmapInfoHeader.StructSize)} "
          + $"codec-private byte(s) after its BITMAPINFOHEADER, where the Matrox trailer needs {_TRAILER_SIZE}.");

      return privateData.Slice(BitmapInfoHeader.StructSize, _TRAILER_SIZE);
    }

    if (privateData.Length < _TRAILER_SIZE)
      throw new InvalidDataException(
        $"{codecName} stream {streamIndex} carries {privateData.Length} codec-private byte(s), "
        + $"where the Matrox trailer needs {_TRAILER_SIZE}.");

    return privateData[.._TRAILER_SIZE];
  }

  private static int _MinimumStride(int width, int bits, int streamIndex, string codecName) {
    var value = bits == 8
      ? (long)width * 2
      : ((long)width + 15) / 16 * 40;
    if (value > int.MaxValue)
      throw new InvalidDataException($"{codecName} stream {streamIndex}'s minimum row stride is too large to address.");

    return (int)value;
  }

  private static void _ValidateGeometry(MediaStreamInfo stream, string codecName) {
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"{codecName} stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no samples.");
    if ((stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"{codecName} stream {stream.Index} states an odd width of {stream.Width}; 4:2:2 chroma is stored for pixel pairs.");
  }
}

/// <summary>Shared decoder body for the two Matrox uncompressed VFW tags.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/m101.c</c>, copyright (c) 2016 Michael Niedermayer,
/// distributed there under LGPL-2.1-or-later. This C# adaptation is distributed with PNGCrushCS
/// under LGPL-3.0-or-later.
/// </remarks>
internal sealed class MatroxUncompressedVideoDecoderCore {

  private readonly int _width;
  private readonly int _height;
  private readonly MatroxUncompressedStreamFormat _format;
  private readonly int _streamIndex;
  private readonly string _codecName;

  private MatroxUncompressedVideoDecoderCore(
    int width,
    int height,
    MatroxUncompressedStreamFormat format,
    int streamIndex,
    string codecName
  ) {
    this._width = width;
    this._height = height;
    this._format = format;
    this._streamIndex = streamIndex;
    this._codecName = codecName;
  }

  public static MatroxUncompressedVideoDecoderCore Create(MediaStreamInfo stream, string codecName)
    => new(stream.Width, stream.Height, MatroxUncompressedStreamFormat.ForDecoder(stream, codecName), stream.Index, codecName);

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    var required = checked(this._format.Stride * this._height);
    if (source.Length < required)
      throw new InvalidDataException(
        $"{this._codecName} stream {this._streamIndex} carries a {source.Length}-byte packet where its declared stride "
        + $"and height need at least {required} bytes.");

    var rgb = new byte[checked(this._width * this._height * 3)];
    for (var y = 0; y < this._height; ++y) {
      var sourceRow = this._format.StoredRow(y, this._height);
      var row = source.Slice(sourceRow * this._format.Stride, this._format.Stride);
      if (this._format.Bits == 8)
        this._Decode8BitRow(row, rgb.AsSpan(y * this._width * 3, this._width * 3));
      else
        this._Decode10BitRow(row, rgb.AsSpan(y * this._width * 3, this._width * 3));
    }

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
    return true;
  }

  private void _Decode8BitRow(ReadOnlySpan<byte> row, Span<byte> rgb) {
    var output = 0;
    for (var x = 0; x < this._width; x += 2) {
      var at = x * 2;
      var y0 = row[at];
      var u = row[at + 1];
      var y1 = row[at + 2];
      var v = row[at + 3];
      _WriteRgb8(rgb, ref output, y0, u, v);
      _WriteRgb8(rgb, ref output, y1, u, v);
    }
  }

  private void _Decode10BitRow(ReadOnlySpan<byte> row, Span<byte> rgb) {
    var output = 0;
    for (var block = 0; block * 16 < this._width; ++block) {
      var blockData = row.Slice(block * 40, 40);
      var count = Math.Min(16, this._width - block * 16);
      for (var x = 0; x < count; x += 2) {
        var packed = blockData[32 + (x >> 1)];
        var y0 = (blockData[2 * x] << 2) | (packed & 3);
        var u = (blockData[2 * x + 1] << 2) | ((packed >> 2) & 3);
        var y1 = (blockData[2 * (x + 1)] << 2) | ((packed >> 4) & 3);
        var v = (blockData[2 * x + 3] << 2) | ((packed >> 6) & 3);
        _WriteRgb10(rgb, ref output, y0, u, v);
        _WriteRgb10(rgb, ref output, y1, u, v);
      }
    }
  }

  private static void _WriteRgb8(Span<byte> destination, ref int at, int y, int u, int v) {
    var c = y - 16;
    var d = u - 128;
    var e = v - 128;
    destination[at++] = _Clamp((298 * c + 409 * e + 128) >> 8);
    destination[at++] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
    destination[at++] = _Clamp((298 * c + 516 * d + 128) >> 8);
  }

  private static void _WriteRgb10(Span<byte> destination, ref int at, int y, int u, int v) {
    var c = y - 64;
    var d = u - 512;
    var e = v - 512;
    destination[at++] = _Clamp((298 * c + 409 * e + 512) >> 10);
    destination[at++] = _Clamp((298 * c - 100 * d - 208 * e + 512) >> 10);
    destination[at++] = _Clamp((298 * c + 516 * d + 512) >> 10);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}

/// <summary>Shared encoder body for Matrox Uncompressed SD and HD.</summary>
internal sealed class MatroxUncompressedVideoEncoderCore {

  private readonly MediaStreamInfo _stream;
  private readonly MatroxUncompressedStreamFormat _format;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly string _codecName;

  private MatroxUncompressedVideoEncoderCore(
    MediaStreamInfo stream,
    CodecTag codec,
    string codecName,
    MatroxUncompressedStreamFormat format
  ) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = stream.Width / 2;
    this._format = format;
    this._codecName = codecName;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = codec,
      Handler = codec,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 16,
      CodecPrivateData = format.BuildBitmapInfoHeader(stream, codec),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static MatroxUncompressedVideoEncoderCore Create(MediaStreamInfo stream, CodecTag codec, string codecName) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"{codecName} can only encode a video stream.");

    return new(stream, codec, codecName, MatroxUncompressedStreamFormat.ForEncoder(stream, codecName));
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"{this._codecName} geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var targetFormat = this._format.Bits == 8 ? PixelFormat.Yuv422P8 : PixelFormat.Yuv422P10;
    var source = frame.Format == targetFormat
      ? frame
      : FastRawImageConverter.Convert(frame, targetFormat, RawImageColorInfo.Bt601Limited);
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException($"Conversion to {targetFormat} produced too few bytes for {this._width}x{this._height}.");

    var data = new byte[checked(this._format.Stride * this._height)];
    var luma = source.GetPlaneData(0);
    var cb = source.GetPlaneData(1);
    var cr = source.GetPlaneData(2);
    for (var y = 0; y < this._height; ++y) {
      var storedRow = this._format.StoredRow(y, this._height);
      var row = data.AsSpan(storedRow * this._format.Stride, this._format.Stride);
      if (this._format.Bits == 8)
        this._Encode8BitRow(y, luma, cb, cr, row);
      else
        this._Encode10BitRow(y, luma, cb, cr, row);
    }

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  private void _Encode8BitRow(
    int y,
    ReadOnlySpan<byte> luma,
    ReadOnlySpan<byte> cb,
    ReadOnlySpan<byte> cr,
    Span<byte> row
  ) {
    var lumaBase = y * this._width;
    var chromaBase = y * this._chromaWidth;
    for (var x = 0; x < this._width; x += 2) {
      var chroma = chromaBase + (x >> 1);
      var at = x * 2;
      row[at] = luma[lumaBase + x];
      row[at + 1] = cb[chroma];
      row[at + 2] = luma[lumaBase + x + 1];
      row[at + 3] = cr[chroma];
    }
  }

  private void _Encode10BitRow(
    int y,
    ReadOnlySpan<byte> luma,
    ReadOnlySpan<byte> cb,
    ReadOnlySpan<byte> cr,
    Span<byte> row
  ) {
    var lumaBase = y * this._width;
    var chromaBase = y * this._chromaWidth;
    for (var block = 0; block * 16 < this._width; ++block) {
      var firstX = block * 16;
      var count = Math.Min(16, this._width - firstX);
      var blockData = row.Slice(block * 40, 40);

      for (var x = 0; x < count; x += 2) {
        var absoluteX = firstX + x;
        var chroma = chromaBase + (absoluteX >> 1);
        var y0 = _TenBitSample(luma, lumaBase + absoluteX, "luma");
        var y1 = _TenBitSample(luma, lumaBase + absoluteX + 1, "luma");
        var u = _TenBitSample(cb, chroma, "blue difference");
        var v = _TenBitSample(cr, chroma, "red difference");

        blockData[2 * x] = (byte)(y0 >> 2);
        blockData[2 * x + 1] = (byte)(u >> 2);
        blockData[2 * x + 2] = (byte)(y1 >> 2);
        blockData[2 * x + 3] = (byte)(v >> 2);
        blockData[32 + (x >> 1)] = (byte)(
          (y0 & 3)
          | ((u & 3) << 2)
          | ((y1 & 3) << 4)
          | ((v & 3) << 6));
      }
    }
  }

  private static int _TenBitSample(ReadOnlySpan<byte> plane, int index, string component) {
    var value = BinaryPrimitives.ReadUInt16LittleEndian(plane[(index * 2)..]);
    if (value > 1023)
      throw new InvalidDataException($"A Matrox uncompressed {component} sample is ten bits wide; {value} does not fit.");

    return value;
  }
}
