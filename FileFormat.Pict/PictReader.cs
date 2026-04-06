using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pict;

/// <summary>Reads PICT2 files from bytes, streams, or file paths.</summary>
public static class PictReader {

  private const int _PREAMBLE_SIZE = 512;
  private const int _PICTURE_SIZE_FIELD = 2;
  private const int _BOUNDING_RECT_SIZE = 8;
  private const int _MIN_FILE_SIZE = _PREAMBLE_SIZE + _PICTURE_SIZE_FIELD + _BOUNDING_RECT_SIZE + 2 + 2 + 2; // preamble + size + rect + version opcode + version arg + end

  public static PictFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PICT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PictFile FromStream(Stream stream) {
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

  public static PictFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PictFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid PICT file.");

    var offset = _PREAMBLE_SIZE;

    // Skip 2-byte picture size (unreliable)
    offset += _PICTURE_SIZE_FIELD;

    // Read bounding rect: top, left, bottom, right (int16 BE each)
    var top = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    var left = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    var bottom = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    var right = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
    offset += 2;

    var width = right - left;
    var height = bottom - top;
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid PICT bounding rect: {width}x{height}.");

    // Parse opcodes
    byte[]? pixelData = null;
    byte[]? palette = null;
    var bitsPerPixel = 0;

    while (offset < data.Length) {
      if (offset + 2 > data.Length)
        break;

      var opcode = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
      offset += 2;

      switch ((PictOpcode)opcode) {
        case PictOpcode.EndOfPicture:
          goto done;

        case PictOpcode.Version:
          // Skip version argument (0x02FF)
          offset += 2;
          break;

        case PictOpcode.HeaderOp:
          // Skip 24-byte extended header
          offset += 24;
          break;

        case PictOpcode.DirectBitsRect:
          (pixelData, bitsPerPixel) = _ReadDirectBitsRect(data, ref offset, width, height);
          break;

        case PictOpcode.PackBitsRect:
          (pixelData, palette, bitsPerPixel) = _ReadPackBitsRect(data, ref offset, width, height);
          break;

        default:
          // Unknown opcode, try to skip (PICT2 opcodes are word-aligned)
          // For safety, stop parsing
          goto done;
      }
    }

    done:
    return new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData ?? [],
      Palette = palette
    };
  }

  private static (byte[] pixelData, int bitsPerPixel) _ReadDirectBitsRect(byte[] data, ref int offset, int width, int height) {
    // Skip 4-byte baseAddr
    offset += 4;

    // Read PixMap record (46 bytes)
    var rowBytesRaw = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    var rowBytes = rowBytesRaw & 0x3FFF;
    offset += 2;

    // Skip bounds rect (8), version(2), packType(2), packSize(4),
    // hRes(4), vRes(4), pixelType(2), pixelSize(2), cmpCount(2), cmpSize(2),
    // planeBytes(4), pmTable(4), reserved(4) = 44 bytes
    offset += 44;

    // Skip source rect (8), dest rect (8), transfer mode (2)
    offset += 18;

    // Read PackBits-compressed scanlines with 3 components (R, G, B)
    var pixelData = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      int byteCount;
      if (rowBytes < 8) {
        byteCount = rowBytes;
      } else if (rowBytes < 250) {
        byteCount = data[offset];
        ++offset;
      } else {
        byteCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;
      }

      var compressed = data.AsSpan(offset, byteCount);
      var scanline = _DecompressPackBits(compressed.ToArray(), rowBytes);
      offset += byteCount;

      // Components are stored separately: R then G then B
      var componentStride = rowBytes / 3;
      for (var x = 0; x < width; ++x) {
        var destIdx = (y * width + x) * 3;
        pixelData[destIdx] = x < componentStride ? scanline[x] : (byte)0;
        pixelData[destIdx + 1] = x < componentStride ? scanline[componentStride + x] : (byte)0;
        pixelData[destIdx + 2] = x < componentStride ? scanline[2 * componentStride + x] : (byte)0;
      }
    }

    return (pixelData, 24);
  }

  private static (byte[] pixelData, byte[] palette, int bitsPerPixel) _ReadPackBitsRect(byte[] data, ref int offset, int width, int height) {
    // Read PixMap record
    var rowBytesRaw = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    var rowBytes = rowBytesRaw & 0x3FFF;
    offset += 2;

    // Skip bounds rect (8), version(2), packType(2), packSize(4),
    // hRes(4), vRes(4), pixelType(2), pixelSize(2), cmpCount(2), cmpSize(2),
    // planeBytes(4), pmTable(4), reserved(4) = 44 bytes
    offset += 44;

    // Read color table
    offset += 4; // seed
    offset += 2; // flags
    var ctSize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    var numColors = ctSize + 1;

    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      offset += 2; // index value
      var r = data[offset];
      offset += 2; // R (high byte used, skip low)
      var g = data[offset];
      offset += 2; // G
      var b = data[offset];
      offset += 2; // B
      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
    }

    // Skip source rect (8), dest rect (8), transfer mode (2)
    offset += 18;

    // Read PackBits-compressed indexed scanlines
    var pixelData = new byte[width * height];

    for (var y = 0; y < height; ++y) {
      int byteCount;
      if (rowBytes < 8) {
        byteCount = rowBytes;
      } else if (rowBytes < 250) {
        byteCount = data[offset];
        ++offset;
      } else {
        byteCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;
      }

      var compressed = data.AsSpan(offset, byteCount);
      var scanline = _DecompressPackBits(compressed.ToArray(), rowBytes);
      offset += byteCount;

      for (var x = 0; x < width; ++x)
        pixelData[y * width + x] = x < scanline.Length ? scanline[x] : (byte)0;
    }

    return (pixelData, palette, 8);
  }

  internal static byte[] _DecompressPackBits(byte[] data, int expectedSize) {
    var output = new byte[expectedSize];
    var outIdx = 0;
    var inIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      var header = (sbyte)data[inIdx++];

      if (header >= 0) {
        var count = header + 1;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedSize; ++j)
          output[outIdx++] = data[inIdx++];
      } else if (header != -128) {
        var count = -header + 1;
        if (inIdx >= data.Length)
          continue;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedSize; ++j)
          output[outIdx++] = value;
      }
      // header == -128 (0x80): no-op
    }

    return output;
  }
}
