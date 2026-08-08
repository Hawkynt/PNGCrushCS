using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Psp;

/// <summary>Reads Paint Shop Pro files from bytes, streams, or file paths.</summary>
/// <remarks>
/// A Paint Shop Pro file does not hold a picture: it holds a stack of layers, and every layer holds
/// its colour one channel at a time, each channel its own compressed stream. Reading the bytes that
/// follow a block header as though they were pixels — which is what this did — cannot produce a
/// picture from any real file, because none of them are stored that way and almost none of them are
/// stored uncompressed. The default a tube, a brush, a frame and an ordinary image are all saved
/// with is LZ77, which is a zlib stream.
/// </remarks>
public static class PspReader {

  private const int _MAGIC_SIZE = 32;
  private const int _FILE_HEADER_SIZE = _MAGIC_SIZE + 4; // magic + major(2) + minor(2)

  /// <summary>The four bytes every block opens with.</summary>
  private static ReadOnlySpan<byte> _BlockMarker => [0x7E, 0x42, 0x4B, 0x00];

  /// <summary>Marker(4) + id(2) + length(4), which is what a block header is from version four on.</summary>
  private const int _BLOCK_HEADER_SIZE = 10;

  /// <summary>The largest a Paint Shop Pro picture may be; beyond it a header was misread.</summary>
  private const int _LARGEST_SIDE = 30000;

  private const ushort _BLOCK_IMAGE_ATTRIBUTES = 0;
  private const ushort _BLOCK_COLOR_PALETTE = 2;
  private const ushort _BLOCK_LAYER_BANK = 3;
  private const ushort _BLOCK_LAYER = 4;
  private const ushort _BLOCK_CHANNEL = 5;
  private const ushort _BLOCK_COMPOSITE_IMAGE = 9;
  private const ushort _BLOCK_COMPOSITE_IMAGE_BANK = 16;
  private const ushort _BLOCK_COMPOSITE_ATTRIBUTES = 17;

  private const ushort _COMPRESSION_NONE = 0;
  private const ushort _COMPRESSION_RLE = 1;
  private const ushort _COMPRESSION_LZ77 = 2;

  private const ushort _CHANNEL_COMPOSITE = 0;
  private const ushort _CHANNEL_RED = 1;
  private const ushort _CHANNEL_BLUE = 3;

  private const ushort _DIB_IMAGE = 0;
  private const ushort _DIB_TRANSPARENCY_MASK = 1;
  private const ushort _DIB_COMPOSITE = 8;
  private const ushort _DIB_COMPOSITE_TRANSPARENCY_MASK = 9;

  private const byte _LAYER_RASTER = 1;
  private const byte _LAYER_FLOATING_RASTER_SELECTION = 2;
  private const byte _LAYER_VISIBLE_FLAG = 0x01;

  public static PspFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PSP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PspFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static PspFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>One layer's colour, already decompressed and placed where the layer says it sits.</summary>
  private sealed class _Layer {
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public byte Opacity = 255;
    public bool Visible = true;
    public byte[]? Red;
    public byte[]? Green;
    public byte[]? Blue;
    public byte[]? Indices;
    public byte[]? Alpha;
  }

  public static PspFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _FILE_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid PSP file.");

    _ValidateMagic(data);

