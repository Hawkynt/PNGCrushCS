using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Psp;

/// <summary>Assembles Paint Shop Pro file bytes from pixel data.</summary>
/// <remarks>
/// What went out before was a block identifier the format does not define, holding raw pixels where
/// a layer bank belongs. Nothing but this project's own reader could open it, and the two agreed
/// because they shared the invention. What goes out now is a layer bank holding one raster layer,
/// its colour separated into one channel a component the way every real file stores it, each channel
/// a zlib stream — which is what the format calls LZ77.
/// </remarks>
public static class PspWriter {

  private static ReadOnlySpan<byte> _BlockMarker => [0x7E, 0x42, 0x4B, 0x00];

  private const ushort _BLOCK_IMAGE_ATTRIBUTES = 0;
  private const ushort _BLOCK_LAYER_BANK = 3;
  private const ushort _BLOCK_LAYER = 4;
  private const ushort _BLOCK_CHANNEL = 5;

  private const ushort _COMPRESSION_LZ77 = 2;

  /// <summary>Everything the layer information chunk states after the layer's name.</summary>
  private const int _LAYER_TAIL_SIZE = 120;

  public static byte[] ToBytes(PspFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelData = file.PixelData ?? [];
    var bitDepth = file.BitDepth == 0 ? 24 : file.BitDepth;
    var majorVersion = file.MajorVersion == 0 ? (ushort)5 : file.MajorVersion;
    return Assemble(pixelData, file.Width, file.Height, bitDepth, majorVersion, file.MinorVersion, file.HasAlpha);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int bitDepth, ushort majorVersion, ushort minorVersion, bool hasAlpha = false) {
    var stride = hasAlpha ? 4 : 3;
    var pixels = width * height;

    var planes = new List<(ushort BitmapType, ushort ChannelType, byte[] Data)>();
    for (var component = 0; component < 3; ++component) {
      var plane = new byte[pixels];
      for (var i = 0; i < pixels; ++i) {
        var at = i * stride + component;
        plane[i] = at < pixelData.Length ? pixelData[at] : (byte)0;
      }

      planes.Add((0, (ushort)(component + 1), plane));
    }

    if (hasAlpha) {
      var alpha = new byte[pixels];
      for (var i = 0; i < pixels; ++i) {
        var at = i * stride + 3;
        alpha[i] = at < pixelData.Length ? pixelData[at] : (byte)255;
      }

      planes.Add((1, 0, alpha));
    }

    using var stream = new MemoryStream();
    stream.Write(PspFile.Magic);
    _WriteUInt16(stream, majorVersion);
    _WriteUInt16(stream, minorVersion);

    _WriteBlock(stream, _BLOCK_IMAGE_ATTRIBUTES, _BuildGeneralAttributes(width, height, bitDepth));
    _WriteBlock(stream, _BLOCK_LAYER_BANK, _BuildLayerBank(width, height, hasAlpha, planes));

    return stream.ToArray();
  }

  private static void _WriteBlock(Stream stream, ushort blockId, byte[] blockData) {
    stream.Write(_BlockMarker);
    _WriteUInt16(stream, blockId);
    _WriteUInt32(stream, (uint)blockData.Length);
    stream.Write(blockData);
  }

  private static void _WriteUInt16(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteInt32(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteRectangle(Stream stream, int left, int top, int right, int bottom) {
    _WriteInt32(stream, left);
    _WriteInt32(stream, top);
    _WriteInt32(stream, right);
    _WriteInt32(stream, bottom);
  }

  private static byte[] _BuildGeneralAttributes(int width, int height, int bitDepth) {
    using var stream = new MemoryStream();
    _WriteUInt32(stream, 46); // chunk size
    _WriteInt32(stream, width);
    _WriteInt32(stream, height);

    Span<byte> resolution = stackalloc byte[8];
    BinaryPrimitives.WriteDoubleLittleEndian(resolution, 72.0);
    stream.Write(resolution);

    stream.WriteByte(1); // metric: pixels per inch
    _WriteUInt16(stream, _COMPRESSION_LZ77);
    _WriteUInt16(stream, (ushort)bitDepth);
    _WriteUInt16(stream, 1); // plane count
    _WriteUInt32(stream, bitDepth == 24 ? 16777216u : 256u);
    stream.WriteByte(0); // not greyscale
    _WriteUInt32(stream, (uint)(width * height * 3));
    _WriteInt32(stream, 0); // active layer
    _WriteUInt16(stream, 1); // layer count
    _WriteUInt32(stream, 1); // graphic contents: raster
    return stream.ToArray();
  }

  private static byte[] _BuildLayerBank(int width, int height, bool hasAlpha, List<(ushort BitmapType, ushort ChannelType, byte[] Data)> planes) {
    using var bank = new MemoryStream();
    _WriteBlock(bank, _BLOCK_LAYER, _BuildLayer(width, height, hasAlpha, planes));
    return bank.ToArray();
  }

  private static byte[] _BuildLayer(int width, int height, bool hasAlpha, List<(ushort BitmapType, ushort ChannelType, byte[] Data)> planes) {
    using var layer = new MemoryStream();

    var name = "Raster 1"u8;
    _WriteUInt32(layer, (uint)(4 + 2 + name.Length + _LAYER_TAIL_SIZE));
    _WriteUInt16(layer, (ushort)name.Length);
    layer.Write(name);

    layer.WriteByte(1); // raster layer
    _WriteRectangle(layer, 0, 0, width, height);
    _WriteRectangle(layer, 0, 0, width, height);
    layer.WriteByte(255); // opacity
    layer.WriteByte(0); // blend mode: normal
    layer.WriteByte(0x01); // visible
    layer.WriteByte(0); // transparency not protected
    layer.WriteByte(0); // link group
    _WriteRectangle(layer, 0, 0, 0, 0);
    _WriteRectangle(layer, 0, 0, 0, 0);
    layer.WriteByte(0); // mask not linked
    layer.WriteByte(0); // mask not disabled
    layer.WriteByte(0); // do not invert mask on blend
    _WriteUInt16(layer, 0); // no valid blend ranges
    layer.Write(new byte[40]); // the five source/destination blend range pairs
    layer.WriteByte(0); // no highlight colour
    _WriteUInt32(layer, 0);

    _WriteUInt32(layer, 8); // layer bitmap chunk
    _WriteUInt16(layer, (ushort)(hasAlpha ? 2 : 1));
    _WriteUInt16(layer, (ushort)planes.Count);

    foreach (var (bitmapType, channelType, plane) in planes) {
      var compressed = _Deflate(plane);
      using var channel = new MemoryStream();
      _WriteUInt32(channel, 16); // channel information chunk size
      _WriteUInt32(channel, (uint)compressed.Length);
      _WriteUInt32(channel, (uint)plane.Length);
      _WriteUInt16(channel, bitmapType);
      _WriteUInt16(channel, channelType);
      channel.Write(compressed);
      _WriteBlock(layer, _BLOCK_CHANNEL, channel.ToArray());
    }

    return layer.ToArray();
  }

  private static byte[] _Deflate(byte[] data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, true))
      zlib.Write(data);

    return output.ToArray();
  }
}
