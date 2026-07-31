using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DelmPaint;

/// <summary>Reads DelmPaint pictures from bytes, streams, or file paths.</summary>
public static class DelmPaintReader {

  /// <summary>Blocks the single-quadrant form declares.</summary>
  private const int _SMALL_BLOCKS = 2;

  /// <summary>Blocks the four-quadrant form declares.</summary>
  private const int _LARGE_BLOCKS = 10;

  public static DelmPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), file.Extension);
  }

  public static DelmPaintFile FromStream(Stream stream) {
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

  public static DelmPaintFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, false);

  /// <summary>Reads a picture, told whether it is the four-quadrant form.</summary>
  /// <remarks>
  /// Nothing inside the file says how many blocks it declares — the count is fixed per extension,
  /// and reading a small picture as a large one asks for eight blocks that are not there.
  /// </remarks>
  public static DelmPaintFile FromSpan(ReadOnlySpan<byte> data, bool large) {
    var blocks = large ? _LARGE_BLOCKS : _SMALL_BLOCKS;

    // The single-quadrant form has a third block after the two it declares, holding the remainder.
    var unpacked = new byte[(blocks + (large ? 0 : 1)) * DelmPaintFile.BlockSize];
    _Unpack(data, unpacked, blocks);

    return large
      ? new() { Unpacked = unpacked, Width = DelmPaintFile.QuadrantWidth * 2, Height = DelmPaintFile.QuadrantHeight * 2 }
      : new() { Unpacked = unpacked, Width = DelmPaintFile.QuadrantWidth, Height = DelmPaintFile.QuadrantHeight };
  }

  private static void _Unpack(ReadOnlySpan<byte> data, Span<byte> unpacked, int blocks) {
    var at = blocks << 2;
    if (at >= data.Length)
      throw new InvalidDataException("A DelmPaint picture's block table fills the whole file.");

    var rle = new AtariStCaRle(data, at);

    for (var block = 0; block < blocks; ++block) {
      var end = at + _BigEndian(data, block << 2);
      if (end > data.Length || end < at)
        throw new InvalidDataException($"Block {block} of a DelmPaint picture runs past the end.");

      rle.Position = at;
      rle.UnpackBlock(unpacked, block * DelmPaintFile.BlockSize, DelmPaintFile.BlockSize, end);
      at = end;
    }

    // The small form has one further block after the two it counts, holding the remainder.
    if (blocks != _SMALL_BLOCKS)
      return;

    rle.Position = at;
    rle.UnpackBlock(unpacked, blocks * DelmPaintFile.BlockSize, DelmPaintFile.BlockSize, data.Length);
  }

  private static int _BigEndian(ReadOnlySpan<byte> data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

  public static DelmPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads a picture, taking the form from the file name as the format requires.</summary>
  public static DelmPaintFile FromBytes(byte[] data, string extension) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, extension.Equals(".dph", StringComparison.OrdinalIgnoreCase));
  }
}
