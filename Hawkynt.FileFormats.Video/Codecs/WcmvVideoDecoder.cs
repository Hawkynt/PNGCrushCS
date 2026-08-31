using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes WinCAM Motion Video (<c>WCMV</c>) screen captures.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/wcmv.c</c>, copyright (c) 2018 Paul B Mahol, distributed
/// there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// A packet names rectangular updates. Up to five rectangle descriptors are stored directly; larger
/// descriptor sets are themselves zlib-compressed. The concatenated pixel rows then form a second
/// zlib stream. Rectangles are placed into a persistent previous frame and a zero-rectangle packet is
/// therefore a legitimate unchanged frame.
/// </remarks>
public sealed class WcmvVideoDecoder : IVideoCodecDecoder<WcmvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WCMV");

  private readonly int _width;
  private readonly int _height;
  private readonly int _bpp;
  private readonly PixelFormat _format;
  private readonly int _streamIndex;
  private readonly byte[] _canvas;

  private WcmvVideoDecoder(int width, int height, int bpp, PixelFormat format, int streamIndex) {
    this._width = width;
    this._height = height;
    this._bpp = bpp;
    this._format = format;
    this._streamIndex = streamIndex;
    this._canvas = new byte[checked(width * height * bpp)];
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "WinCAM Motion Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static WcmvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"WCMV stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var (bpp, format) = stream.BitsPerPixel switch {
      16 => (2, PixelFormat.Rgb565),
      24 => (3, PixelFormat.Bgr24),
      32 => (4, PixelFormat.Bgra32),
      _ => throw new NotSupportedException(
        $"WCMV stream {stream.Index} states {stream.BitsPerPixel} bits per pixel; defined layouts are 16, 24 and 32."),
    };
    if ((long)stream.Width * stream.Height * bpp > int.MaxValue)
      throw new InvalidDataException($"WCMV stream {stream.Index}'s frame is too large to hold in memory.");

    return new(stream.Width, stream.Height, bpp, format, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 2)
      throw new InvalidDataException($"WCMV stream {this._streamIndex} carries a packet shorter than its block count.");

    var blocks = BinaryPrimitives.ReadUInt16LittleEndian(data);
    if (blocks == 0) {
      frame = this._Frame();
      return true;
    }

    var position = 2;
    byte[] descriptors;
    if (blocks > 5) {
      var sizeWidth = blocks * 8 >= 0xFFFF ? 3 : blocks * 8 >= 0xFF ? 2 : 1;
      var packedDescriptorBytes = _ReadVariableLittleEndian(data, ref position, sizeWidth, this._streamIndex, "descriptor zlib size");
      if (packedDescriptorBytes > data.Length - position)
        throw new InvalidDataException($"WCMV stream {this._streamIndex} truncates its compressed descriptor table.");
      descriptors = _InflateExact(data.Slice(position, packedDescriptorBytes), checked(blocks * 8), this._streamIndex, "descriptor table");
      position += packedDescriptorBytes;
    } else {
      var descriptorBytes = checked(blocks * 8);
      if (descriptorBytes > data.Length - position)
        throw new InvalidDataException($"WCMV stream {this._streamIndex} truncates its descriptor table.");
      descriptors = data.Slice(position, descriptorBytes).ToArray();
      position += descriptorBytes;
    }

    var rectangles = new Rectangle[blocks];
    long rawBytes = 0;
    for (var i = 0; i < blocks; ++i) {
      var at = i * 8;
      var x = BinaryPrimitives.ReadUInt16LittleEndian(descriptors.AsSpan(at));
      var y = BinaryPrimitives.ReadUInt16LittleEndian(descriptors.AsSpan(at + 2));
      var w = BinaryPrimitives.ReadUInt16LittleEndian(descriptors.AsSpan(at + 4));
      var h = BinaryPrimitives.ReadUInt16LittleEndian(descriptors.AsSpan(at + 6));
      if (w == 0 || h == 0 || x + w > this._width || y + h > this._height)
        throw new InvalidDataException(
          $"WCMV stream {this._streamIndex} contains rectangle {i} at ({x},{y}) sized {w}x{h}, outside its "
          + $"{this._width}x{this._height} frame.");
      rawBytes += (long)w * h * this._bpp;
      if (rawBytes > int.MaxValue)
        throw new InvalidDataException($"WCMV stream {this._streamIndex}'s update payload is too large.");
      rectangles[i] = new(x, y, w, h);
    }

    var pixelSizeWidth = rawBytes >= 0xFFFF ? 3 : rawBytes >= 0xFF ? 2 : 1;
    _ = _ReadVariableLittleEndian(data, ref position, pixelSizeWidth, this._streamIndex, "pixel zlib size");
    if (position >= data.Length)
      throw new InvalidDataException($"WCMV stream {this._streamIndex} omits its compressed pixel payload.");

    var pixels = _InflateExact(data[position..], (int)rawBytes, this._streamIndex, "pixel payload");
    var source = 0;
    foreach (var rectangle in rectangles) {
      var rowBytes = rectangle.Width * this._bpp;
      for (var row = 0; row < rectangle.Height; ++row) {
        var outputRow = this._height - rectangle.Y - 1 - row;
        var destination = (outputRow * this._width + rectangle.X) * this._bpp;
        pixels.AsSpan(source, rowBytes).CopyTo(this._canvas.AsSpan(destination, rowBytes));
        source += rowBytes;
      }
    }

    frame = this._Frame();
    return true;
  }

  private RawImage _Frame() => new() {
    Width = this._width,
    Height = this._height,
    Format = this._format,
    PixelData = (byte[])this._canvas.Clone(),
  };

  private static int _ReadVariableLittleEndian(ReadOnlySpan<byte> data, ref int position, int width, int streamIndex, string field) {
    if (width is < 1 or > 3 || data.Length - position < width)
      throw new InvalidDataException($"WCMV stream {streamIndex} truncates its {field}.");
    int value = data[position];
    if (width >= 2)
      value |= data[position + 1] << 8;
    if (width == 3)
      value |= data[position + 2] << 16;
    position += width;
    return value;
  }

  private static byte[] _InflateExact(ReadOnlySpan<byte> compressed, int expected, int streamIndex, string what) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      var result = new byte[expected];
      var offset = 0;
      while (offset < result.Length) {
        var read = zlib.Read(result, offset, result.Length - offset);
        if (read == 0)
          break;
        offset += read;
      }
      if (offset != expected)
        throw new InvalidDataException(
          $"WCMV stream {streamIndex}'s {what} inflates to {offset} byte(s), expected {expected}.");
      return result;
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"WCMV stream {streamIndex} carries invalid zlib data in its {what}.", ex);
    }
  }

  private readonly record struct Rectangle(int X, int Y, int Width, int Height);
}
