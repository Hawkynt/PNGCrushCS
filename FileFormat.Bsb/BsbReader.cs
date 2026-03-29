using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Bsb;

/// <summary>Reads BSB/KAP nautical chart files from bytes, streams, or file paths.</summary>
public static class BsbReader {

  public static BsbFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BSB file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BsbFile FromStream(Stream stream) {
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

  public static BsbFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid BSB file.");

    var headerEnd = _FindHeaderEnd(data);
    if (headerEnd < 0)
      throw new InvalidDataException("No NUL terminator found; invalid BSB header.");

    var headerText = Encoding.ASCII.GetString(data, 0, headerEnd);
    var lines = _SplitHeaderLines(headerText);

    var width = 0;
    var height = 0;
    var name = "";
    var depth = 7;
    var palette = new Dictionary<int, (byte R, byte G, byte B)>();

    var hasExplicitDepth = false;

    foreach (var line in lines) {
      if (line.StartsWith("BSB/", StringComparison.Ordinal))
        _ParseBsbLine(line, ref width, ref height, ref name);
      else if (line.StartsWith("RGB/", StringComparison.Ordinal))
        _ParseRgbLine(line, palette);
      else if (line.StartsWith("IFM/", StringComparison.Ordinal)) {
        if (int.TryParse(line.Substring(4).Trim(), out var d) && d >= 1 && d <= 7) {
          depth = d;
          hasExplicitDepth = true;
        }
      } else if (line.StartsWith("VER/", StringComparison.Ordinal)) {
        // version line -- no action needed
      }
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("BSB header missing or invalid RA= dimensions.");

    var paletteCount = palette.Count > 0 ? _MaxKey(palette) + 1 : 0;
    var paletteBytes = new byte[paletteCount * 3];
    foreach (var kvp in palette) {
      var idx = kvp.Key;
      if (idx < paletteCount) {
        paletteBytes[idx * 3] = kvp.Value.R;
        paletteBytes[idx * 3 + 1] = kvp.Value.G;
        paletteBytes[idx * 3 + 2] = kvp.Value.B;
      }
    }

    // Determine depth from palette count if not explicitly specified
    if (!hasExplicitDepth && paletteCount > 0) {
      depth = 1;
      while ((1 << depth) < paletteCount && depth < 7)
        ++depth;
    }

    var pixelDataOffset = headerEnd + 1;
    var pixelData = _DecodePixelData(data, pixelDataOffset, width, height, depth);

    return new BsbFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Palette = paletteBytes,
      PaletteCount = paletteCount,
      Depth = depth,
      Name = name,
    };
  }

  private static int _FindHeaderEnd(byte[] data) {
    for (var i = 0; i < data.Length; ++i)
      if (data[i] == 0x00)
        return i;

    return -1;
  }

  private static List<string> _SplitHeaderLines(string headerText) {
    var lines = new List<string>();
    var sb = new StringBuilder();

    foreach (var ch in headerText) {
      if (ch == '\r')
        continue;

      if (ch == '\n') {
        if (sb.Length > 0) {
          lines.Add(sb.ToString());
          sb.Clear();
        }

        continue;
      }

      sb.Append(ch);
    }

    if (sb.Length > 0)
      lines.Add(sb.ToString());

    return lines;
  }

  private static void _ParseBsbLine(string line, ref int width, ref int height, ref string name) {
    // BSB/NA=ChartName,NU=123,RA=width,height
    var content = line.Substring(4);
    var fields = content.Split(',');

    for (var i = 0; i < fields.Length; ++i) {
      var field = fields[i].Trim();

      if (field.StartsWith("NA=", StringComparison.Ordinal))
        name = field.Substring(3);
      else if (field.StartsWith("RA=", StringComparison.Ordinal)) {
        if (int.TryParse(field.Substring(3), out var w)) {
          width = w;
          // Height is the next field (not a key=value, just a bare number)
          if (i + 1 < fields.Length && int.TryParse(fields[i + 1].Trim(), out var h))
            height = h;
        }
      }
    }
  }

  private static void _ParseRgbLine(string line, Dictionary<int, (byte R, byte G, byte B)> palette) {
    // RGB/index,R,G,B
    var content = line.Substring(4);
    var parts = content.Split(',');
    if (parts.Length < 4)
      return;

    if (!int.TryParse(parts[0].Trim(), out var index))
      return;
    if (!int.TryParse(parts[1].Trim(), out var r))
      return;
    if (!int.TryParse(parts[2].Trim(), out var g))
      return;
    if (!int.TryParse(parts[3].Trim(), out var b))
      return;

    palette[index] = ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
  }

  private static int _MaxKey(Dictionary<int, (byte R, byte G, byte B)> palette) {
    var max = 0;
    foreach (var key in palette.Keys)
      if (key > max)
        max = key;

    return max;
  }

  internal static byte[] _DecodePixelData(byte[] data, int offset, int width, int height, int depth) {
    var pixels = new byte[width * height];
    var colorBits = depth;
    var runBits = 8 - colorBits;

    // Read row index table (4 bytes per row, big-endian offsets)
    var rowOffsets = new int[height];
    for (var row = 0; row < height; ++row) {
      var tableOffset = offset + row * 4;
      if (tableOffset + 4 <= data.Length)
        rowOffsets[row] = (data[tableOffset] << 24) | (data[tableOffset + 1] << 16) | (data[tableOffset + 2] << 8) | data[tableOffset + 3];
      else
        rowOffsets[row] = -1;
    }

    for (var row = 0; row < height; ++row) {
      var rowStart = rowOffsets[row];
      if (rowStart < 0 || rowStart >= data.Length)
        continue;

      var pos = rowStart;

      // Read row number (7-bit continuation encoding)
      while (pos < data.Length && (data[pos] & 0x80) != 0)
        ++pos;

      // Skip the last byte of row number
      if (pos < data.Length)
        ++pos;

      // Decode RLE pixel data for this row
      var col = 0;
      while (col < width && pos < data.Length) {
        if (data[pos] == 0x00)
          break;

        var b = data[pos++];
        var colorIndex = (byte)(b >> runBits);
        var runLength = b & ((1 << runBits) - 1);

        // If run length is 0, read additional continuation bytes
        if (runLength == 0) {
          while (pos < data.Length) {
            var next = data[pos++];
            runLength = (runLength << 7) | (next & 0x7F);
            if ((next & 0x80) == 0)
              break;
          }
        }

        if (runLength == 0)
          runLength = 1;

        var pixelOffset = row * width + col;
        for (var i = 0; i < runLength && col < width; ++i, ++col)
          if (pixelOffset + i < pixels.Length)
            pixels[pixelOffset + i] = colorIndex;
      }
    }

    return pixels;
  }
}
