using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Pict;

/// <summary>Assembles PICT2 file bytes from pixel data.</summary>
public static class PictWriter {

  public static byte[] ToBytes(PictFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // 1. 512-byte preamble (all zeros)
    ms.Write(new byte[512]);

    // 2. 2-byte picture size (0, unreliable)
    _WriteUInt16BE(ms, 0);

    // 3. 8-byte bounding rect: top=0, left=0, bottom=height, right=width
    _WriteInt16BE(ms, 0); // top
    _WriteInt16BE(ms, 0); // left
    _WriteInt16BE(ms, (short)file.Height); // bottom
    _WriteInt16BE(ms, (short)file.Width); // right

    // 4. Version opcode (0x0011) + version 2 argument (0x02FF)
    _WriteUInt16BE(ms, (ushort)PictOpcode.Version);
    _WriteUInt16BE(ms, 0x02FF);

    // 5. Header opcode (0x0C00) + 24 bytes extended header
    _WriteUInt16BE(ms, (ushort)PictOpcode.HeaderOp);
    ms.WriteByte(0xFF);
    ms.WriteByte(0xFE);
    _WriteUInt16BE(ms, 0); // reserved
    _WriteUInt32BE(ms, 0x00480000); // hRes 72 dpi fixed-point
    _WriteUInt32BE(ms, 0x00480000); // vRes 72 dpi fixed-point
    // optimal source rect: top=0, left=0, bottom=height, right=width
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, (short)file.Height);
    _WriteInt16BE(ms, (short)file.Width);
    _WriteUInt32BE(ms, 0); // reserved

    // 6. Raster opcode
    if (file.BitsPerPixel == 8 && file.Palette != null)
      _WritePackBitsRect(ms, file);
    else
      _WriteDirectBitsRect(ms, file);

    // 7. EndOfPicture
    _WriteUInt16BE(ms, (ushort)PictOpcode.EndOfPicture);

