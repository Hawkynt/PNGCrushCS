using System;
using System.IO;

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

    for (var block = 0; block < blocks; ++block) {
      var end = at + _BigEndian(data, block << 2);
      if (end > data.Length || end < at)
        throw new InvalidDataException($"Block {block} of a DelmPaint picture runs past the end.");

      _UnpackBlock(data, ref at, end, unpacked, block * DelmPaintFile.BlockSize);
      at = end;
    }

    if (blocks != _SMALL_BLOCKS)
      return;

    _UnpackBlock(data, ref at, data.Length, unpacked, blocks * DelmPaintFile.BlockSize);
  }

  /// <summary>
  /// Unpacks one block, whose own four-byte head names the escape byte, a default value and the
  /// stride the block is laid out in.
  /// </summary>
  /// <remarks>
  /// A stride of zero is not a degenerate case but a shorthand: the block is the default value all
  /// the way through and the stream contributes nothing at all, which is what an empty screen or a
  /// solid background costs.
  /// </remarks>
  private static void _UnpackBlock(ReadOnlySpan<byte> data, ref int at, int end, Span<byte> unpacked, int target) {
    if (at > end - 4)
      throw new InvalidDataException("A DelmPaint block is too short to name its own encoding.");

    var escape = data[at];
    var fill = data[at + 1];
    var stride = (data[at + 2] << 8) | data[at + 3];
    if (stride >= DelmPaintFile.BlockSize)
      throw new InvalidDataException($"A DelmPaint block's stride of {stride} exceeds the block.");

    at += 4;

    var remaining = 0;
    var value = 0;
    if (stride == 0) {
      remaining = DelmPaintFile.BlockSize;
      value = fill;
      stride = 1;
    }

    for (var column = 0; column < stride; ++column)
    for (var position = column; position < DelmPaintFile.BlockSize; position += stride) {
      while (remaining == 0)
        _ReadCommand(data, ref at, end, escape, fill, ref remaining, ref value);

      --remaining;
      unpacked[target + position] = (byte)value;
    }
  }

  private static void _ReadCommand(
    ReadOnlySpan<byte> data, ref int at, int end, byte escape, byte fill, ref int remaining, ref int value) {
    if (at >= end)
      throw new InvalidDataException("A DelmPaint block ends before it has filled itself.");

    var b = data[at++];
    if (b != escape) {
      remaining = 1;
      value = b;
      return;
    }

    if (at >= end)
      throw new InvalidDataException("A DelmPaint escape has nothing after it.");

    var kind = data[at++];
    // The escape doubled stands for itself, which is what keeps it usable as a literal.
    if (kind == escape) {
      remaining = 1;
      value = kind;
      return;
    }

    if (at >= end)
      throw new InvalidDataException("A DelmPaint command has no count.");

    var count = data[at++];

    switch (kind) {
      case 0:
        remaining = count + 1;
        value = _Next(data, ref at, end);
        break;

      case 1:
        remaining = ((count << 8) | _Next(data, ref at, end)) + 1;
        value = _Next(data, ref at, end);
        break;

      case 2:
        // A high byte of zero means the whole block rather than a run of 256.
        remaining = count == 0 ? DelmPaintFile.BlockSize : ((count << 8) | _Next(data, ref at, end)) + 1;
        value = fill;
        break;

      default:
        remaining = kind + 1;
        value = count;
        break;
    }
  }

  private static byte _Next(ReadOnlySpan<byte> data, ref int at, int end) {
    if (at >= end)
      throw new InvalidDataException("A DelmPaint command runs past its block.");

    return data[at++];
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
