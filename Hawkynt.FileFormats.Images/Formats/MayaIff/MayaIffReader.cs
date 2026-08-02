using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.MayaIff;

/// <summary>Reads Maya IFF files from bytes, streams, or file paths.</summary>
public static class MayaIffReader {

  /// <summary>FOR4 magic bytes (46 4F 52 34).</summary>
  private static readonly byte[] _FOR4_MAGIC = "FOR4"u8.ToArray();

  /// <summary>CIMG form type bytes (43 49 4D 47).</summary>
  private static readonly byte[] _CIMG_TYPE = "CIMG"u8.ToArray();

  /// <summary>TBHD chunk tag.</summary>
  private static readonly byte[] _TBHD_TAG = "TBHD"u8.ToArray();

  /// <summary>RGBA chunk tag.</summary>
  private static readonly byte[] _RGBA_TAG = "RGBA"u8.ToArray();

  /// <summary>RGB  chunk tag (with trailing space).</summary>
  private static readonly byte[] _RGB_TAG = Encoding.ASCII.GetBytes("RGB ");

  /// <summary>Minimum file size: 12 (FOR4+size+CIMG) + 8 (TBHD tag+size) + 32 (TBHD data) = 52.</summary>
  private const int _MIN_FILE_SIZE = 12 + 8 + 32;

  public static MayaIffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Maya IFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MayaIffFile FromStream(Stream stream) {
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

  public static MayaIffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid Maya IFF file.");

    if (!data.Slice(0, 4).SequenceEqual(_FOR4_MAGIC))
      throw new InvalidDataException("Invalid Maya IFF magic: expected FOR4.");

    if (!data.Slice(8, 4).SequenceEqual(_CIMG_TYPE))
      throw new InvalidDataException("Invalid Maya IFF form type: expected CIMG.");

    var offset = 12;
    var width = 0;
    var height = 0;
    var tbhdFound = false;
    var tiles = new List<(int Left, int Top, int Right, int Bottom, byte[] Data)>();
    var hasAlpha = false;

    // The tiles live in a form of their own inside the outer one, so the walk descends into any
    // nested FOR4 rather than stepping over it.
    _Walk(data, offset, data.Length, ref width, ref height, ref tbhdFound, ref hasAlpha, tiles);

    if (!tbhdFound)
      throw new InvalidDataException("No TBHD chunk found in Maya IFF file.");

    var channels = hasAlpha ? 4 : 3;
    var pixelData = new byte[width * height * channels];

    // Each tile states its own corners, so it goes back where it came from rather than wherever the
    // reading happens to have reached.
    foreach (var (left, top, right, bottom, tile) in tiles) {
      var wide = right - left + 1;
      var high = bottom - top + 1;
      if (wide <= 0 || high <= 0)
        continue;

      var at = 0;
      for (var c = 0; c < channels; ++c)
      for (var y = top; y <= bottom && y < height; ++y)
      for (var x = left; x <= right && x < width; ++x) {
        if (at >= tile.Length)
          break;

        pixelData[(y * width + x) * channels + c] = tile[at++];
      }
    }

    return new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = hasAlpha,
      PixelData = pixelData,
    };
  
  }

  public static MayaIffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Walks the chunks of one form, descending into any form nested inside it.</summary>
  private static void _Walk(
    ReadOnlySpan<byte> data, int offset, int end, ref int width, ref int height,
    ref bool tbhdFound, ref bool hasAlpha,
    List<(int Left, int Top, int Right, int Bottom, byte[] Data)> tiles) {
    while (offset + 8 <= end) {
      var tag = data.Slice(offset, 4);
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
      var body = offset + 8;
      if (size < 0 || body + size > end)
        return;

      if (tag.SequenceEqual(_FOR4_MAGIC)) {
        // A nested form names its own type in the first four bytes of its body.
        _Walk(data, body + 4, body + size, ref width, ref height, ref tbhdFound, ref hasAlpha, tiles);
      } else if (tag.SequenceEqual(_TBHD_TAG) && size >= MayaIffTbhdHeader.StructSize) {
        var tbhd = MayaIffTbhdHeader.ReadFrom(data.Slice(body, MayaIffTbhdHeader.StructSize));
        width = (int)tbhd.Width;
        height = (int)tbhd.Height;
        tbhdFound = true;
      } else if (tag.SequenceEqual(_RGBA_TAG) || tag.SequenceEqual(_RGB_TAG)) {
        if (tag.SequenceEqual(_RGBA_TAG))
          hasAlpha = true;

        if (size >= 8) {
          var left = BinaryPrimitives.ReadUInt16BigEndian(data[body..]);
          var top = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 2)..]);
          var right = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 4)..]);
          var bottom = BinaryPrimitives.ReadUInt16BigEndian(data[(body + 6)..]);
          tiles.Add((left, top, right, bottom, data.Slice(body + 8, size - 8).ToArray()));
        }
      }

      offset = body + size + (size & 1);
    }
  }
}
