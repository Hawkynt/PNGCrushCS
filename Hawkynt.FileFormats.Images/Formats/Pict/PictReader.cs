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

        default: {
          // A picture is a drawing, and the image is one instruction in it. Every real PICT sets a
          // clipping region first, so giving up at the first unrecognised opcode meant giving up
          // before ever reaching the pixels — which is why these came out blank. Anything whose
          // length is known gets stepped over; anything else still stops, because a wrong guess
          // would put the next read in the middle of an opcode rather than at the start of one.
          var dataLength = _OpcodeDataLength(opcode, data, offset);
          if (dataLength < 0)
            goto done;

          offset += dataLength;
          break;
        }
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

  /// <summary>
  /// How many bytes of data follow an opcode we do not act on, or -1 when its length is not known.
  /// </summary>
  /// <remarks>
  /// The sizes are QuickDraw's, from the picture opcode table. Most are fixed; regions and polygons
  /// begin with their own total length, and comments and font names give a byte count. The verbs
  /// that draw shapes come in runs of eight — one per shape, frame through fill — which is why they
  /// are written here as ranges rather than one line each.
  /// </remarks>
  private static int _OpcodeDataLength(ushort opcode, ReadOnlySpan<byte> data, int offset) {
    // A region or polygon opens with a 16-bit total length that counts itself.
    var counted = offset + 2 <= data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : -1;

    switch (opcode) {
      case 0x0000: return 0;  // NOP
      case 0x0001: return counted; // ClipRgn
      case 0x0002: return 8;  // BkPat
      case 0x0003: return 2;  // TxFont
      case 0x0004: return 1;  // TxFace
      case 0x0005: return 2;  // TxMode
      case 0x0006: return 4;  // SpExtra
      case 0x0007: return 4;  // PnSize
      case 0x0008: return 2;  // PnMode
      case 0x0009: return 8;  // PnPat
      case 0x000A: return 8;  // FillPat
      case 0x000B: return 4;  // OvSize
      case 0x000C: return 4;  // Origin
      case 0x000D: return 2;  // TxSize
      case 0x000E: return 4;  // FgColor
      case 0x000F: return 4;  // BkColor
      case 0x0010: return 8;  // TxRatio
      case 0x0015: return 2;  // PnLocHFrac
      case 0x0016: return 2;  // ChExtra
      case 0x001A: return 6;  // RGBFgCol
      case 0x001B: return 6;  // RGBBkCol
      case 0x001C: return 0;  // HiliteMode
      case 0x001D: return 6;  // HiliteColor
      case 0x001E: return 0;  // DefHilite
      case 0x001F: return 6;  // OpColor
      case 0x0020: return 8;  // Line
      case 0x0021: return 4;  // LineFrom
      case 0x0022: return 6;  // ShortLine
      case 0x0023: return 2;  // ShortLineFrom
      case 0x002C: return offset + 2 <= data.Length ? 2 + BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : -1; // fontName
      case 0x00A0: return 2;  // ShortComment
      case 0x00A1: // LongComment: a two-byte kind, then a counted payload
        return offset + 4 <= data.Length ? 4 + BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]) : -1;
      default:
        return opcode switch {
          >= 0x0030 and <= 0x0037 => 8,   // frame..fill Rect
          >= 0x0038 and <= 0x003F => 0,   // the same Rect again
          >= 0x0040 and <= 0x0047 => 8,   // RRect
          >= 0x0048 and <= 0x004F => 0,
          >= 0x0050 and <= 0x0057 => 8,   // Oval
          >= 0x0058 and <= 0x005F => 0,
          >= 0x0060 and <= 0x0067 => 12,  // Arc, which adds a start angle and an extent
          >= 0x0068 and <= 0x006F => 4,   // the same Arc, new angles
          >= 0x0070 and <= 0x0077 => counted, // Poly
          >= 0x0078 and <= 0x007F => 0,
          >= 0x0080 and <= 0x0087 => counted, // Rgn
          >= 0x0088 and <= 0x008F => 0,
          >= 0x00B0 and <= 0x00CF => 0,   // reserved, and empty
          _ => -1
        };
    }
  }

  /// <summary>Reads a DirectBitsRect opcode: a pixmap that carries its colours rather than indices.</summary>
  /// <remarks>
  /// How long a decompressed row is, and how far apart the colour planes sit inside it, comes from
  /// <c>cmpCount</c> and <c>pixelSize</c> — two of the fields that were being skipped over unread.
  /// A 32-bit pixmap reserves four bytes a pixel in <c>rowBytes</c> but writes only the three planes
  /// it uses, so a 40-pixel row unpacks to 120 bytes and not the 160 <c>rowBytes</c> claims. Reading
  /// it as 160 with the planes a third of that apart put every plane in the wrong place and ran the
  /// row off its own end, which is why these arrived blank.
  /// </remarks>
  private static (byte[] pixelData, int bitsPerPixel) _ReadDirectBitsRect(byte[] data, ref int offset, int width, int height) {
    // Skip 4-byte baseAddr
    offset += 4;

    // PixMap record, 46 bytes
    var rowBytes = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset)) & 0x3FFF;
    offset += 2;
    offset += 10; // bounds rect (8), pmVersion (2)
    var packType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    offset += 14; // packSize (4), hRes (4), vRes (4), pixelType (2)
    var pixelSize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    var componentCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    offset += 2;
    offset += 14; // cmpSize (2), planeBytes (4), pmTable (4), pmReserved (4)

    // Skip source rect (8), dest rect (8), transfer mode (2)
    offset += 18;

    // 32-bit pixels are stored a plane at a time — every red, then every green, then every blue —
    // so a row is as long as its planes, not as long as the space reserved for it.
    var planes = componentCount is 3 or 4 ? componentCount : 3;
    var unpackedRow = pixelSize == 32 ? planes * width : rowBytes;

    var pixelData = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      byte[] scanline;

      // Pack types 1 and 2 say the row is not compressed at all; otherwise it carries its own length.
      if (packType is 1 or 2 || rowBytes < 8) {
        var take = Math.Min(unpackedRow, data.Length - offset);
        scanline = new byte[unpackedRow];
        data.AsSpan(offset, Math.Max(take, 0)).CopyTo(scanline);
        offset += unpackedRow;
      } else {
        int byteCount;
        if (rowBytes > 250) {
          byteCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
          offset += 2;
        } else {
          byteCount = data[offset];
          ++offset;
        }

        byteCount = Math.Min(byteCount, data.Length - offset);
        scanline = _DecompressPackBits(data.AsSpan(offset, byteCount).ToArray(), unpackedRow);
        offset += byteCount;
      }

      _PlaceDirectRow(scanline, pixelData, y, width, pixelSize, planes);
    }

    return (pixelData, 24);
  }

  /// <summary>Turns one decompressed row into RGB triples.</summary>
  /// <remarks>
  /// A four-component pixmap leads with alpha, so the colours are the last three planes either way.
  /// A 16-bit one is not planar at all: each pixel is one big-endian word of five bits a channel.
  /// </remarks>
  private static void _PlaceDirectRow(byte[] scanline, byte[] pixelData, int y, int width, int pixelSize, int planes) {
    var destRow = y * width * 3;

    if (pixelSize == 16) {
      for (var x = 0; x < width; ++x) {
        var at = x * 2;
        var word = at + 1 < scanline.Length ? (scanline[at] << 8) | scanline[at + 1] : 0;
        var destIdx = destRow + (x * 3);
        pixelData[destIdx] = (byte)(((word >> 10) & 0x1F) * 255 / 31);
        pixelData[destIdx + 1] = (byte)(((word >> 5) & 0x1F) * 255 / 31);
        pixelData[destIdx + 2] = (byte)((word & 0x1F) * 255 / 31);
      }

      return;
    }

    var firstColourPlane = (planes - 3) * width;
    for (var x = 0; x < width; ++x) {
      var destIdx = destRow + (x * 3);
      for (var channel = 0; channel < 3; ++channel) {
        var at = firstColourPlane + (channel * width) + x;
        pixelData[destIdx + channel] = at < scanline.Length ? scanline[at] : (byte)0;
      }
    }
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
