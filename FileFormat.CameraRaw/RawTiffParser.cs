using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.CameraRaw;

/// <summary>Internal minimal TIFF IFD parser for extracting preview images and metadata from Camera RAW files.</summary>
internal static class RawTiffParser {

  // TIFF tag IDs
  internal const ushort TAG_IMAGE_WIDTH = 256;
  internal const ushort TAG_IMAGE_LENGTH = 257;
  internal const ushort TAG_BITS_PER_SAMPLE = 258;
  internal const ushort TAG_COMPRESSION = 259;
  internal const ushort TAG_PHOTOMETRIC_INTERPRETATION = 262;
  internal const ushort TAG_MAKE = 271;
  internal const ushort TAG_MODEL = 272;
  internal const ushort TAG_STRIP_OFFSETS = 273;
  internal const ushort TAG_SAMPLES_PER_PIXEL = 277;
  internal const ushort TAG_ROWS_PER_STRIP = 278;
  internal const ushort TAG_STRIP_BYTE_COUNTS = 279;
  internal const ushort TAG_SUB_IFDS = 330;

  // CFA / DNG tag IDs
  internal const ushort TAG_CFA_REPEAT_PATTERN_DIM = 33421;
  internal const ushort TAG_CFA_PATTERN = 33422;
  internal const ushort TAG_DNG_VERSION = 50706;
  internal const ushort TAG_BLACK_LEVEL = 50714;
  internal const ushort TAG_WHITE_LEVEL = 50717;
  internal const ushort TAG_COLOR_MATRIX_1 = 50721;
  internal const ushort TAG_AS_SHOT_NEUTRAL = 50728;

  // Canon CR2 slice info tag
  internal const ushort TAG_CANON_SLICE_INFO = 0xC640;

  // TIFF field types
  internal const ushort TYPE_BYTE = 1;
  internal const ushort TYPE_ASCII = 2;
  internal const ushort TYPE_SHORT = 3;
  internal const ushort TYPE_LONG = 4;
  internal const ushort TYPE_RATIONAL = 5;
  internal const ushort TYPE_SRATIONAL = 10;

  /// <summary>Parsed IFD image descriptor.</summary>
  internal sealed class IfdImage {
    public int Width;
    public int Height;
    public int BitsPerSample = 8;
    public int Compression = 1;
    public int SamplesPerPixel = 1;
    public int PhotometricInterpretation = -1;
    public uint[]? StripOffsets;
    public uint[]? StripByteCounts;

    // CFA / DNG fields
    public byte[]? CfaPattern;
    public int CfaRepeatCols;
    public int CfaRepeatRows;
    public int[]? BlackLevel;
    public int WhiteLevel;
    public float[]? AsShotNeutral;
    public float[]? ColorMatrix1;

    // Canon CR2 slice info
    public int[]? SliceInfo;

    public bool HasCfa => CfaPattern is { Length: >= 4 } && CfaRepeatCols == 2 && CfaRepeatRows == 2;
  }

  /// <summary>Detects byte order from the TIFF header.</summary>
  internal static bool DetectByteOrder(byte[] data) {
    var b0 = (char)data[0];
    var b1 = (char)data[1];
    if (b0 == 'I' && b1 == 'I')
      return true;
    if (b0 == 'M' && b1 == 'M')
      return false;

    throw new System.IO.InvalidDataException($"Invalid TIFF byte order marker: 0x{data[0]:X2} 0x{data[1]:X2}.");
  }

  /// <summary>Validates the TIFF magic number (42).</summary>
  internal static void ValidateMagic(byte[] data, bool isLittleEndian) {
    var magic = ReadUInt16(data, 2, isLittleEndian);
    if (magic != 42)
      throw new System.IO.InvalidDataException($"Invalid TIFF magic number: expected 42, got {magic}.");
  }

  /// <summary>Reads the first IFD offset from the TIFF header.</summary>
  internal static int ReadFirstIfdOffset(byte[] data, bool isLittleEndian) {
    var offset = (int)ReadUInt32(data, 4, isLittleEndian);
    if (offset < 8 || offset + 2 > data.Length)
      throw new System.IO.InvalidDataException($"Invalid first IFD offset: {offset}.");

    return offset;
  }

