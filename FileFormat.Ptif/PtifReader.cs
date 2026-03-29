using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Ptif;

/// <summary>Reads PTIF (Pyramid TIFF) files from bytes, streams, or file paths. Only the first IFD is read.</summary>
public static class PtifReader {

  /// <summary>Minimum valid TIFF file size: 8-byte header + 2-byte IFD count + 4-byte next IFD offset.</summary>
  private const int _MIN_FILE_SIZE = 14;

  private const ushort _TIFF_MAGIC = 42;

  // TIFF tag IDs
  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC_INTERPRETATION = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;

  // TIFF field types
  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;

  public static PtifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PTIF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PtifFile FromStream(Stream stream) {
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

  public static PtifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid PTIF/TIFF file.");

    var isLittleEndian = _DetectByteOrder(data);
    var magic = _ReadUInt16(data, 2, isLittleEndian);
    if (magic != _TIFF_MAGIC)
      throw new InvalidDataException($"Invalid TIFF magic number: expected {_TIFF_MAGIC}, got {magic}.");

    var ifdOffset = (int)_ReadUInt32(data, 4, isLittleEndian);
    if (ifdOffset < 8 || ifdOffset + 2 > data.Length)
      throw new InvalidDataException($"Invalid first IFD offset: {ifdOffset}.");

    return _ParseIfd(data, ifdOffset, isLittleEndian);
  }

  private static bool _DetectByteOrder(byte[] data) {
    var bom = (char)data[0];
    var bom2 = (char)data[1];
    if (bom == 'I' && bom2 == 'I')
      return true;
    if (bom == 'M' && bom2 == 'M')
      return false;

    throw new InvalidDataException($"Invalid TIFF byte order marker: 0x{data[0]:X2} 0x{data[1]:X2}.");
  }

  private static PtifFile _ParseIfd(byte[] data, int ifdOffset, bool isLittleEndian) {
    var entryCount = _ReadUInt16(data, ifdOffset, isLittleEndian);
    var pos = ifdOffset + 2;

    int width = 0, height = 0, bitsPerSample = 8, compression = 1, samplesPerPixel = 1;
    uint[]? stripOffsets = null;
    uint[]? stripByteCounts = null;
    var rowsPerStrip = int.MaxValue;

    for (var i = 0; i < entryCount; ++i) {
      if (pos + 12 > data.Length)
        throw new InvalidDataException("IFD entry extends beyond file.");

      var tag = _ReadUInt16(data, pos, isLittleEndian);
      var type = _ReadUInt16(data, pos + 2, isLittleEndian);
      var count = _ReadUInt32(data, pos + 4, isLittleEndian);
      var valueOffset = pos + 8;

      switch (tag) {
        case _TAG_IMAGE_WIDTH:
          width = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_IMAGE_LENGTH:
          height = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_BITS_PER_SAMPLE:
          bitsPerSample = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_COMPRESSION:
          compression = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_SAMPLES_PER_PIXEL:
          samplesPerPixel = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_ROWS_PER_STRIP:
          rowsPerStrip = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_STRIP_OFFSETS:
          stripOffsets = _ReadTagArray(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_STRIP_BYTE_COUNTS:
          stripByteCounts = _ReadTagArray(data, type, count, valueOffset, isLittleEndian);
          break;
      }

      pos += 12;
    }

    if (compression != 1)
      throw new InvalidDataException($"Only uncompressed PTIF is supported (Compression={compression}).");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid PTIF dimensions: {width}x{height}.");

    if (stripOffsets == null || stripOffsets.Length == 0)
      throw new InvalidDataException("Missing StripOffsets tag.");

    if (stripByteCounts == null || stripByteCounts.Length == 0)
      throw new InvalidDataException("Missing StripByteCounts tag.");

    var bytesPerPixel = samplesPerPixel * (bitsPerSample / 8);
    var totalPixelBytes = width * height * bytesPerPixel;
    var pixelData = new byte[totalPixelBytes];
    var destOffset = 0;

    for (var s = 0; s < stripOffsets.Length; ++s) {
      var srcOffset = (int)stripOffsets[s];
      var byteCount = (int)stripByteCounts[s];
      var toCopy = Math.Min(byteCount, totalPixelBytes - destOffset);
      if (toCopy <= 0)
        break;
      if (srcOffset + toCopy > data.Length)
        throw new InvalidDataException($"Strip {s} extends beyond file (offset={srcOffset}, count={toCopy}, file={data.Length}).");

      data.AsSpan(srcOffset, toCopy).CopyTo(pixelData.AsSpan(destOffset));
      destOffset += toCopy;
    }

    return new PtifFile {
      Width = width,
      Height = height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = pixelData
    };
  }

  private static uint _ReadTagValue(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (count == 1) {
      return type switch {
        _TYPE_SHORT => _ReadUInt16(data, valueOffset, isLittleEndian),
        _TYPE_LONG => _ReadUInt32(data, valueOffset, isLittleEndian),
        _ => _ReadUInt32(data, valueOffset, isLittleEndian)
      };
    }

    // For multi-value tags with count > 1, read first value (used for BitsPerSample)
    var dataOffset = (int)_ReadUInt32(data, valueOffset, isLittleEndian);
    return type switch {
      _TYPE_SHORT => _ReadUInt16(data, dataOffset, isLittleEndian),
      _TYPE_LONG => _ReadUInt32(data, dataOffset, isLittleEndian),
      _ => _ReadUInt32(data, dataOffset, isLittleEndian)
    };
  }

  private static uint[] _ReadTagArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    var elementSize = type == _TYPE_SHORT ? 2 : 4;
    var totalBytes = count * (uint)elementSize;

    int dataOffset;
    if (totalBytes <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)_ReadUInt32(data, valueOffset, isLittleEndian);

    var result = new uint[count];
    for (var i = 0; i < count; ++i) {
      result[i] = type == _TYPE_SHORT
        ? _ReadUInt16(data, dataOffset + i * elementSize, isLittleEndian)
        : _ReadUInt32(data, dataOffset + i * elementSize, isLittleEndian);
    }

    return result;
  }

  private static ushort _ReadUInt16(byte[] data, int offset, bool isLittleEndian) =>
    isLittleEndian
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
      : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

  private static uint _ReadUInt32(byte[] data, int offset, bool isLittleEndian) =>
    isLittleEndian
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
      : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
