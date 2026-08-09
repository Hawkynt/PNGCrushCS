using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Eroiica;

/// <summary>Reads Eroiica documents (.eif) from bytes, streams, or file paths.</summary>
public static class EroiicaReader {

  /// <summary>Tags naming where a page's pixels are and how long each run of them is.</summary>
  private const int _StripOffsets = 273;
  private const int _StripByteCounts = 279;
  private const int _TileOffsets = 324;
  private const int _TileByteCounts = 325;
  private const int _ImageWidth = 256;
  private const int _ImageLength = 257;

  public static EroiicaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Eroiica file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EroiicaFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static EroiicaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static EroiicaFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EroiicaFile.Magic.Length)
      throw new InvalidDataException($"Data too small for an Eroiica document (need at least {EroiicaFile.Magic.Length} bytes, got {data.Length}).");

    if (!data[..EroiicaFile.Magic.Length].SequenceEqual(EroiicaFile.Magic))
      throw new InvalidDataException("Not an Eroiica document: the eight bytes it opens with are not the ones this format uses.");

    var pages = new List<byte[]>();
    for (var at = EroiicaFile.Magic.Length; at + 8 <= data.Length;) {
      var extent = _TiffExtentAt(data, at);
      if (extent <= 0) {
        ++at;
        continue;
      }

      pages.Add(data.Slice(at, extent).ToArray());
      at += extent;
    }

    if (pages.Count == 0)
      throw new InvalidDataException("An Eroiica document with no page in it this can read: no complete TIFF stream stands in the file.");

    return new() { Pages = pages };
  }

  /// <summary>How long the TIFF standing at <paramref name="start"/> is, or zero when nothing stands there.</summary>
  /// <remarks>
  /// Everything the directory names has to be inside the file for the answer to be anything but
  /// zero: the directory itself, each entry whose value is too large to sit in the entry, and the
  /// runs of pixels the strip or tile tags point at. The extent returned is the last byte any of
  /// those reaches, so the stream handed to the TIFF reader is the stream and not the rest of the
  /// document behind it.
  /// </remarks>
  private static int _TiffExtentAt(ReadOnlySpan<byte> data, int start) {
    var length = data.Length - start;
    if (length < 8)
      return 0;

    bool littleEndian;
    if (data[start] == 'I' && data[start + 1] == 'I')
      littleEndian = true;
    else if (data[start] == 'M' && data[start + 1] == 'M')
      littleEndian = false;
    else
      return 0;

    if (_Read16(data, start + 2, littleEndian) != 42)
      return 0;

    var directory = (long)_Read32(data, start + 4, littleEndian);
    if (directory < 8 || directory + 2 > length)
      return 0;

    var extent = 8L;
    var seen = 0;
    var offsets = new List<long>();
    var counts = new List<long>();

    while (directory != 0) {
      if (++seen > 64 || directory + 2 > length)
        return 0;

      var entries = _Read16(data, start + (int)directory, littleEndian);
      if (entries == 0)
        return 0;

      var end = directory + 2 + (long)entries * 12 + 4;
      if (end > length)
        return 0;
      if (end > extent)
        extent = end;

      for (var i = 0; i < entries; ++i) {
        var entry = start + (int)directory + 2 + i * 12;
        var tag = _Read16(data, entry, littleEndian);
        var type = _Read16(data, entry + 2, littleEndian);
        var count = (long)_Read32(data, entry + 4, littleEndian);
        var size = _TypeSize(type);
        if (size == 0 || count < 0 || count > int.MaxValue / 8)
          return 0;

        var bytes = count * size;
        var valueAt = (long)_Read32(data, entry + 8, littleEndian);
        if (bytes > 4) {
          if (valueAt + bytes > length)
            return 0;
          if (valueAt + bytes > extent)
            extent = valueAt + bytes;
        }

        if (tag is _ImageWidth or _ImageLength && count == 1) {
          var value = bytes <= 4 ? _InlineValue(data, entry + 8, type, littleEndian) : 0;
          if (value <= 0)
            return 0;
        }

        if (tag is _StripOffsets or _TileOffsets)
          _Collect(data, entry, start, type, count, bytes, valueAt, littleEndian, offsets);
        else if (tag is _StripByteCounts or _TileByteCounts)
          _Collect(data, entry, start, type, count, bytes, valueAt, littleEndian, counts);
      }

      directory = _Read32(data, start + (int)directory + 2 + entries * 12, littleEndian);
      if (directory != 0 && (directory < 8 || directory + 2 > length))
        return 0;
    }

    if (offsets.Count == 0 || offsets.Count != counts.Count)
      return 0;

    for (var i = 0; i < offsets.Count; ++i) {
      var last = offsets[i] + counts[i];
      if (offsets[i] < 8 || counts[i] <= 0 || last > length)
        return 0;
      if (last > extent)
        extent = last;
    }

    return (int)extent;
  }

  private static void _Collect(ReadOnlySpan<byte> data, int entry, int start, int type, long count, long bytes, long valueAt, bool littleEndian, List<long> into) {
    var size = _TypeSize(type);
    if (size is not (2 or 4))
      return;

    var at = bytes <= 4 ? entry + 8 : start + (int)valueAt;
    for (var i = 0; i < count; ++i)
      into.Add(size == 2 ? _Read16(data, at + i * 2, littleEndian) : _Read32(data, at + i * 4, littleEndian));
  }

  private static long _InlineValue(ReadOnlySpan<byte> data, int at, int type, bool littleEndian)
    => _TypeSize(type) switch {
      1 => data[at],
      2 => _Read16(data, at, littleEndian),
      4 => _Read32(data, at, littleEndian),
      _ => 0,
    };

  private static int _TypeSize(int type) => type switch {
    1 or 2 or 6 or 7 => 1,
    3 or 8 => 2,
    4 or 9 or 11 => 4,
    5 or 10 or 12 => 8,
    _ => 0,
  };

  private static int _Read16(ReadOnlySpan<byte> data, int at, bool littleEndian)
    => littleEndian ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];

  private static long _Read32(ReadOnlySpan<byte> data, int at, bool littleEndian)
    => littleEndian
      ? (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24))
      : (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
