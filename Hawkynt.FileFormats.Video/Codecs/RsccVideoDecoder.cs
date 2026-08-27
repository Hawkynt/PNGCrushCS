using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes innoHeim/Rsupport Screen Capture Codec (<c>RSCC</c>/<c>ISCC</c>) frames.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/rscc.c</c>, copyright (C) 2015 Vittorio Giovara,
/// distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS
/// under LGPL-3.0-or-later.
/// <para/>
/// A frame is a set of rectangular tiles applied to a persistent picture. Descriptor tables larger
/// than five tiles may be zlib-compressed; the concatenated tile pixels have a second independent
/// raw-or-zlib size discriminator. Both descriptor and pixel paths are implemented. Tile rows are
/// stored bottom-up relative to their destination rectangle.
/// </remarks>
public sealed class RsccVideoDecoder : IVideoCodecDecoder<RsccVideoDecoder> {

  private static readonly CodecTag _RsccTag = CodecTag.FromCharacters("RSCC");
  private static readonly CodecTag _IsccTag = CodecTag.FromCharacters("ISCC");

  private readonly int _width;
  private readonly int _height;
  private readonly int _componentSize;
  private readonly NativeLayout _layout;
  private readonly int _streamIndex;
  private readonly byte[] _canvas;
  private readonly byte[]? _palette;

  private RsccVideoDecoder(
    int width,
    int height,
    int componentSize,
    NativeLayout layout,
    int streamIndex,
    byte[]? palette
  ) {
    this._width = width;
    this._height = height;
    this._componentSize = componentSize;
    this._layout = layout;
    this._streamIndex = streamIndex;
    this._canvas = new byte[checked(width * height * componentSize)];
    this._palette = palette;
  }

