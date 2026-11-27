using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Hqx;

namespace FileFormat.Codecs.SheerVideo;

/// <summary>A decoded SheerVideo picture in FFmpeg's planes, and the layout that made it.</summary>
internal sealed class SheerFrame {

  internal const int HeaderLength = 20;

  internal required SheerFormat Format { get; init; }
  internal required int[][] Planes { get; init; }
  internal required int[] Strides { get; init; }

  private static readonly Dictionary<string, HqxVlc[]> _Codes = [];

  /// <summary>FFmpeg's <c>build_vlc</c>: lengths 1 to 15 going up, the sixteens, then 15 to 1 coming down; symbols are their order.</summary>
  internal static HqxVlc Build(SheerTable table) {
    var lengths = new List<byte>();
    var at = 0;
    for (int step = 1, length = 1; length > 0; length += step) {
      int count;
      if (length == 16) {
        count = table.Sixteens;
        step = -1;
      } else
        count = table.Counts[at++];
      for (var i = 0; i < count; ++i)
        lengths.Add((byte)length);
    }
    var symbols = new int[lengths.Count];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = i;
    return HqxVlc.FromLengths(lengths.ToArray(), symbols);
  }

  internal static HqxVlc[] Codes(string table) {
    lock (_Codes)
      return _Codes.TryGetValue(table, out var codes) ? codes : _Codes[table] = [Build(SheerDecode.Tables[table][0]), Build(SheerDecode.Tables[table][1])];
  }

  internal static SheerFormat? Find(uint tag) {
    foreach (var format in SheerDecode.Formats)
      if (format.Tag == tag)
        return format;
    return null;
  }

  /// <summary>How many planes FFmpeg's pixel format has, and whether its first is packed four bytes a pixel.</summary>
  internal static (int Planes, bool Packed) Shape(string pixelFormat) => pixelFormat switch {
    "RGB0" or "ARGB" => (1, true),
    "GBRAP10" or "YUVA444P" or "YUVA444P10" or "YUVA422P" or "YUVA422P10" => (4, false),
    _ => (3, false),
  };

  internal static SheerFrame Decode(ReadOnlySpan<byte> packet, int width, int height) {
    if (packet.Length <= HeaderLength)
      throw new InvalidDataException("A SheerVideo frame is more than its 20-byte header.");
    var magic = BinaryPrimitives.ReadUInt32LittleEndian(packet);
    if (magic != 0x72696853 && magic != 0x6B61775A) // "Shir", "Zwak"
      throw new InvalidDataException("This frame opens with neither Shir nor Zwak; it is not SheerVideo.");
    var tag = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]);
    if (Find(tag) is not { } format)
      throw new NotSupportedException($"SheerVideo layout 0x{tag:X8} is not one FFmpeg reads either.");
    if (packet.Length < HeaderLength + width * height / 16)
      throw new InvalidDataException("This SheerVideo frame is smaller than any picture of its size can be.");

    var (count, packed) = Shape(format.PixelFormat);
    var planes = new int[count][];
    var strides = new int[count];
    for (var p = 0; p < count; ++p) {
      strides[p] = (packed ? 4 * width : width) + 16;
      planes[p] = new int[strides[p] * height];
    }
    var reader = new HqxBitReader(packet[HeaderLength..]);
    SheerDecode.Decoder(format.Decoder)(width, height, format.Alt, planes, strides, Codes(format.Table), ref reader);
    return new() { Format = format, Planes = planes, Strides = strides };
  }
}
