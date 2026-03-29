using System;
using System.IO;
using System.Text;

namespace FileFormat.Bsb;

/// <summary>Assembles BSB/KAP nautical chart file bytes from a BsbFile model.</summary>
public static class BsbWriter {

  public static byte[] ToBytes(BsbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Write text header
    var header = _BuildHeader(file);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    ms.Write(headerBytes, 0, headerBytes.Length);

    // NUL terminator
    ms.WriteByte(0x00);

    // Encode and write pixel data rows
    var depth = Math.Clamp(file.Depth, 1, 7);
    var rowData = _EncodePixelData(file.PixelData, file.Width, file.Height, depth);

    // Write row index table (4 bytes per row, big-endian offsets from start of file)
    var indexTableOffset = (int)ms.Position;
    var indexTable = new byte[file.Height * 4];
    var rowDataOffset = indexTableOffset + indexTable.Length;
    var currentOffset = rowDataOffset;

    for (var row = 0; row < file.Height; ++row) {
      var offset = currentOffset;
      _WriteBigEndian32(indexTable, row * 4, offset);
      currentOffset += rowData[row].Length;
    }

    ms.Write(indexTable, 0, indexTable.Length);

    // Write encoded rows
    for (var row = 0; row < file.Height; ++row)
      ms.Write(rowData[row], 0, rowData[row].Length);

    return ms.ToArray();
  }

  internal static string _BuildHeader(BsbFile file) {
    var sb = new StringBuilder();
    sb.AppendLine("VER/3.0");
    sb.Append("BSB/NA=");
    sb.Append(string.IsNullOrEmpty(file.Name) ? "NOAA" : file.Name);
    sb.Append(",RA=");
    sb.Append(file.Width);
    sb.Append(',');
    sb.AppendLine(file.Height.ToString());

    // Write depth (bits per pixel for index encoding)
    sb.Append("IFM/");
    sb.AppendLine(Math.Clamp(file.Depth, 1, 7).ToString());

    // Write palette
    var paletteCount = file.PaletteCount;
    for (var i = 0; i < paletteCount; ++i) {
      var baseIdx = i * 3;
      var r = baseIdx < file.Palette.Length ? file.Palette[baseIdx] : (byte)0;
      var g = baseIdx + 1 < file.Palette.Length ? file.Palette[baseIdx + 1] : (byte)0;
      var b = baseIdx + 2 < file.Palette.Length ? file.Palette[baseIdx + 2] : (byte)0;
      sb.Append("RGB/");
      sb.Append(i);
      sb.Append(',');
      sb.Append(r);
      sb.Append(',');
      sb.Append(g);
      sb.Append(',');
      sb.AppendLine(b.ToString());
    }

    return sb.ToString();
  }

  internal static byte[][] _EncodePixelData(byte[] pixelData, int width, int height, int depth) {
    var rows = new byte[height][];
    var colorBits = depth;
    var runBits = 8 - colorBits;
    var maxRunInline = (1 << runBits) - 1;

    for (var row = 0; row < height; ++row) {
      using var rowStream = new MemoryStream();

      // Write row number (1-based, 7-bit continuation encoding)
      _WriteRowNumber(rowStream, row + 1);

      // RLE encode the row
      var rowOffset = row * width;
      var col = 0;
      while (col < width) {
        var colorIndex = rowOffset + col < pixelData.Length ? pixelData[rowOffset + col] : (byte)0;
        var runLength = 1;
        while (col + runLength < width && runLength < 65535) {
          var nextIdx = rowOffset + col + runLength;
          if (nextIdx < pixelData.Length && pixelData[nextIdx] == colorIndex)
            ++runLength;
          else
            break;
        }

        if (runLength <= maxRunInline) {
          // Fits in a single byte
          var b = (byte)((colorIndex << runBits) | runLength);
          rowStream.WriteByte(b);
        } else {
          // Color with zero run length, then continuation bytes for the actual length
          var b = (byte)(colorIndex << runBits);
          rowStream.WriteByte(b);
          _WriteRunContinuation(rowStream, runLength);
        }

        col += runLength;
      }

      // Row terminator
      rowStream.WriteByte(0x00);

      rows[row] = rowStream.ToArray();
    }

    return rows;
  }

  private static void _WriteRowNumber(MemoryStream ms, int rowNumber) {
    // 7-bit encoding with high bit set on continuation bytes
    if (rowNumber < 0x80) {
      ms.WriteByte((byte)rowNumber);
      return;
    }

    // Collect bytes in reverse
    var bytes = new byte[4];
    var count = 0;
    var value = rowNumber;
    while (value > 0) {
      bytes[count++] = (byte)(value & 0x7F);
      value >>= 7;
    }

    // Write with high bit set on all but last
    for (var i = count - 1; i >= 0; --i)
      ms.WriteByte(i > 0 ? (byte)(bytes[i] | 0x80) : bytes[i]);
  }

  private static void _WriteRunContinuation(MemoryStream ms, int runLength) {
    // 7-bit encoding with high bit clear on last byte
    var bytes = new byte[4];
    var count = 0;
    var value = runLength;
    while (value > 0) {
      bytes[count++] = (byte)(value & 0x7F);
      value >>= 7;
    }

    if (count == 0) {
      ms.WriteByte(0x00);
      return;
    }

    for (var i = count - 1; i >= 0; --i)
      ms.WriteByte(i > 0 ? (byte)(bytes[i] | 0x80) : bytes[i]);
  }

  private static void _WriteBigEndian32(byte[] buffer, int offset, int value) {
    buffer[offset] = (byte)((value >> 24) & 0xFF);
    buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
    buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
    buffer[offset + 3] = (byte)(value & 0xFF);
  }
}
