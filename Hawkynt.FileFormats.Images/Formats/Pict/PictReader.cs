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

  public static PictFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PictFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid PICT file.");

    var offset = _PREAMBLE_SIZE;

    // Skip 2-byte picture size (unreliable)
    offset += _PICTURE_SIZE_FIELD;

    // Read bounding rect: top, left, bottom, right (int16 BE each)
    var top = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
    offset += 2;
    var left = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
    offset += 2;
    var bottom = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
    offset += 2;
    var right = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
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

      var opcode = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
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
          (pixelData, bitsPerPixel) = _ReadDirectBitsRect(data.ToArray(), ref offset, width, height);
          break;

        case PictOpcode.PackBitsRect:
          (pixelData, palette, bitsPerPixel) = _ReadPackBitsRect(data.ToArray(), ref offset, width, height);
          break;

        default:
          if (!_TrySkipOpcode(data, ref offset, opcode))
            goto done;

          break;
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

  /// <summary>Steps over an opcode that is not a picture, so the one that is can still be reached.</summary>
  /// <remarks>
  /// A drawing is a list of commands, and the raster is only one of them — clipping regions, colours
  /// and comments come first in most files. Stopping at the first command it did not recognise, as
  /// this used to, meant a perfectly ordinary file parsed to no pixels at all: the sizes were read,
  /// nothing threw, and the picture came back empty.
  /// <para/>
  /// The sizes below are the ones the format fixes. The ranges after them are its own rule for
  /// anything reserved, which is what makes stepping over an unknown command safe rather than a
  /// guess.
  /// </remarks>
  private static bool _TrySkipOpcode(ReadOnlySpan<byte> data, ref int offset, int opcode) {
    var fixedSize = opcode switch {
      0x0000 or 0x001C or 0x001E => 0,
      0x0004 => 1,
      0x0003 or 0x0005 or 0x000D or 0x0015 or 0x0016 => 2,
      0x0006 or 0x0007 or 0x000B or 0x000C or 0x000E or 0x000F or 0x0021 or 0x0023 => 4,
      0x001A or 0x001B or 0x001D or 0x001F or 0x0022 => 6,
      0x0002 or 0x0009 or 0x000A or 0x0010 or 0x0020 or 0x0030 or 0x0031 or 0x0032 or 0x0033 or 0x0034 => 8,
      _ => -1,
    };

    if (fixedSize >= 0) {
      offset += fixedSize;
      return offset <= data.Length;
    }

    // A word of length that counts itself.
    if (opcode is 0x0001 or 0x0070 or 0x0071 or 0x0072 or 0x0073 or 0x0074 or 0x0075 or 0x0076 or 0x0077) {
      if (offset + 2 > data.Length)
        return false;

      offset += Math.Max(2, (int)BinaryPrimitives.ReadUInt16BigEndian(data[offset..]));
      return offset <= data.Length;
    }

    // A comment: what kind, then how long, then that many bytes.
    if (opcode == 0x00A1) {
      if (offset + 4 > data.Length)
        return false;

      offset += 4 + BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
      return offset <= data.Length;
    }

    var reservedSize = opcode switch {
      >= 0x00A2 and <= 0x00AF => _ReadUInt16Length(data, ref offset),
      >= 0x00B0 and <= 0x00CF => 0,
      >= 0x00D0 and <= 0x00FE => _ReadUInt32Length(data, ref offset),
      >= 0x0100 and <= 0x7FFF => (opcode >> 8) * 2,
      >= 0x8000 and <= 0x80FF => 0,
      >= 0x8100 => _ReadUInt32Length(data, ref offset),
      _ => -1,
    };

    if (reservedSize < 0)
      return false;

    offset += reservedSize;

    return offset <= data.Length;
  }

  private static int _ReadUInt16Length(ReadOnlySpan<byte> data, ref int offset) {
    if (offset + 2 > data.Length)
      return -1;

    var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
    offset += 2;

    return length;
  }

  private static int _ReadUInt32Length(ReadOnlySpan<byte> data, ref int offset) {
    if (offset + 4 > data.Length)
      return -1;

    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    offset += 4;

    return length < 0 ? -1 : length;
  }

  private static (byte[] pixelData, int bitsPerPixel) _ReadDirectBitsRect(byte[] data, ref int offset, int width, int height) {
    // Skip 4-byte baseAddr
    offset += 4;

    // Read PixMap record (46 bytes)
    var rowBytesRaw = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    var rowBytes = rowBytesRaw & 0x3FFF;
    offset += 2;

    // The pixmap states how the row is packed and how many components it holds, and both matter:
    // rowBytes is the width of an unpacked row in memory, four bytes a pixel, while what is stored
    // is cmpCount planes of one byte each. Dividing rowBytes by three to find the planes — which is
    // what this used to do — lands short and leaves the right-hand end of every row black.
    var packType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 10));
    var componentCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 28));

    // bounds rect (8), version(2), packType(2), packSize(4), hRes(4), vRes(4), pixelType(2),
    // pixelSize(2), cmpCount(2), cmpSize(2), planeBytes(4), pmTable(4), reserved(4)
    offset += 44;

    // Skip source rect (8), dest rect (8), transfer mode (2)
    offset += 18;

    if (componentCount is not (3 or 4))
      throw new InvalidDataException($"A PICT raster of {componentCount} components is not one this reads.");

    var pixelData = new byte[width * height * 3];
    var packedRowBytes = componentCount * width;

    for (var y = 0; y < height; ++y) {
      byte[] scanline;

      if (packType == 1 || rowBytes < 8) {
        // Stored as it stands, with no run-length coding at all.
        scanline = data.AsSpan(offset, Math.Min(packedRowBytes, data.Length - offset)).ToArray();
        offset += packedRowBytes;
      } else {
        int byteCount;
        if (rowBytes > 250) {
          byteCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
          offset += 2;
        } else {
          byteCount = data[offset];
          ++offset;
        }

        scanline = _DecompressPackBits(data.AsSpan(offset, byteCount).ToArray(), packedRowBytes);
        offset += byteCount;
      }

      // Within a row the components are planar: every red, then every green, then every blue. When
      // there are four, the first is alpha and the colours follow it.
      var first = componentCount == 4 ? width : 0;
      for (var x = 0; x < width; ++x) {
        var target = (y * width + x) * 3;
        for (var channel = 0; channel < 3; ++channel) {
          var source = first + channel * width + x;
          pixelData[target + channel] = source < scanline.Length ? scanline[source] : (byte)0;
        }
      }
    }

    return (pixelData, 24);
  }

  /// <summary>
  /// Refuses a record that runs past the end of the file instead of reading past it.
  /// </summary>
  /// <remarks>
  /// Every field here was read without asking whether it was there. A QuickDraw picture whose
  /// records this does not follow correctly — and there are several kinds it does not — walked off
  /// the end and threw an index out of range, which tells a caller nothing except that something
  /// inside broke.
  /// </remarks>
  private static void _Need(byte[] data, int offset, int count) {
    if (offset < 0 || count < 0 || offset + count > data.Length)
      throw new InvalidDataException($"A QuickDraw record wants {count} bytes at {offset}; the picture is {data.Length} long.");
  }

  private static (byte[] pixelData, byte[] palette, int bitsPerPixel) _ReadPackBitsRect(byte[] data, ref int offset, int width, int height) {
    // Read PixMap record
    _Need(data, offset, 2 + 44 + 8);
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

    _Need(data, offset, numColors * 8);

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