  /// <summary>Parses all IFDs in a chain and returns them along with extracted metadata.</summary>
  internal static (List<IfdImage> Images, string Make, string Model, bool HasDngVersion) ParseAllIfds(byte[] data, int firstIfdOffset, bool isLittleEndian) {
    var images = new List<IfdImage>();
    var make = "";
    var model = "";
    var hasDngVersion = false;
    var currentOffset = firstIfdOffset;
    var visited = new HashSet<int>();

    while (currentOffset > 0 && currentOffset + 2 <= data.Length && visited.Add(currentOffset)) {
      var entryCount = ReadUInt16(data, currentOffset, isLittleEndian);
      var pos = currentOffset + 2;

      var ifd = new IfdImage();
      var hasImageWidth = false;

      for (var i = 0; i < entryCount; ++i) {
        if (pos + 12 > data.Length)
          break;

        var tag = ReadUInt16(data, pos, isLittleEndian);
        var type = ReadUInt16(data, pos + 2, isLittleEndian);
        var count = ReadUInt32(data, pos + 4, isLittleEndian);
        var valueOffset = pos + 8;

        switch (tag) {
          case TAG_IMAGE_WIDTH:
            ifd.Width = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            hasImageWidth = true;
            break;
          case TAG_IMAGE_LENGTH:
            ifd.Height = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_BITS_PER_SAMPLE:
            ifd.BitsPerSample = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_COMPRESSION:
            ifd.Compression = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_PHOTOMETRIC_INTERPRETATION:
            ifd.PhotometricInterpretation = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_SAMPLES_PER_PIXEL:
            ifd.SamplesPerPixel = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_STRIP_OFFSETS:
            ifd.StripOffsets = ReadTagArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_STRIP_BYTE_COUNTS:
            ifd.StripByteCounts = ReadTagArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_MAKE:
            make = ReadAsciiTag(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_MODEL:
            model = ReadAsciiTag(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_DNG_VERSION:
            hasDngVersion = true;
            break;
          case TAG_CFA_REPEAT_PATTERN_DIM: {
            var dims = ReadTagArray(data, type, count, valueOffset, isLittleEndian);
            if (dims.Length >= 2) {
              ifd.CfaRepeatCols = (int)dims[0];
              ifd.CfaRepeatRows = (int)dims[1];
            }

            break;
          }
          case TAG_CFA_PATTERN:
            ifd.CfaPattern = ReadByteArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_BLACK_LEVEL:
            ifd.BlackLevel = ReadIntArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_WHITE_LEVEL:
            ifd.WhiteLevel = (int)ReadTagValue(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_AS_SHOT_NEUTRAL:
            ifd.AsShotNeutral = ReadRationalArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_COLOR_MATRIX_1:
            ifd.ColorMatrix1 = ReadSignedRationalArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_CANON_SLICE_INFO:
            ifd.SliceInfo = ReadIntArray(data, type, count, valueOffset, isLittleEndian);
            break;
          case TAG_SUB_IFDS: {
            var subIfdOffsets = ReadTagArray(data, type, count, valueOffset, isLittleEndian);
            foreach (var subOffset in subIfdOffsets) {
              var subOff = (int)subOffset;
              if (subOff > 0 && subOff + 2 <= data.Length) {
                var (subImages, _, _, _) = ParseAllIfds(data, subOff, isLittleEndian);
                images.AddRange(subImages);
              }
            }

            break;
          }
        }

        pos += 12;
      }

      if (hasImageWidth && ifd.Width > 0 && ifd.Height > 0)
        images.Add(ifd);

      // Read next IFD offset
      var nextIfdPos = currentOffset + 2 + entryCount * 12;
      if (nextIfdPos + 4 <= data.Length)
        currentOffset = (int)ReadUInt32(data, nextIfdPos, isLittleEndian);
      else
        currentOffset = 0;
    }

    return (images, make, model, hasDngVersion);
  }

  /// <summary>Extracts pixel data from strips into a contiguous byte array.</summary>
  internal static byte[] ExtractStripData(byte[] data, uint[] stripOffsets, uint[] stripByteCounts, int expectedSize) {
    var pixelData = new byte[expectedSize];
    var destOffset = 0;

    for (var s = 0; s < stripOffsets.Length; ++s) {
      var srcOffset = (int)stripOffsets[s];
      var byteCount = s < stripByteCounts.Length ? (int)stripByteCounts[s] : 0;
      var toCopy = Math.Min(byteCount, expectedSize - destOffset);
      if (toCopy <= 0)
        break;
      if (srcOffset + toCopy > data.Length)
        toCopy = Math.Max(0, data.Length - srcOffset);
      if (toCopy <= 0)
        break;

      Array.Copy(data, srcOffset, pixelData, destOffset, toCopy);
      destOffset += toCopy;
    }

    return pixelData;
  }

  /// <summary>Identifies the camera manufacturer from the TIFF Make tag string.</summary>
  internal static CameraRawManufacturer IdentifyManufacturer(string make, byte[] data) {
    if (string.IsNullOrEmpty(make))
      return _IdentifyManufacturerFromSignature(data);

    var upper = make.ToUpperInvariant();
    if (upper.Contains("CANON"))
      return CameraRawManufacturer.Canon;
    if (upper.Contains("NIKON"))
      return CameraRawManufacturer.Nikon;
    if (upper.Contains("SONY"))
      return CameraRawManufacturer.Sony;
    if (upper.Contains("OLYMPUS"))
      return CameraRawManufacturer.Olympus;
    if (upper.Contains("PANASONIC"))
      return CameraRawManufacturer.Panasonic;
    if (upper.Contains("PENTAX") || upper.Contains("RICOH"))
      return CameraRawManufacturer.Pentax;
    if (upper.Contains("FUJI"))
      return CameraRawManufacturer.Fujifilm;
    if (upper.Contains("SAMSUNG"))
      return CameraRawManufacturer.Samsung;

    return CameraRawManufacturer.Generic;
  }

  private static CameraRawManufacturer _IdentifyManufacturerFromSignature(byte[] data) {
    // Check for CR2 signature at offset 8
    if (data.Length >= 10 && data[8] == (byte)'C' && data[9] == (byte)'R')
      return CameraRawManufacturer.Canon;

    return CameraRawManufacturer.Unknown;
  }

  internal static ushort ReadUInt16(byte[] data, int offset, bool isLittleEndian) =>
    isLittleEndian
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
      : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

  internal static uint ReadUInt32(byte[] data, int offset, bool isLittleEndian) =>
    isLittleEndian
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
      : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

  internal static uint ReadTagValue(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (count == 1) {
      return type switch {
        TYPE_SHORT => ReadUInt16(data, valueOffset, isLittleEndian),
        TYPE_LONG => ReadUInt32(data, valueOffset, isLittleEndian),
        _ => ReadUInt32(data, valueOffset, isLittleEndian)
      };
    }

    var dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);
    if (dataOffset < 0 || dataOffset >= data.Length)
      return 0;

    return type switch {
      TYPE_SHORT => ReadUInt16(data, dataOffset, isLittleEndian),
      TYPE_LONG => ReadUInt32(data, dataOffset, isLittleEndian),
      _ => ReadUInt32(data, dataOffset, isLittleEndian)
    };
  }

  internal static uint[] ReadTagArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    var elementSize = type == TYPE_SHORT ? 2 : 4;
    var totalBytes = count * (uint)elementSize;

    int dataOffset;
    if (totalBytes <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);

    if (dataOffset < 0 || dataOffset >= data.Length)
      return [];

    var result = new uint[count];
    for (var i = 0; i < count; ++i) {
      var off = dataOffset + i * elementSize;
      if (off + elementSize > data.Length)
        break;
      result[i] = type == TYPE_SHORT
        ? ReadUInt16(data, off, isLittleEndian)
        : ReadUInt32(data, off, isLittleEndian);
    }

    return result;
  }

  internal static string ReadAsciiTag(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (type != TYPE_ASCII || count == 0)
      return "";

    int dataOffset;
    if (count <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);

    if (dataOffset < 0 || dataOffset + count > data.Length)
      return "";

    var len = (int)count;
    // Trim trailing null
    while (len > 0 && data[dataOffset + len - 1] == 0)
      --len;

    return Encoding.ASCII.GetString(data, dataOffset, len);
  }

  /// <summary>Read a byte array tag (TYPE_BYTE or TYPE_UNDEFINED).</summary>
  internal static byte[] ReadByteArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    int dataOffset;
    if (count <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);

    if (dataOffset < 0 || dataOffset + count > data.Length)
      return [];

    var result = new byte[count];
    Array.Copy(data, dataOffset, result, 0, (int)count);
    return result;
  }

