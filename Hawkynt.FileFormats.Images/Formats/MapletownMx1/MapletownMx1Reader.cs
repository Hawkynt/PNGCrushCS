using System;
using System.IO;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMx1;

/// <summary>Reads Mapletown Network MX1 pictures from bytes, streams, or file paths.</summary>
public static class MapletownMx1Reader {

  public static MapletownMx1File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MapletownMx1File FromStream(Stream stream) {
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

  public static MapletownMx1File FromSpan(ReadOnlySpan<byte> data) {
    var decode = MapletownStream.CreateDecodeTable();
    var stream = new MapletownStream(data, 0, decode);
    int[]? pixels = null;
    int width = 0, height = 0;

    if (!MapletownDecoder.FindImage(ref stream, data)
        || MapletownDecoder.Decode(ref stream, ref pixels, ref width, ref height, -1) < 0)
      throw new InvalidDataException("Not a Mapletown MX1 picture.");

    // One image is the whole picture; the rest of this is working out what several add up to.
    if (!MapletownDecoder.FindImage(ref stream, data))
      return new() { Width = width, Height = height, Pixels = MapletownDecoder.ToRgb(pixels!) };

    var sameSize = 1;
    var totalWidth = width;
    var totalHeight = height;

    do {
      int[]? next = null;
      int nextWidth = 0, nextHeight = 0;
      if (MapletownDecoder.Decode(ref stream, ref next, ref nextWidth, ref nextHeight, -1) < 0)
        throw new InvalidDataException("An MX1 image after the first is malformed.");

      if (sameSize > 0 && nextWidth == totalWidth && nextHeight == totalHeight)
        ++sameSize;
      else {
        totalWidth = Math.Max(totalWidth, nextWidth);

        // Once one image differs, the ones counted so far stop being a grid and become a stack.
        if (sameSize > 0) {
          totalHeight *= sameSize;
          sameSize = 0;
        }

        totalHeight += nextHeight;
      }
    } while (MapletownDecoder.FindImage(ref stream, data));

    stream = new MapletownStream(data, 0, decode);

    // Four images of one size are a two-by-two grid and sixteen are four-by-four; any other count
    // of equal images is simply stacked.
    var shift = sameSize switch { 4 => 1, 16 => 2, _ => 0 };
    if (shift > 0)
      return _Tiles(ref stream, data, totalWidth, totalHeight, shift);

    if (sameSize > 0)
      totalHeight *= sameSize;

    pixels = new int[totalWidth * totalHeight];
    var offset = 0;
    while (MapletownDecoder.FindImage(ref stream, data)) {
      var rows = MapletownDecoder.Decode(ref stream, ref pixels, ref totalWidth, ref totalHeight, offset);
      if (rows < 0)
        throw new InvalidDataException("An MX1 image is malformed on the second pass.");

      offset += rows * totalWidth;
    }

    return new() { Width = totalWidth, Height = totalHeight, Pixels = MapletownDecoder.ToRgb(pixels) };
  }

  /// <summary>Lays equal images out as a square grid rather than a column.</summary>
  private static MapletownMx1File _Tiles(
    ref MapletownStream stream, ReadOnlySpan<byte> data, int width, int height, int shift) {
    var totalWidth = width << shift;
    var totalHeight = height << shift;
    var pixels = new int[totalWidth * totalHeight];

    for (var y = 0; y < totalHeight; y += height)
    for (var x = 0; x < totalWidth; x += width) {
      int[]? target = pixels;
      var w = totalWidth;
      var h = totalHeight;
      if (!MapletownDecoder.FindImage(ref stream, data)
          || MapletownDecoder.Decode(ref stream, ref target, ref w, ref h, y * totalWidth + x) < 0)
        throw new InvalidDataException("An MX1 tile is missing or malformed.");
    }

    return new() { Width = totalWidth, Height = totalHeight, Pixels = MapletownDecoder.ToRgb(pixels) };
  }

  public static MapletownMx1File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
