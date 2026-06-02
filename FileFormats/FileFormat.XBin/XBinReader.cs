using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.TextMode;

namespace FileFormat.XBin;

public static class XBinReader {

  private static readonly byte[] _Magic = [(byte)'X', (byte)'B', (byte)'I', (byte)'N', 0x1A];

  public static XBinFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XBIN file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XBinFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static XBinFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static XBinFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 11)
      throw new InvalidDataException("XBIN data too small for header.");
    if (!data[..5].SequenceEqual(_Magic))
      throw new InvalidDataException("Missing 'XBIN' + EOF magic at start of file.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(7, 2));
    var fontHeight = data[9];
    var flags = (XBinFlags)data[10];

    if (width is 0 or > 4096 || height is 0 or > 4096)
      throw new InvalidDataException($"XBIN reports implausible dimensions: {width}×{height}.");
    if (fontHeight is 0 or > 32)
      throw new InvalidDataException($"XBIN reports implausible font height: {fontHeight}.");

    var offset = 11;
    byte[]? palette = null;
    if ((flags & XBinFlags.Palette) != 0) {
      if (data.Length < offset + 48) throw new InvalidDataException("XBIN palette block truncated.");
      palette = data.Slice(offset, 48).ToArray();
      offset += 48;
    }

    byte[]? font = null;
    if ((flags & XBinFlags.Font) != 0) {
      var glyphCount = (flags & XBinFlags.Font512) != 0 ? 512 : 256;
      var fontBytes = glyphCount * fontHeight;
      if (data.Length < offset + fontBytes) throw new InvalidDataException("XBIN font block truncated.");
      font = data.Slice(offset, fontBytes).ToArray();
      offset += fontBytes;
    }

    var cellsExpected = width * height;
    var cells = new TextCell[cellsExpected];
    var useBlinkBit = (flags & XBinFlags.NonBlink) == 0;

    if ((flags & XBinFlags.Compressed) != 0) {
      _DecodeRle(data[offset..], cells, useBlinkBit);
    } else {
      if (data.Length < offset + cellsExpected * 2)
        throw new InvalidDataException("XBIN image block truncated.");
      for (var i = 0; i < cellsExpected; ++i)
        cells[i] = TextCell.FromAttribute(data[offset + i * 2], data[offset + i * 2 + 1], useBlinkBit);
    }

    return new XBinFile {
      ColumnCount = width,
      RowCount = height,
      FontHeight = fontHeight,
      Flags = flags,
      Palette = palette,
      Font = font,
      Cells = cells,
    };
  }

  // XBIN RLE: top 2 bits of control byte = mode, low 6 bits = count - 1. Modes:
  // 00 = no compression (count cell-pairs follow), 01 = run of char (1 char + count attrs),
  // 10 = run of attr (count chars + 1 attr), 11 = full run (1 char + 1 attr, repeat count times).
  private static void _DecodeRle(ReadOnlySpan<byte> src, TextCell[] dst, bool useBlinkBit) {
    var ix = 0;
    var di = 0;
    while (di < dst.Length) {
      if (ix >= src.Length)
        throw new InvalidDataException("XBIN RLE stream truncated.");
      var ctrl = src[ix++];
      var mode = ctrl >> 6;
      var count = (ctrl & 0x3F) + 1;
      switch (mode) {
        case 0:
          for (var k = 0; k < count; ++k) {
            if (ix + 1 >= src.Length || di >= dst.Length) throw new InvalidDataException("XBIN RLE no-comp run overflow.");
            dst[di++] = TextCell.FromAttribute(src[ix], src[ix + 1], useBlinkBit);
            ix += 2;
          }
          break;
        case 1: {
          var cp = src[ix++];
          for (var k = 0; k < count; ++k) {
            if (ix >= src.Length || di >= dst.Length) throw new InvalidDataException("XBIN RLE char-run overflow.");
            dst[di++] = TextCell.FromAttribute(cp, src[ix++], useBlinkBit);
          }
          break;
        }
        case 2: {
          // Attribute first, then 'count' code points.
          var attr = src[ix++];
          for (var k = 0; k < count; ++k) {
            if (ix >= src.Length || di >= dst.Length) throw new InvalidDataException("XBIN RLE attr-run overflow.");
            dst[di++] = TextCell.FromAttribute(src[ix++], attr, useBlinkBit);
          }
          break;
        }
        case 3: {
          if (ix + 1 >= src.Length) throw new InvalidDataException("XBIN RLE full-run truncated.");
          var cp = src[ix++]; var attr = src[ix++];
          for (var k = 0; k < count; ++k) {
            if (di >= dst.Length) throw new InvalidDataException("XBIN RLE full-run overflow.");
            dst[di++] = TextCell.FromAttribute(cp, attr, useBlinkBit);
          }
          break;
        }
      }
    }
  }
}
