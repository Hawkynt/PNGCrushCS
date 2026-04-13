using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.BigTiff;

/// <summary>Reads BigTIFF images from bytes, streams, or file paths.</summary>
public static class BigTiffReader {

  public static BigTiffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BigTIFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BigTiffFile FromStream(Stream stream) {
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

  public static BigTiffFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BigTiffFile.MinimumFileSize)
      throw new InvalidDataException($"Data too small for BigTIFF: expected at least {BigTiffFile.MinimumFileSize} bytes, got {data.Length}.");

    var isBigEndian = _ReadByteOrder(data);

    ushort version, offsetSize, reserved;
    ulong ifdOffset;

    if (!isBigEndian) {
      var header = BigTiffFileHeader.ReadFrom(data[..BigTiffFileHeader.StructSize]);
      version = header.Version;
      offsetSize = header.OffsetSize;
      reserved = header.Reserved;
      ifdOffset = (ulong)header.FirstIfdOffset;
    } else {
      version = _ReadUInt16(data, 2, true);
      offsetSize = _ReadUInt16(data, 4, true);
      reserved = _ReadUInt16(data, 6, true);
      ifdOffset = _ReadUInt64(data, 8, true);
    }

    if (version != BigTiffFile.Version)
      throw new InvalidDataException($"Invalid BigTIFF version: expected {BigTiffFile.Version}, got {version}.");

    if (offsetSize != BigTiffFile.OffsetSize)
      throw new InvalidDataException($"Invalid BigTIFF offset size: expected {BigTiffFile.OffsetSize}, got {offsetSize}.");

    if (reserved != 0)
      throw new InvalidDataException($"Invalid BigTIFF reserved field: expected 0, got {reserved}.");

    if (ifdOffset == 0 || ifdOffset >= (ulong)data.Length)
      throw new InvalidDataException($"Invalid first IFD offset: {ifdOffset}.");

    var (firstIfd, nextOffset) = _ParseIfd(data, (int)ifdOffset, isBigEndian);

    // Read additional IFDs as pages
    var pages = new List<BigTiffPage>();
    while (nextOffset != 0 && nextOffset < (ulong)data.Length) {
      try {
        var (page, next) = _ParseIfd(data, (int)nextOffset, isBigEndian);
        pages.Add(new BigTiffPage {
          Width = page.Width,
          Height = page.Height,
          SamplesPerPixel = page.SamplesPerPixel,
          BitsPerSample = page.BitsPerSample,
          PhotometricInterpretation = page.PhotometricInterpretation,
          PixelData = page.PixelData,
        });
        nextOffset = next;
      } catch {
        break;
      }
    }

