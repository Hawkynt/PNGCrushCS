using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Vips;

/// <summary>Reads VIPS native image files from bytes, streams, or file paths.</summary>
public static class VipsReader {

  internal const int HeaderSize = VipsHeader.StructSize;
  internal const int MagicValue = VipsHeader.MagicValue;

  public static VipsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VIPS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VipsFile FromStream(Stream stream) {
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

  public static VipsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < VipsHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid VIPS file.");

    // VIPS records its byte order in the magic itself rather than in a flag: the four bytes read
    // big-endian are 0x08F2A6B6 in a file written on a big-endian machine, and the same four read
    // little-endian in one written on a little-endian machine. Both are ordinary VIPS files. Only one
    // order was ever tried, so every file of the other kind was rejected as having a corrupt magic —
    // and the error even printed the two values as though they were unrelated.
    var isBigEndian = BinaryPrimitives.ReadInt32BigEndian(data) == VipsHeader.MagicValue;
    if (!isBigEndian && BinaryPrimitives.ReadInt32LittleEndian(data) != VipsHeader.MagicValue)
      throw new InvalidDataException(
        $"Invalid VIPS magic: expected 0x{VipsHeader.MagicValue:X8} in either byte order, "
        + $"got 0x{BinaryPrimitives.ReadInt32BigEndian(data):X8}.");

    var width = _ReadInt32(data, 4, isBigEndian);
    var height = _ReadInt32(data, 8, isBigEndian);
    var bands = _ReadInt32(data, 12, isBigEndian);

    if (width <= 0)
      throw new InvalidDataException($"Invalid VIPS width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid VIPS height: {height}.");
    if (bands <= 0)
      throw new InvalidDataException($"Invalid VIPS band count: {bands}.");

    var header = new VipsHeader(
      VipsHeader.MagicValue, width, height, bands,
      _ReadInt32(data, 16, isBigEndian), _ReadInt32(data, 20, isBigEndian),
      _ReadInt32(data, 24, isBigEndian), _ReadInt32(data, 28, isBigEndian),
      0f, 0f,
      _ReadInt32(data, 40, isBigEndian), _ReadInt32(data, 44, isBigEndian),
      _ReadInt32(data, 48, isBigEndian),
      0, 0, 0, 0);

    var bandFormat = (VipsBandFormat)header.BandFormat;
    if (bandFormat != VipsBandFormat.UChar)
      throw new NotSupportedException($"Only UChar band format is supported, got {bandFormat}.");

    var bytesPerPixel = header.Bands;
    var expectedPixelBytes = header.Width * header.Height * bytesPerPixel;
    var available = data.Length - VipsHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(VipsHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new VipsFile {
      Width = header.Width,
      Height = header.Height,
      Bands = header.Bands,
      BandFormat = bandFormat,
      PixelData = pixelData,
    };
    }

  /// <summary>One 32-bit header field, in whichever order this file was written in.</summary>
  private static int _ReadInt32(ReadOnlySpan<byte> data, int offset, bool isBigEndian)
    => isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(data[offset..])
      : BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

  public static VipsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
