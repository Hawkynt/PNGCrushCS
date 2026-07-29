using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Dng;

/// <summary>Reads DNG (Adobe Digital Negative) files from bytes, streams, or file paths.</summary>
public static class DngReader {

  /// <summary>Minimum valid TIFF/DNG file size: 8-byte header + 2-byte IFD count + 4-byte next IFD offset.</summary>
  private const int _MIN_FILE_SIZE = 14;

  private const ushort _TIFF_MAGIC = 42;

  // TIFF tag IDs
  private const ushort _TAG_NEW_SUBFILE_TYPE = 254;
  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC_INTERPRETATION = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;
  private const ushort _TAG_DNG_VERSION = 50706;
  private const ushort _TAG_DNG_BACKWARD_VERSION = 50707;
  private const ushort _TAG_UNIQUE_CAMERA_MODEL = 50708;

  // TIFF field types
  private const ushort _TYPE_BYTE = 1;
  private const ushort _TYPE_ASCII = 2;
  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;

  public static DngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DngFile FromStream(Stream stream) {
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

  public static DngFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid DNG/TIFF file.");

    var bytes = data.ToArray();

    var isLittleEndian = _DetectByteOrder(bytes);
    var magic = _ReadUInt16(bytes, 2, isLittleEndian);
    if (magic != _TIFF_MAGIC)
      throw new InvalidDataException($"Invalid TIFF magic number: expected {_TIFF_MAGIC}, got {magic}.");

    var ifdOffset = (int)_ReadUInt32(bytes, 4, isLittleEndian);
    if (ifdOffset < 8 || ifdOffset + 2 > bytes.Length)
      throw new InvalidDataException($"Invalid first IFD offset: {ifdOffset}.");

    // Walk IFDs to find the full-res image (SubfileType=0) and verify DNGVersion exists
    var hasDngVersion = false;
    byte[]? dngVersion = null;
    var cameraModel = "";
    DngFile? result = null;

    var currentOffset = ifdOffset;
    while (currentOffset != 0) {
      var (file, nextIfd, foundDngVersion, foundDngVersionBytes, foundCameraModel) = _ParseIfd(bytes, currentOffset, isLittleEndian);
      if (foundDngVersion) {
        hasDngVersion = true;
        dngVersion = foundDngVersionBytes;
      }

      if (foundCameraModel.Length > 0)
        cameraModel = foundCameraModel;

      if (file != null && result == null)
        result = file;

      currentOffset = nextIfd;
    }

    if (!hasDngVersion)
      throw new InvalidDataException("No DNGVersion tag found; file is not a valid DNG.");

    if (result == null)
      throw new InvalidDataException("No full-resolution image IFD found in DNG.");

    return new DngFile {
      Width = result.Width,
      Height = result.Height,
      BitsPerSample = result.BitsPerSample,
      SamplesPerPixel = result.SamplesPerPixel,
      PixelData = result.PixelData,
      DngVersion = dngVersion ?? [1, 4, 0, 0],
      CameraModel = cameraModel,
      Photometric = result.Photometric,
    };
  }

  public static DngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
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