  public static string CodecName => "innoHeim/Rsupport Screen Capture Codec";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video
      && (stream.Codec.EqualsIgnoringCase(_RsccTag) || stream.Codec.EqualsIgnoringCase(_IsccTag));
  }

  public static RsccVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"RSCC stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    int componentSize;
    NativeLayout layout;
    byte[]? palette = null;

    if (stream.Codec.EqualsIgnoringCase(_IsccTag)) {
      var extra = stream.CodecPrivateData.Span.Length > BitmapInfoHeader.StructSize
        ? stream.CodecPrivateData.Span[BitmapInfoHeader.StructSize..]
        : ReadOnlySpan<byte>.Empty;
      var bgra = extra.Length == 4 ? ((extra[0] >> 1) & 1) != 0 : true;
      componentSize = bgra ? 4 : 3;
      layout = bgra ? NativeLayout.Bgra : NativeLayout.Bgr24;
    } else {
      (componentSize, layout) = stream.BitsPerPixel switch {
        8 => (1, NativeLayout.Indexed8),
        16 => (2, NativeLayout.Rgb555),
        24 => (3, NativeLayout.Bgr24),
        32 => (4, NativeLayout.Bgr0),
        _ => throw new NotSupportedException(
          $"RSCC stream {stream.Index} states {stream.BitsPerPixel} bits per pixel; defined layouts are 8, 16, 24 and 32."),
      };

      if (layout == NativeLayout.Indexed8)
        palette = _ReadAviPalette(stream);
    }

    if ((long)stream.Width * stream.Height * componentSize > int.MaxValue)
      throw new InvalidDataException($"RSCC stream {stream.Index}'s frame is too large to hold in memory.");

    return new(stream.Width, stream.Height, componentSize, layout, stream.Index, palette);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    var position = 0;
    if (data.Length < 2) {
      frame = null!;
      throw new InvalidDataException($"RSCC stream {this._streamIndex} carries a packet shorter than its tile count.");
    }

    var tileCount = _ReadUInt16(data, ref position, this._streamIndex, "tile count");
    if (tileCount == 0) {
      frame = null!;
      return false;
    }

    byte[] descriptorBytes;
    if (tileCount > 5) {
      var sizeWidth = tileCount < 32 ? 1 : 2;
      var packed = _ReadVariable(data, ref position, sizeWidth, this._streamIndex, "tile-table size");
      if (packed > data.Length - position)
        throw new InvalidDataException($"RSCC stream {this._streamIndex} truncates its tile table.");

      if (packed != tileCount * 8) {
        descriptorBytes = _InflateExact(data.Slice(position, packed), tileCount * 8, this._streamIndex, "tile table");
        position += packed;
      } else {
        descriptorBytes = data.Slice(position, packed).ToArray();
        position += packed;
      }
    } else {
      var bytes = tileCount * 8;
      if (bytes > data.Length - position)
        throw new InvalidDataException($"RSCC stream {this._streamIndex} truncates its tile table.");
      descriptorBytes = data.Slice(position, bytes).ToArray();
      position += bytes;
    }

    var tiles = new Tile[tileCount];
    long pixelBytesLong = 0;
    for (var i = 0; i < tileCount; ++i) {
      var at = i * 8;
      var x = BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(at));
      var w = BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(at + 2));
      var y = BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(at + 4));
      var h = BinaryPrimitives.ReadUInt16LittleEndian(descriptorBytes.AsSpan(at + 6));
      if (w == 0 || h == 0 || x + w > this._width || y + h > this._height)
        throw new InvalidDataException(
          $"RSCC stream {this._streamIndex} contains tile {i} at ({x},{y}) sized {w}x{h}, outside its "
          + $"{this._width}x{this._height} frame.");
      pixelBytesLong += (long)w * h * this._componentSize;
      if (pixelBytesLong > int.MaxValue)
        throw new InvalidDataException($"RSCC stream {this._streamIndex}'s tile payload is too large.");
      tiles[i] = new(x, y, w, h);
    }

    var pixelBytes = (int)pixelBytesLong;
    var sizeFieldWidth = pixelBytes < 0x100 ? 1 : pixelBytes < 0x10000 ? 2 : pixelBytes < 0x1000000 ? 3 : 4;
    var packedPixelBytes = _ReadVariable(data, ref position, sizeFieldWidth, this._streamIndex, "pixel payload size");
    if (packedPixelBytes > data.Length - position)
      throw new InvalidDataException($"RSCC stream {this._streamIndex} truncates its pixel payload.");

    var pixels = packedPixelBytes == pixelBytes
      ? data.Slice(position, pixelBytes).ToArray()
      : _InflateExact(data.Slice(position, packedPixelBytes), pixelBytes, this._streamIndex, "pixel payload");

    var source = 0;
    foreach (var tile in tiles) {
      var rowBytes = tile.Width * this._componentSize;
      for (var row = 0; row < tile.Height; ++row) {
        var outputRow = this._height - tile.Y - 1 - row;
        var destination = (outputRow * this._width + tile.X) * this._componentSize;
        pixels.AsSpan(source, rowBytes).CopyTo(this._canvas.AsSpan(destination, rowBytes));
        source += rowBytes;
      }
    }

    frame = this._Frame();
    return true;
  }

  private RawImage _Frame() => this._layout switch {
    NativeLayout.Indexed8 => new RawImage {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._canvas.Clone(),
      Palette = (byte[])this._palette!.Clone(),
      PaletteCount = this._palette.Length / 3,
    },
    NativeLayout.Rgb555 => new RawImage {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _Rgb555ToRgb24(this._canvas),
    },
    NativeLayout.Bgr24 => new RawImage {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = (byte[])this._canvas.Clone(),
    },
    NativeLayout.Bgra => new RawImage {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgra32,
      PixelData = (byte[])this._canvas.Clone(),
    },
    NativeLayout.Bgr0 => new RawImage {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgra32,
      PixelData = _Bgr0ToBgra(this._canvas),
    },
    _ => throw new InvalidOperationException(),
  };

  private static byte[] _ReadAviPalette(MediaStreamInfo stream) {
    var format = stream.CodecPrivateData.Span;
    var available = Math.Max(0, format.Length - BitmapInfoHeader.StructSize) / 4;
    var entries = Math.Min(256, available);
    if (entries == 0)
      throw new NotSupportedException($"RSCC stream {stream.Index} is indexed but carries no static AVI palette.");
    var palette = new byte[entries * 3];
    var source = format[BitmapInfoHeader.StructSize..];
    for (var i = 0; i < entries; ++i) {
      palette[i * 3] = source[i * 4 + 2];
      palette[i * 3 + 1] = source[i * 4 + 1];
      palette[i * 3 + 2] = source[i * 4];
    }
    return palette;
  }

  private static int _ReadUInt16(ReadOnlySpan<byte> data, ref int position, int streamIndex, string what) {
    if (data.Length - position < 2)
      throw new InvalidDataException($"RSCC stream {streamIndex} truncates its {what}.");
    var value = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
    position += 2;
    return value;
  }

  private static int _ReadVariable(ReadOnlySpan<byte> data, ref int position, int width, int streamIndex, string what) {
    if (width is < 1 or > 4 || data.Length - position < width)
      throw new InvalidDataException($"RSCC stream {streamIndex} truncates its {what}.");
    uint value = 0;
    for (var i = 0; i < width; ++i)
      value |= (uint)data[position + i] << (8 * i);
    position += width;
    if (value > int.MaxValue)
      throw new InvalidDataException($"RSCC stream {streamIndex}'s {what} is too large.");
    return (int)value;
  }

  private static byte[] _InflateExact(ReadOnlySpan<byte> compressed, int expected, int streamIndex, string what) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      var result = new byte[expected];
      var offset = 0;
      while (offset < expected) {
        var read = zlib.Read(result, offset, expected - offset);
        if (read == 0)
          break;
        offset += read;
      }
      if (offset != expected)
        throw new InvalidDataException(
          $"RSCC stream {streamIndex}'s {what} inflates to {offset} byte(s), expected {expected}.");
      return result;
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"RSCC stream {streamIndex} carries invalid zlib data in its {what}.", ex);
    }
  }

  private static byte[] _Rgb555ToRgb24(ReadOnlySpan<byte> source) {
    var result = new byte[source.Length / 2 * 3];
    var at = 0;
    for (var i = 0; i < source.Length; i += 2) {
      var value = source[i] | source[i + 1] << 8;
      var r = (value >> 10) & 31;
      var g = (value >> 5) & 31;
      var b = value & 31;
      result[at++] = (byte)((r << 3) | (r >> 2));
      result[at++] = (byte)((g << 3) | (g >> 2));
      result[at++] = (byte)((b << 3) | (b >> 2));
    }
    return result;
  }

  private static byte[] _Bgr0ToBgra(ReadOnlySpan<byte> source) {
    var result = source.ToArray();
    for (var i = 3; i < result.Length; i += 4)
      result[i] = 255;
    return result;
  }

  private readonly record struct Tile(int X, int Y, int Width, int Height);
  private enum NativeLayout { Indexed8, Rgb555, Bgr24, Bgra, Bgr0 }
}
