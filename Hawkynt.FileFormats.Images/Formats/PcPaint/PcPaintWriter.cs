using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.PcPaint;

/// <summary>Assembles a version 2 Pictor page: header, VGA palette, and one compressed block.</summary>
/// <remarks>
/// What this writes is what <see cref="PcPaintReader"/> reads out of a real file, block header and
/// run marker and all — not a private encoding the two of them share. The marker is picked as a byte
/// the picture does not use where one is free, so that no literal ever has to be escaped; where the
/// picture uses all 256, the block is written with no runs at all.
/// </remarks>
public static class PcPaintWriter {

  public static byte[] ToBytes(PcPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    if (width <= 0 || height <= 0)
      throw new ArgumentException("A Pictor page needs a size.", nameof(file));

    var pixels = file.PixelData ?? throw new ArgumentException("A Pictor page needs pixels.", nameof(file));
    if (pixels.Length < width * height)
      throw new ArgumentException($"A {width}x{height} Pictor page needs {width * height} bytes; {pixels.Length} were given.", nameof(file));

    using var ms = new MemoryStream();

    _WriteUInt16(ms, PcPaintFile.Magic);
    _WriteUInt16(ms, width);
    _WriteUInt16(ms, height);
    _WriteUInt16(ms, file.XOffset);
    _WriteUInt16(ms, file.YOffset);
    ms.WriteByte(8);
    ms.WriteByte(PcPaintFile.VersionTwoFlag);
    ms.WriteByte(file.VideoMode == 0 ? (byte)'A' : file.VideoMode);
    _WriteUInt16(ms, PcPaintFile.PaletteVga);
    _WriteUInt16(ms, PcPaintFile.VgaPaletteBytes);

    var palette = file.Palette ?? [];
    for (var i = 0; i < PcPaintFile.VgaPaletteBytes; ++i)
      ms.WriteByte(i < palette.Length ? (byte)(palette[i] >> 2) : (byte)0);

    // Rows are stored from the bottom of the picture upwards.
    var flipped = new byte[width * height];
    for (var row = 0; row < height; ++row)
      Array.Copy(pixels, (height - 1 - row) * width, flipped, row * width, width);

    // A block states both its own size and the length it unpacks to in sixteen bits, so a picture
    // larger than that goes out as several — which is what the format's own count of blocks is for.
    var blocks = new List<byte[]>();
    for (var at = 0; at < flipped.Length; at += MaxBlockPixels) {
      var take = Math.Min(MaxBlockPixels, flipped.Length - at);
      blocks.Add(_Compress(flipped.AsSpan(at, take).ToArray()));
    }

    _WriteUInt16(ms, blocks.Count);
    foreach (var block in blocks)
      ms.Write(block, 0, block.Length);

    return ms.ToArray();
  }

  /// <summary>The most a single block may unpack to, kept clear of both sixteen-bit fields.</summary>
  private const int MaxBlockPixels = 0x8000;

  private static byte[] _Compress(byte[] data) {
    // The marker is whichever byte the picture uses least, so that the fewest of its own pixels have
    // to be written as one-long runs to keep them from being read as a marker.
    var counts = new int[256];
    foreach (var value in data)
      ++counts[value];

    var marker = 0;
    for (var candidate = 1; candidate < 256; ++candidate)
      if (counts[candidate] < counts[marker])
        marker = candidate;

    var body = new List<byte>(data.Length);

    var at = 0;
    while (at < data.Length) {
      var value = data[at];
      var run = 1;
      while (at + run < data.Length && data[at + run] == value && run < 0xFFFF)
        ++run;

      // A run costs three bytes short and five long, so anything shorter goes out as literals —
      // except the marker itself, which has to be run-encoded however few of it there are.
      if (run >= 4 || value == marker) {
        body.Add((byte)marker);
        if (run <= 0xFF)
          body.Add((byte)run);
        else {
          body.Add(0);
          body.Add((byte)(run & 0xFF));
          body.Add((byte)((run >> 8) & 0xFF));
        }

        body.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          body.Add(value);

      at += run;
    }

    var block = new byte[PcPaintFile.BlockHeaderSize + body.Count];
    var size = block.Length;
    block[0] = (byte)(size & 0xFF);
    block[1] = (byte)((size >> 8) & 0xFF);
    block[2] = (byte)(data.Length & 0xFF);
    block[3] = (byte)((data.Length >> 8) & 0xFF);
    block[4] = (byte)marker;
    body.CopyTo(block, PcPaintFile.BlockHeaderSize);
    return block;
  }

  private static void _WriteUInt16(Stream stream, int value) {
    stream.WriteByte((byte)(value & 0xFF));
    stream.WriteByte((byte)((value >> 8) & 0xFF));
  }
}