  private static (DngFile? file, int nextIfd, bool hasDngVersion, byte[]? dngVersion, string cameraModel) _ParseIfd(byte[] data, int ifdOffset, bool isLittleEndian) {
    var entryCount = _ReadUInt16(data, ifdOffset, isLittleEndian);
    var pos = ifdOffset + 2;

    int width = 0, height = 0, bitsPerSample = 8, compression = 1, samplesPerPixel = 1;
    var photometric = (ushort)2;
    uint subfileType = 0;
    uint[]? stripOffsets = null;
    uint[]? stripByteCounts = null;
    var hasDngVersion = false;
    byte[]? dngVersion = null;
    var cameraModel = "";

    for (var i = 0; i < entryCount; ++i) {
      if (pos + 12 > data.Length)
        throw new InvalidDataException("IFD entry extends beyond file.");

      var tag = _ReadUInt16(data, pos, isLittleEndian);
      var type = _ReadUInt16(data, pos + 2, isLittleEndian);
      var count = _ReadUInt32(data, pos + 4, isLittleEndian);
      var valueOffset = pos + 8;

      switch (tag) {
        case _TAG_NEW_SUBFILE_TYPE:
          subfileType = _ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
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
        case _TAG_PHOTOMETRIC_INTERPRETATION:
          photometric = (ushort)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_SAMPLES_PER_PIXEL:
          samplesPerPixel = (int)_ReadTagValue(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_STRIP_OFFSETS:
          stripOffsets = _ReadTagArray(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_STRIP_BYTE_COUNTS:
          stripByteCounts = _ReadTagArray(data, type, count, valueOffset, isLittleEndian);
          break;
        case _TAG_DNG_VERSION:
          hasDngVersion = true;
          dngVersion = _ReadByteTag(data, count, valueOffset, isLittleEndian);
          break;
        case _TAG_UNIQUE_CAMERA_MODEL:
          cameraModel = _ReadAsciiTag(data, type, count, valueOffset, isLittleEndian);
          break;
      }

      pos += 12;
    }

    // Read next IFD offset
    var nextIfd = 0;
    if (pos + 4 <= data.Length)
      nextIfd = (int)_ReadUInt32(data, pos, isLittleEndian);

    // Only parse pixel data for the full-res IFD (SubfileType=0) with uncompressed data
    DngFile? file = null;
    if (subfileType == 0 && width > 0 && height > 0 && stripOffsets != null && stripByteCounts != null) {
      if (compression != 1)
        throw new InvalidDataException($"Only uncompressed DNG is supported (Compression={compression}).");

      // For CFA (16-bit), the stored data is 16-bit per sample but we convert to 8-bit Gray
      var storedBitsPerSample = bitsPerSample;
      var effectiveBitsPerSample = bitsPerSample;
      var effectiveSamplesPerPixel = samplesPerPixel;
      var isCfa = photometric == 32803;

      if (isCfa && bitsPerSample == 16) {
        effectiveBitsPerSample = 8;
        effectiveSamplesPerPixel = 1;
      }

      var bytesPerPixel = samplesPerPixel * (storedBitsPerSample / 8);
      var totalSourceBytes = width * height * bytesPerPixel;
      var sourceData = new byte[totalSourceBytes];
      var destOffset = 0;

      for (var s = 0; s < stripOffsets.Length; ++s) {
        var srcOffset = (int)stripOffsets[s];
        var byteCount = (int)stripByteCounts[s];
        var toCopy = Math.Min(byteCount, totalSourceBytes - destOffset);
        if (toCopy <= 0)
          break;
        if (srcOffset + toCopy > data.Length)
          throw new InvalidDataException($"Strip {s} extends beyond file (offset={srcOffset}, count={toCopy}, file={data.Length}).");

        data.AsSpan(srcOffset, toCopy).CopyTo(sourceData.AsSpan(destOffset));
        destOffset += toCopy;
      }

      byte[] pixelData;
      if (isCfa && storedBitsPerSample == 16) {
        // Convert 16-bit CFA to 8-bit grayscale by taking MSB
        var pixelCount = width * height;
        pixelData = new byte[pixelCount];
        for (var p = 0; p < pixelCount; ++p) {
          var offset = p * 2;
          if (offset + 1 < sourceData.Length) {
            // Take MSB based on byte order
            pixelData[p] = isLittleEndian ? sourceData[offset + 1] : sourceData[offset];
          }
        }
      } else {
        pixelData = sourceData;
      }

      var photometricEnum = photometric switch {
        1 => DngPhotometric.BlackIsZero,
        2 => DngPhotometric.Rgb,
        32803 => DngPhotometric.Cfa,
        _ => (DngPhotometric)photometric,
      };

      file = new DngFile {
        Width = width,
        Height = height,
        BitsPerSample = effectiveBitsPerSample,
        SamplesPerPixel = effectiveSamplesPerPixel,
        PixelData = pixelData,
        Photometric = photometricEnum,
      };
    }

    return (file, nextIfd, hasDngVersion, dngVersion, cameraModel);
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

  private static byte[] _ReadByteTag(byte[] data, uint count, int valueOffset, bool isLittleEndian) {
    int dataOffset;
    if (count <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)_ReadUInt32(data, valueOffset, isLittleEndian);

    var result = new byte[count];
    data.AsSpan(dataOffset, (int)count).CopyTo(result.AsSpan(0));
    return result;
  }

  private static string _ReadAsciiTag(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (count == 0)
      return "";

    int dataOffset;
    if (count <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)_ReadUInt32(data, valueOffset, isLittleEndian);

    // ASCII strings in TIFF are null-terminated
    var length = (int)count;
    if (dataOffset + length > data.Length)
      length = data.Length - dataOffset;

    var str = Encoding.ASCII.GetString(data, dataOffset, length);
    // Remove trailing null characters
    var nullIndex = str.IndexOf('\0');
    if (nullIndex >= 0)
      str = str[..nullIndex];

    return str;
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