    var majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data[_MAGIC_SIZE..]);
    var minorVersion = BinaryPrimitives.ReadUInt16LittleEndian(data[(_MAGIC_SIZE + 2)..]);

    // Every chunk inside a block states its own length from version four on, which is what lets the
    // fields after it be found without counting bytes. Version three states none of them and lays
    // its layers out differently; there is no sample of one here to check a reader against, so it is
    // refused rather than guessed at.
    if (majorVersion < 4)
      throw new InvalidDataException($"Paint Shop Pro file version {majorVersion}.{minorVersion} is older than the block layout this reads.");

    var width = 0;
    var height = 0;
    var bitDepth = 24;
    var compression = _COMPRESSION_NONE;
    var greyscale = false;
    byte[]? palette = null;
    var layers = new List<_Layer>();
    _Layer? composite = null;

    foreach (var (blockId, blockData) in _Blocks(data, _FILE_HEADER_SIZE, data.Length))
      switch (blockId) {
        case _BLOCK_IMAGE_ATTRIBUTES:
          _ReadImageAttributes(blockData, out width, out height, out bitDepth, out compression, out greyscale);
          break;
        case _BLOCK_COLOR_PALETTE:
          palette = _ReadPalette(blockData);
          break;
        case _BLOCK_LAYER_BANK:
          _ReadLayerBank(blockData, compression, bitDepth, layers);
          break;
        case _BLOCK_COMPOSITE_IMAGE_BANK:
          composite = _ReadCompositeBank(blockData, width, height, bitDepth, ref palette);
          break;
      }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("PSP file missing General Image Attributes block or invalid dimensions.");
    if (width > _LARGEST_SIDE || height > _LARGEST_SIDE)
      throw new InvalidDataException($"A Paint Shop Pro picture of {width}x{height} is larger than the format allows.");

    var drawable = layers.FindAll(layer => layer.Visible && (layer.Red != null || layer.Indices != null));
    if (drawable.Count == 0 && composite != null)
      drawable = [composite];

    if (drawable.Count == 0)
      throw new InvalidDataException("PSP file carries no layer this can draw.");

    var (pixels, hasAlpha) = _Compose(drawable, width, height, palette, greyscale);

    return new PspFile {
      Width = width,
      Height = height,
      BitDepth = bitDepth,
      MajorVersion = majorVersion,
      MinorVersion = minorVersion,
      HasAlpha = hasAlpha,
      PixelData = pixels,
    };
  }

  private static void _ValidateMagic(ReadOnlySpan<byte> data) {
    for (var i = 0; i < PspFile.Magic.Length; ++i)
      if (data[i] != PspFile.Magic[i])
        throw new InvalidDataException("Invalid PSP magic bytes.");
  }

  /// <summary>Walks the blocks between two offsets, tolerating the chunk a bank puts before them.</summary>
  /// <remarks>
  /// A layer states its own attributes in a chunk, then a second chunk saying how many channels
  /// follow, and only then the channel blocks. Neither chunk is a block, so a walk that expects a
  /// marker at once stops at the first layer. The gap between two blocks is bounded and short, so it
  /// is stepped over rather than treated as the end.
  /// </remarks>
  private static IEnumerable<(ushort Id, byte[] Data)> _Blocks(byte[] data, int offset, int end) {
    const int LARGEST_GAP = 64;

    while (offset + _BLOCK_HEADER_SIZE <= end) {
      if (!data.AsSpan(offset, _BlockMarker.Length).SequenceEqual(_BlockMarker)) {
        var found = _FindMarker(data, offset, Math.Min(offset + LARGEST_GAP, end));
        if (found < 0)
          yield break;

        offset = found;
        if (offset + _BLOCK_HEADER_SIZE > end)
          yield break;
      }

      var blockId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));
      var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 6));
      var dataOffset = offset + _BLOCK_HEADER_SIZE;
      if (totalLength > (uint)(end - dataOffset))
        yield break;

      yield return (blockId, data[dataOffset..(dataOffset + (int)totalLength)]);
      offset = dataOffset + (int)totalLength;
    }
  }

  private static IEnumerable<(ushort Id, byte[] Data)> _Blocks(ReadOnlySpan<byte> data, int offset, int end)
    => _Blocks(data.ToArray(), offset, end);

  private static int _FindMarker(byte[] data, int from, int to) {
    for (var i = from; i + _BlockMarker.Length <= to; ++i)
      if (data[i] == 0x7E && data[i + 1] == 0x42 && data[i + 2] == 0x4B && data[i + 3] == 0x00)
        return i;

    return -1;
  }

  private static void _ReadImageAttributes(byte[] block, out int width, out int height, out int bitDepth, out ushort compression, out bool greyscale) {
    // chunk(4) width(4) height(4) resolution(8) metric(1) compression(2) depth(2) planes(2)
    // colours(4) greyscale(1) totalSize(4) activeLayer(4) layerCount(2) contents(4)
    if (block.Length < 32)
      throw new InvalidDataException("General Image Attributes block too small.");

    width = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(4));
    height = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(8));
    compression = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(21));
    bitDepth = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(23));
    greyscale = block[31] != 0;
  }

  /// <summary>Reads a palette, which the file states as blue, green, red and a spare byte an entry.</summary>
  private static byte[]? _ReadPalette(byte[] block) {
    if (block.Length < 8)
      return null;

    var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block);
    var entries = (int)BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(4));
    if (chunkSize < 8 || entries <= 0 || entries > 256)
      return null;

    if (chunkSize + entries * 4 > block.Length)
      entries = Math.Max(0, (block.Length - chunkSize) / 4);

    var palette = new byte[256 * 3];
    for (var i = 0; i < entries; ++i) {
      var at = chunkSize + i * 4;
      palette[i * 3] = block[at + 2];
      palette[i * 3 + 1] = block[at + 1];
      palette[i * 3 + 2] = block[at];
    }

    return palette;
  }

  private static void _ReadLayerBank(byte[] block, ushort compression, int bitDepth, List<_Layer> layers) {
    foreach (var (id, layerBlock) in _Blocks(block, 0, block.Length)) {
      if (id != _BLOCK_LAYER)
        continue;

      var layer = _ReadLayer(layerBlock, compression, bitDepth);
      if (layer != null)
        layers.Add(layer);
    }
  }

  private static _Layer? _ReadLayer(byte[] block, ushort compression, int bitDepth) {
    if (block.Length < 8)
      return null;

    var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block);
    if (chunkSize < 8 || chunkSize > block.Length)
      return null;

    var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(4));
    var at = 6 + nameLength;
    if (at + 1 + 16 + 16 + 3 > block.Length)
      return null;

    var layerType = block[at];
    ++at;
    at += 16; // image rectangle
    var left = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(at));
    var top = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(at + 4));
    var right = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(at + 8));
    var bottom = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(at + 12));
    at += 16;
    var opacity = block[at];
    var flags = block[at + 2];

    if (layerType != _LAYER_RASTER && layerType != _LAYER_FLOATING_RASTER_SELECTION)
      return null;

    var width = right - left;
    var height = bottom - top;
    if (width <= 0 || height <= 0 || width > _LARGEST_SIDE || height > _LARGEST_SIDE)
      return null;

    var layer = new _Layer {
      X = left,
      Y = top,
      Width = width,
      Height = height,
      Opacity = opacity,
      Visible = (flags & _LAYER_VISIBLE_FLAG) != 0,
    };

    _ReadChannels(block, chunkSize, block.Length, compression, bitDepth, width, height, layer);
    return layer;
  }

  private static _Layer? _ReadCompositeBank(byte[] block, int width, int height, int bitDepth, ref byte[]? palette) {
    if (block.Length < 8)
      return null;

    var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block);
    if (chunkSize < 8 || chunkSize > block.Length)
      return null;

    // The bank holds the picture and its thumbnails side by side, each described by an attributes
    // block that comes before all of them. Drawing whichever one appears first would draw a
    // thumbnail as if it were the picture, so a composite is only taken when its own attributes say
    // it is the full size the file states.
    var attributes = new List<(int Width, int Height, int Depth, ushort Compression, ushort Kind)>();
    var index = 0;

    foreach (var (id, sub) in _Blocks(block, chunkSize, block.Length))
      switch (id) {
        case _BLOCK_COMPOSITE_ATTRIBUTES when sub.Length >= 24: {
          attributes.Add((
            BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(4)),
            BinaryPrimitives.ReadInt32LittleEndian(sub.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(sub.AsSpan(12)),
            BinaryPrimitives.ReadUInt16LittleEndian(sub.AsSpan(14)),
            BinaryPrimitives.ReadUInt16LittleEndian(sub.AsSpan(22))));
          break;
        }
        case _BLOCK_COMPOSITE_IMAGE: {
          var described = index < attributes.Count ? attributes[index] : default;
          ++index;
          if (described.Width != width || described.Height != height)
            break;

          var layer = new _Layer { Width = width, Height = height };
          var compositePalette = palette;
          foreach (var (paletteId, paletteBlock) in _Blocks(sub, 0, sub.Length))
            if (paletteId == _BLOCK_COLOR_PALETTE)
              compositePalette = _ReadPalette(paletteBlock) ?? compositePalette;

          palette = compositePalette;
          _ReadChannels(sub, _CompositeChunkSize(sub), sub.Length, described.Compression, described.Depth, width, height, layer);
          if (layer.Red != null || layer.Indices != null)
            return layer;

          break;
        }
        default:
          // A JPEG composite is counted so the attributes stay in step with the images they describe.
          if (id == 18)
            ++index;

          break;
      }

    return null;
  }

  private static int _CompositeChunkSize(byte[] block)
    => block.Length >= 4 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(block) : 0;

  private static void _ReadChannels(byte[] block, int from, int end, ushort compression, int bitDepth, int width, int height, _Layer layer) {
    var expected = width * height;

    foreach (var (id, channel) in _Blocks(block, from, end)) {
      if (id != _BLOCK_CHANNEL || channel.Length < 16)
        continue;

      var chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(channel);
      var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(channel.AsSpan(4));
      var bitmapType = BinaryPrimitives.ReadUInt16LittleEndian(channel.AsSpan(12));
      var channelType = BinaryPrimitives.ReadUInt16LittleEndian(channel.AsSpan(14));
      if (chunkSize < 16 || chunkSize > channel.Length)
        continue;

      var available = channel.Length - chunkSize;
      if (compressedLength <= 0 || compressedLength > available)
        compressedLength = available;

      byte[] content;
      try {
        content = _Decompress(channel.AsSpan(chunkSize, compressedLength), compression, width, height, bitDepth, expected);
      } catch (Exception) {
        continue;
      }

      switch (bitmapType) {
        case _DIB_TRANSPARENCY_MASK:
        case _DIB_COMPOSITE_TRANSPARENCY_MASK:
          layer.Alpha = content;
          continue;
        case _DIB_IMAGE:
        case _DIB_COMPOSITE:
          break;
        default:
          continue;
      }

      switch (channelType) {
        case _CHANNEL_COMPOSITE:
          layer.Indices = content;
          break;
        case _CHANNEL_RED:
          layer.Red = content;
          break;
        case 2:
          layer.Green = content;
          break;
        case _CHANNEL_BLUE:
          layer.Blue = content;
          break;
      }
    }
  }

  /// <summary>Turns one channel's stored bytes into one byte a pixel.</summary>
  /// <remarks>
  /// What the format calls LZ77 is a zlib stream, so it inflates with the same code a PNG does. The
  /// stated uncompressed length is not the channel's own — real files put a figure there covering
  /// more than the channel — so the picture's own size decides how much is expected.
  /// </remarks>
  private static byte[] _Decompress(ReadOnlySpan<byte> content, ushort compression, int width, int height, int bitDepth, int expected) {
    switch (compression) {
      case _COMPRESSION_LZ77: {
        using var input = new MemoryStream(content.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[expected];
        var read = 0;
        while (read < expected) {
          var got = zlib.Read(output, read, expected - read);
          if (got <= 0)
            break;

          read += got;
        }

        return bitDepth >= 8 ? output : _ExpandNarrowSamples(output, width, height, bitDepth, expected);
      }
      case _COMPRESSION_RLE: {
        var output = new byte[expected];
        var at = 0;
        var put = 0;
        while (put < expected && at < content.Length) {
          var run = content[at];
          ++at;
          if (run > 128) {
            run -= 128;
            if (at >= content.Length)
              break;

            var value = content[at];
            ++at;
            var count = Math.Min(run, expected - put);
            output.AsSpan(put, count).Fill(value);
            put += count;
          } else {
            var count = Math.Min(run, Math.Min(expected - put, content.Length - at));
            content.Slice(at, count).CopyTo(output.AsSpan(put));
            at += run;
            put += count;
          }
        }

        return bitDepth >= 8 ? output : _ExpandNarrowSamples(output, width, height, bitDepth, expected);
      }
      default: {
        // Uncompressed rows start on a four-byte boundary, which is the only place that padding
        // appears — a compressed stream carries the rows end to end.
        var stride = bitDepth >= 8 ? width : (width * bitDepth + 7) / 8;
        var padded = (stride + 3) & ~3;
        var output = new byte[expected];
        if (bitDepth >= 8) {
          for (var y = 0; y < height; ++y) {
            var at = y * padded;
            if (at + width > content.Length)
              break;

            content.Slice(at, width).CopyTo(output.AsSpan(y * width));
          }

          return output;
        }

        var rows = new byte[padded * height];
        content[..Math.Min(content.Length, rows.Length)].CopyTo(rows);
        return _ExpandNarrowSamples(rows, width, height, bitDepth, expected, padded);
      }
    }
  }

  /// <summary>Spreads indices narrower than a byte one to a byte.</summary>
  private static byte[] _ExpandNarrowSamples(byte[] packed, int width, int height, int bitDepth, int expected, int stride = 0) {
    if (stride <= 0)
      stride = (width * bitDepth + 7) / 8;

    var output = new byte[expected];
    var mask = (1 << bitDepth) - 1;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var bit = x * bitDepth;
      var at = y * stride + (bit >> 3);
      if (at >= packed.Length)
        return output;

      output[y * width + x] = (byte)((packed[at] >> (8 - bitDepth - (bit & 7))) & mask);
    }

    return output;
  }

  /// <summary>Lays the layers over one another from the bottom of the file upwards.</summary>
  private static (byte[] Pixels, bool HasAlpha) _Compose(List<_Layer> layers, int width, int height, byte[]? palette, bool greyscale) {
    var canvas = new byte[width * height * 4];
    var anyAlpha = false;

    foreach (var layer in layers) {
      var opacity = layer.Opacity;
      if (opacity == 0)
        continue;

      for (var y = 0; y < layer.Height; ++y) {
        var targetY = layer.Y + y;
        if (targetY < 0 || targetY >= height)
          continue;

        for (var x = 0; x < layer.Width; ++x) {
          var targetX = layer.X + x;
          if (targetX < 0 || targetX >= width)
            continue;

          var source = y * layer.Width + x;
          byte r, g, b;
          if (layer.Red != null && layer.Green != null && layer.Blue != null) {
            if (source >= layer.Red.Length || source >= layer.Green.Length || source >= layer.Blue.Length)
              continue;

            r = layer.Red[source];
            g = layer.Green[source];
            b = layer.Blue[source];
          } else if (layer.Indices != null) {
            if (source >= layer.Indices.Length)
              continue;

            var index = layer.Indices[source];
            if (palette != null && !greyscale) {
              r = palette[index * 3];
              g = palette[index * 3 + 1];
              b = palette[index * 3 + 2];
            } else {
              r = g = b = index;
            }
          } else {
            continue;
          }

          var alpha = layer.Alpha != null && source < layer.Alpha.Length ? layer.Alpha[source] : (byte)255;
          if (alpha < 255)
            anyAlpha = true;

          alpha = (byte)(alpha * opacity / 255);
          var target = (targetY * width + targetX) * 4;
          if (alpha == 255) {
            canvas[target] = r;
            canvas[target + 1] = g;
            canvas[target + 2] = b;
            canvas[target + 3] = 255;
            continue;
          }

          var below = canvas[target + 3];
          var combined = alpha + below * (255 - alpha) / 255;
          if (combined == 0)
            continue;

          canvas[target] = (byte)((r * alpha + canvas[target] * below * (255 - alpha) / 255) / combined);
          canvas[target + 1] = (byte)((g * alpha + canvas[target + 1] * below * (255 - alpha) / 255) / combined);
          canvas[target + 2] = (byte)((b * alpha + canvas[target + 2] * below * (255 - alpha) / 255) / combined);
          canvas[target + 3] = (byte)combined;
        }
      }
    }

    if (anyAlpha)
      return (canvas, true);

    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      rgb[i * 3] = canvas[i * 4];
      rgb[i * 3 + 1] = canvas[i * 4 + 1];
      rgb[i * 3 + 2] = canvas[i * 4 + 2];
    }

    return (rgb, false);
  }
}
