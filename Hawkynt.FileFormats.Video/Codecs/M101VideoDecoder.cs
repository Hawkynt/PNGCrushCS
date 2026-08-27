using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Matrox Uncompressed SD (<c>M101</c>) in its eight- and ten-bit 4:2:2 layouts.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/m101.c</c>, copyright (c) 2016 Michael Niedermayer,
/// distributed there under LGPL-2.1-or-later. This C# adaptation is distributed with PNGCrushCS
/// under LGPL-3.0-or-later.
/// <para/>
/// AVI carries a 24-byte Matrox trailer after the ordinary <c>BITMAPINFOHEADER</c>. Byte 8 of that
/// trailer selects eight- or ten-bit samples, byte 12 describes progressive/field order, and the
/// little-endian word at byte 20 is the stored row stride. Eight-bit rows are YUYV. Ten-bit rows are
/// grouped sixteen luma samples at a time: the high eight bits of Y/U/V occupy the first 32 bytes and
/// eight packing bytes carry the low two bits.
/// </remarks>
public sealed class M101VideoDecoder : IVideoCodecDecoder<M101VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("M101");

  private readonly int _width;
  private readonly int _height;
  private readonly int _bits;
  private readonly int _stride;
  private readonly byte _fieldFlags;
  private readonly int _streamIndex;

  private M101VideoDecoder(int width, int height, int bits, int stride, byte fieldFlags, int streamIndex) {
    this._width = width;
    this._height = height;
    this._bits = bits;
    this._stride = stride;
    this._fieldFlags = fieldFlags;
    this._streamIndex = streamIndex;
  }

  public static string CodecName => "Matrox Uncompressed SD";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static M101VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"M101 stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no samples.");
    if ((stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"M101 stream {stream.Index} states an odd width of {stream.Width}; 4:2:2 chroma is stored for pixel pairs.");

    var format = stream.CodecPrivateData.Span;
    var extraOffset = BitmapInfoHeader.StructSize;
    if (format.Length < extraOffset + 24)
      throw new InvalidDataException(
        $"M101 stream {stream.Index} carries {Math.Max(0, format.Length - extraOffset)} codec-private byte(s), "
        + "where the Matrox trailer needs 24.");

    var extra = format[extraOffset..];
    var bits = extra[8];
    if (bits is not (8 or 10))
      throw new NotSupportedException(
        $"M101 stream {stream.Index} states {bits} bits per sample; the defined layouts are 8 and 10.");

    var strideValue = BinaryPrimitives.ReadUInt32LittleEndian(extra[20..]);
    if (strideValue > int.MaxValue)
      throw new InvalidDataException($"M101 stream {stream.Index} states a row stride too large to address.");
    var stride = (int)strideValue;
    var minimumStride = bits == 8 ? checked(stream.Width * 2) : checked((stream.Width + 15) / 16 * 40);
    if (stride < minimumStride)
      throw new InvalidDataException(
        $"M101 stream {stream.Index} states a {stride}-byte row stride, below the {minimumStride} byte minimum "
        + $"for {stream.Width} pixels at {bits} bits.");

    if ((long)stride * stream.Height > int.MaxValue)
      throw new InvalidDataException($"M101 stream {stream.Index}'s coded frame is too large to address.");

    return new(stream.Width, stream.Height, bits, stride, extra[12], stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var source = packet.Data.Span;
    var required = checked(this._stride * this._height);
    if (source.Length < required)
      throw new InvalidDataException(
        $"M101 stream {this._streamIndex} carries a {source.Length}-byte packet where its declared stride and height "
        + $"need at least {required} bytes.");

    var rgb = new byte[checked(this._width * this._height * 3)];
    for (var y = 0; y < this._height; ++y) {
      var sourceRow = this._SourceRow(y);
      var row = source.Slice(sourceRow * this._stride, this._stride);
      if (this._bits == 8)
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

  private int _SourceRow(int outputRow) {
    var flags = this._fieldFlags & 3;
    if (flags == 3)
      return outputRow;

    var topFieldFirst = (flags & 1) != 0;
    return ((outputRow & 1) ^ (topFieldFirst ? 1 : 0)) != 0
      ? outputRow / 2
      : outputRow / 2 + this._height / 2;
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
        var y1 = x + 1 < count
          ? (blockData[2 * (x + 1)] << 2) | ((packed >> 4) & 3)
          : y0;
        var v = (blockData[2 * x + 3] << 2) | ((packed >> 6) & 3);
        _WriteRgb10(rgb, ref output, y0, u, v);
        if (x + 1 < count)
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
    // Studio-range 10-bit is the 8-bit range scaled by four: Y 64..940, C centered at 512.
    var c = y - 64;
    var d = u - 512;
    var e = v - 512;
    destination[at++] = _Clamp((298 * c + 409 * e + 512) >> 10);
    destination[at++] = _Clamp((298 * c - 100 * d - 208 * e + 512) >> 10);
    destination[at++] = _Clamp((298 * c + 516 * d + 512) >> 10);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