    return ms.ToArray();
  }

  private static void _WriteDirectBitsRect(MemoryStream ms, PictFile file) {
    var width = file.Width;
    var height = file.Height;
    var rowBytes = width * 3; // 3 components per pixel

    // Opcode
    _WriteUInt16BE(ms, (ushort)PictOpcode.DirectBitsRect);

    // baseAddr (4 bytes)
    _WriteUInt32BE(ms, 0x000000FF);

    // PixMap record (46 bytes)
    _WriteUInt16BE(ms, (ushort)(rowBytes | 0x8000)); // rowBytes with pixmap flag
    // bounds rect
    _WriteInt16BE(ms, 0); // top
    _WriteInt16BE(ms, 0); // left
    _WriteInt16BE(ms, (short)height); // bottom
    _WriteInt16BE(ms, (short)width); // right
    _WriteUInt16BE(ms, 0); // version
    _WriteUInt16BE(ms, 4); // packType (PackBits per component)
    _WriteUInt32BE(ms, 0); // packSize
    _WriteUInt32BE(ms, 0x00480000); // hRes 72 dpi
    _WriteUInt32BE(ms, 0x00480000); // vRes 72 dpi
    _WriteUInt16BE(ms, 16); // pixelType (RGBDirect)
    _WriteUInt16BE(ms, 32); // pixelSize
    _WriteUInt16BE(ms, 3); // cmpCount
    _WriteUInt16BE(ms, 8); // cmpSize
    _WriteUInt32BE(ms, 0); // planeBytes
    _WriteUInt32BE(ms, 0); // pmTable
    _WriteUInt32BE(ms, 0); // reserved

    // source rect
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, (short)height);
    _WriteInt16BE(ms, (short)width);
    // dest rect
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, (short)height);
    _WriteInt16BE(ms, (short)width);
    // transfer mode
    _WriteUInt16BE(ms, 0); // srcCopy

    // Write PackBits-compressed scanlines
    for (var y = 0; y < height; ++y) {
      // Separate into R, G, B components
      var scanline = new byte[rowBytes];
      var componentStride = width;
      for (var x = 0; x < width; ++x) {
        var srcIdx = (y * width + x) * 3;
        scanline[x] = file.PixelData[srcIdx]; // R
        scanline[componentStride + x] = file.PixelData[srcIdx + 1]; // G
        scanline[2 * componentStride + x] = file.PixelData[srcIdx + 2]; // B
      }

      var compressed = _CompressPackBits(scanline);

      if (rowBytes < 8) {
        ms.Write(compressed);
      } else if (rowBytes < 250) {
        ms.WriteByte((byte)compressed.Length);
        ms.Write(compressed);
      } else {
        _WriteUInt16BE(ms, (ushort)compressed.Length);
        ms.Write(compressed);
      }
    }
  }

  private static void _WritePackBitsRect(MemoryStream ms, PictFile file) {
    var width = file.Width;
    var height = file.Height;
    var rowBytes = width;

    // Opcode
    _WriteUInt16BE(ms, (ushort)PictOpcode.PackBitsRect);

    // PixMap record (46 bytes)
    _WriteUInt16BE(ms, (ushort)(rowBytes | 0x8000)); // rowBytes with pixmap flag
    // bounds rect
    _WriteInt16BE(ms, 0); // top
    _WriteInt16BE(ms, 0); // left
    _WriteInt16BE(ms, (short)height); // bottom
    _WriteInt16BE(ms, (short)width); // right
    _WriteUInt16BE(ms, 0); // version
    _WriteUInt16BE(ms, 0); // packType
    _WriteUInt32BE(ms, 0); // packSize
    _WriteUInt32BE(ms, 0x00480000); // hRes 72 dpi
    _WriteUInt32BE(ms, 0x00480000); // vRes 72 dpi
    _WriteUInt16BE(ms, 0); // pixelType
    _WriteUInt16BE(ms, 8); // pixelSize
    _WriteUInt16BE(ms, 1); // cmpCount
    _WriteUInt16BE(ms, 8); // cmpSize
    _WriteUInt32BE(ms, 0); // planeBytes
    _WriteUInt32BE(ms, 0); // pmTable
    _WriteUInt32BE(ms, 0); // reserved

    // Color table
    var palette = file.Palette!;
    var numColors = palette.Length / 3;
    _WriteUInt32BE(ms, 0); // seed
    _WriteUInt16BE(ms, 0); // flags
    _WriteUInt16BE(ms, (ushort)(numColors - 1)); // ctSize
    for (var i = 0; i < numColors; ++i) {
      _WriteUInt16BE(ms, (ushort)i); // index
      ms.WriteByte(palette[i * 3]); // R high
      ms.WriteByte(0); // R low
      ms.WriteByte(palette[i * 3 + 1]); // G high
      ms.WriteByte(0); // G low
      ms.WriteByte(palette[i * 3 + 2]); // B high
      ms.WriteByte(0); // B low
    }

    // source rect
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, (short)height);
    _WriteInt16BE(ms, (short)width);
    // dest rect
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, 0);
    _WriteInt16BE(ms, (short)height);
    _WriteInt16BE(ms, (short)width);
    // transfer mode
    _WriteUInt16BE(ms, 0); // srcCopy

    // Write PackBits-compressed indexed scanlines
    for (var y = 0; y < height; ++y) {
      var scanline = new byte[rowBytes];
      for (var x = 0; x < width; ++x)
        scanline[x] = file.PixelData[y * width + x];

      var compressed = _CompressPackBits(scanline);

      if (rowBytes < 8) {
        ms.Write(compressed);
      } else if (rowBytes < 250) {
        ms.WriteByte((byte)compressed.Length);
        ms.Write(compressed);
      } else {
        _WriteUInt16BE(ms, (ushort)compressed.Length);
        ms.Write(compressed);
      }
    }
  }

  internal static byte[] _CompressPackBits(byte[] data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length)
      if (i + 1 < data.Length && data[i] == data[i + 1]) {
        var runStart = i;
        var value = data[i];
        while (i < data.Length && i - runStart < 128 && data[i] == value)
          ++i;

        var count = i - runStart;
        ms.WriteByte((byte)(257 - count));
        ms.WriteByte(value);
      } else {
        var literalStart = i;
        while (i < data.Length && i - literalStart < 128) {
          if (i + 1 < data.Length && data[i] == data[i + 1])
            break;
          ++i;
        }

        var count = i - literalStart;
        ms.WriteByte((byte)(count - 1));
        ms.Write(data, literalStart, count);
      }

    return ms.ToArray();
  }

  private static void _WriteUInt16BE(MemoryStream ms, ushort value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buf, value);
    ms.Write(buf);
  }

  private static void _WriteInt16BE(MemoryStream ms, short value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(buf, value);
    ms.Write(buf);
  }

  private static void _WriteUInt32BE(MemoryStream ms, uint value) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buf, value);
    ms.Write(buf);
  }
}