  /// <summary>Read an array of integer values from various TIFF types (BYTE/SHORT/LONG/RATIONAL).</summary>
  internal static int[] ReadIntArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (type == TYPE_RATIONAL || type == TYPE_SRATIONAL) {
      var rationals = type == TYPE_SRATIONAL
        ? ReadSignedRationalArray(data, type, count, valueOffset, isLittleEndian)
        : ReadRationalArray(data, type, count, valueOffset, isLittleEndian);
      if (rationals == null)
        return [];

      var result = new int[rationals.Length];
      for (var i = 0; i < rationals.Length; ++i)
        result[i] = (int)(rationals[i] + 0.5f);
      return result;
    }

    var arr = ReadTagArray(data, type, count, valueOffset, isLittleEndian);
    var intResult = new int[arr.Length];
    for (var i = 0; i < arr.Length; ++i)
      intResult[i] = (int)arr[i];
    return intResult;
  }

  /// <summary>Read an array of RATIONAL (unsigned numerator/denominator) values as floats.</summary>
  internal static float[]? ReadRationalArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (count == 0)
      return null;

    var totalBytes = count * 8u; // each RATIONAL is 8 bytes (2 x uint32)
    int dataOffset;
    if (totalBytes <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);

    if (dataOffset < 0 || dataOffset + totalBytes > data.Length)
      return null;

    var result = new float[count];
    for (var i = 0; i < count; ++i) {
      var off = dataOffset + i * 8;
      var num = ReadUInt32(data, off, isLittleEndian);
      var den = ReadUInt32(data, off + 4, isLittleEndian);
      result[i] = den == 0 ? 0f : (float)num / den;
    }

    return result;
  }

  /// <summary>Read an array of SRATIONAL (signed numerator/denominator) values as floats.</summary>
  internal static float[]? ReadSignedRationalArray(byte[] data, ushort type, uint count, int valueOffset, bool isLittleEndian) {
    if (count == 0)
      return null;

    var totalBytes = count * 8u;
    int dataOffset;
    if (totalBytes <= 4)
      dataOffset = valueOffset;
    else
      dataOffset = (int)ReadUInt32(data, valueOffset, isLittleEndian);

    if (dataOffset < 0 || dataOffset + totalBytes > data.Length)
      return null;

    var result = new float[count];
    for (var i = 0; i < count; ++i) {
      var off = dataOffset + i * 8;
      var num = ReadInt32(data, off, isLittleEndian);
      var den = ReadInt32(data, off + 4, isLittleEndian);
      result[i] = den == 0 ? 0f : (float)num / den;
    }

    return result;
  }

  internal static int ReadInt32(byte[] data, int offset, bool isLittleEndian) =>
    isLittleEndian
      ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset))
      : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));

  /// <summary>Read raw 16-bit sensor data from strips.</summary>
  internal static ushort[] ExtractStripDataUInt16(byte[] data, uint[] stripOffsets, uint[] stripByteCounts, int pixelCount, int bitsPerSample, bool isLittleEndian) {
    var totalBytes = pixelCount * ((bitsPerSample + 7) / 8);
    if (bitsPerSample > 8)
      totalBytes = pixelCount * 2; // 16-bit storage for >8-bit data

    var rawBytes = ExtractStripData(data, stripOffsets, stripByteCounts, totalBytes);
    var result = new ushort[pixelCount];

    if (bitsPerSample <= 8) {
      for (var i = 0; i < pixelCount && i < rawBytes.Length; ++i)
        result[i] = rawBytes[i];
    } else if (bitsPerSample <= 16) {
      for (var i = 0; i < pixelCount; ++i) {
        var off = i * 2;
        if (off + 2 > rawBytes.Length)
          break;
        result[i] = isLittleEndian
          ? BinaryPrimitives.ReadUInt16LittleEndian(rawBytes.AsSpan(off))
          : BinaryPrimitives.ReadUInt16BigEndian(rawBytes.AsSpan(off));
      }
    }

    return result;
  }
}