    return new() {
      Width = firstIfd.Width,
      Height = firstIfd.Height,
      SamplesPerPixel = firstIfd.SamplesPerPixel,
      BitsPerSample = firstIfd.BitsPerSample,
      PhotometricInterpretation = firstIfd.PhotometricInterpretation,
      PixelData = firstIfd.PixelData,
      IsBigEndian = isBigEndian,
      Pages = pages,
    };
  }

  public static BigTiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static bool _ReadByteOrder(ReadOnlySpan<byte> data) {
    if (data[0] == 0x49 && data[1] == 0x49)
      return false;
    if (data[0] == 0x4D && data[1] == 0x4D)
      return true;
    throw new InvalidDataException($"Invalid byte order mark: 0x{data[0]:X2}{data[1]:X2}.");
  }

  private static ushort _ReadUInt16(ReadOnlySpan<byte> data, int offset, bool bigEndian) {
    var span = data.Slice(offset, 2);
    return bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> data, int offset, bool bigEndian) {
    var span = data.Slice(offset, 4);
    return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
  }

  private static ulong _ReadUInt64(ReadOnlySpan<byte> data, int offset, bool bigEndian) {
    var span = data.Slice(offset, 8);
    return bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(span) : BinaryPrimitives.ReadUInt64LittleEndian(span);
  }

  private static (BigTiffFile File, ulong NextIfdOffset) _ParseIfd(ReadOnlySpan<byte> data, int ifdOffset, bool isBigEndian) {
    var entryCount = _ReadUInt64(data, ifdOffset, isBigEndian);
    var pos = ifdOffset + 8;

    var width = 0;
    var height = 0;
    var bitsPerSample = 8;
    ushort compression = 1;
    ushort photometric = 1;
    var samplesPerPixel = 1;
    var rowsPerStrip = 0;
    ulong stripOffset = 0;
    ulong stripByteCount = 0;

    for (ulong i = 0; i < entryCount; ++i) {
      var tag = _ReadUInt16(data, pos, isBigEndian);
      var type = _ReadUInt16(data, pos + 2, isBigEndian);
      var count = _ReadUInt64(data, pos + 4, isBigEndian);

      var value = _ReadTagValue(data, pos + 12, type, count, isBigEndian);

      switch (tag) {
        case BigTiffFile.TagImageWidth:
          width = (int)value;
          break;
        case BigTiffFile.TagImageLength:
          height = (int)value;
          break;
        case BigTiffFile.TagBitsPerSample:
          bitsPerSample = (int)value;
          break;
        case BigTiffFile.TagCompression:
          compression = (ushort)value;
          break;
        case BigTiffFile.TagPhotometricInterpretation:
          photometric = (ushort)value;
          break;
        case BigTiffFile.TagSamplesPerPixel:
          samplesPerPixel = (int)value;
          break;
        case BigTiffFile.TagRowsPerStrip:
          rowsPerStrip = (int)value;
          break;
        case BigTiffFile.TagStripOffsets:
          stripOffset = value;
          break;
        case BigTiffFile.TagStripByteCounts:
          stripByteCount = value;
          break;
      }

      pos += 20;
    }

    // Read next IFD offset (after all entries)
    var nextIfdOffset = pos + 8 <= data.Length ? _ReadUInt64(data, pos, isBigEndian) : 0UL;

    if (compression != BigTiffFile.CompressionNone)
      throw new InvalidDataException($"Unsupported compression: {compression}. Only uncompressed (1) is supported.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");

    var bytesPerSample = bitsPerSample > 8 ? 2 : 1;
    var bytesPerPixel = samplesPerPixel * bytesPerSample;
    var expectedDataSize = width * height * bytesPerPixel;

    if (stripOffset == 0)
      throw new InvalidDataException("Missing StripOffsets tag.");

    var actualByteCount = stripByteCount > 0 ? (int)stripByteCount : expectedDataSize;
    var copyLength = Math.Min(actualByteCount, expectedDataSize);
    copyLength = Math.Min(copyLength, data.Length - (int)stripOffset);

    var pixelData = new byte[expectedDataSize];
    if ((int)stripOffset < data.Length)
      data.Slice((int)stripOffset, Math.Max(0, copyLength)).CopyTo(pixelData);

    var file = new BigTiffFile {
      Width = width,
      Height = height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PhotometricInterpretation = photometric,
      PixelData = pixelData,
      IsBigEndian = isBigEndian,
    };

    return (file, nextIfdOffset);
  }

  private static ulong _ReadTagValue(ReadOnlySpan<byte> data, int valueOffset, ushort type, ulong count, bool bigEndian) {
    var typeSize = type switch {
      BigTiffFile.TypeShort => 2UL,
      BigTiffFile.TypeLong => 4UL,
      _ => 8UL,
    };

    // If total value size exceeds 8 bytes, the value field contains an offset to external data
    if (count * typeSize > 8)
      valueOffset = (int)_ReadUInt64(data, valueOffset, bigEndian);

    return type switch {
      BigTiffFile.TypeShort => _ReadUInt16(data, valueOffset, bigEndian),
      BigTiffFile.TypeLong => _ReadUInt32(data, valueOffset, bigEndian),
      BigTiffFile.TypeLong8 => _ReadUInt64(data, valueOffset, bigEndian),
      _ => _ReadUInt64(data, valueOffset, bigEndian),
    };
  }
}
