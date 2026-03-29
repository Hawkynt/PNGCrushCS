using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Pdf;

/// <summary>Decodes PDF stream data by applying the filter chain specified in the stream dictionary.</summary>
internal static class PdfStreamDecoder {

  /// <summary>Decodes the raw stream bytes using the /Filter and /DecodeParms entries from the dictionary.</summary>
  public static byte[] Decode(byte[] rawData, Dictionary<string, object?> dict) {
    if (rawData.Length == 0)
      return rawData;

    var filters = _GetFilters(dict);
    if (filters.Count == 0)
      return rawData;

    var decodeParms = _GetDecodeParmsArray(dict, filters.Count);
    var result = rawData;

    for (var i = 0; i < filters.Count; ++i) {
      var parms = i < decodeParms.Count ? decodeParms[i] : null;
      result = _ApplyFilter(result, filters[i], parms);
    }

    return result;
  }

  /// <summary>Decodes FlateDecode data (zlib-wrapped DEFLATE).</summary>
  internal static byte[] DecodeFlateDecode(byte[] data, Dictionary<string, object?>? parms) {
    if (data.Length < 2)
      return data;

    // Skip 2-byte zlib header (CMF + FLG)
    var offset = 2;
    var decompressed = _Inflate(data, offset);

    // Apply predictor if specified
    if (parms != null)
      decompressed = _ApplyPredictor(decompressed, parms);

    return decompressed;
  }

  /// <summary>Decodes ASCII85 data.</summary>
  internal static byte[] DecodeAscii85(byte[] data) {
    var result = new List<byte>();
    var group = new int[5];
    var groupLen = 0;
    var i = 0;

    // Skip leading whitespace and "<~" prefix
    while (i < data.Length && data[i] is 0 or 9 or 10 or 12 or 13 or 32)
      ++i;
    if (i + 1 < data.Length && data[i] == (byte)'<' && data[i + 1] == (byte)'~')
      i += 2;

    while (i < data.Length) {
      var b = data[i];
      ++i;

      // End marker "~>"
      if (b == (byte)'~')
        break;

      // Skip whitespace
      if (b is 0 or 9 or 10 or 12 or 13 or 32)
        continue;

      // 'z' = special case for 4 zero bytes
      if (b == (byte)'z') {
        if (groupLen != 0)
          throw new InvalidDataException("ASCII85: 'z' found in middle of group.");

        result.Add(0);
        result.Add(0);
        result.Add(0);
        result.Add(0);
        continue;
      }

      if (b < 33 || b > 117)
        continue; // skip invalid chars

      group[groupLen] = b - 33;
      ++groupLen;

      if (groupLen < 5)
        continue;

      // Decode 5 ASCII85 chars to 4 bytes
      long val = 0;
      for (var j = 0; j < 5; ++j)
        val = val * 85 + group[j];

      result.Add((byte)(val >> 24));
      result.Add((byte)(val >> 16));
      result.Add((byte)(val >> 8));
      result.Add((byte)val);
      groupLen = 0;
    }

    // Handle remaining partial group
    if (groupLen > 1) {
      for (var j = groupLen; j < 5; ++j)
        group[j] = 84; // pad with 'u' (84)

      long val = 0;
      for (var j = 0; j < 5; ++j)
        val = val * 85 + group[j];

      for (var j = 0; j < groupLen - 1; ++j)
        result.Add((byte)(val >> (24 - j * 8)));
    }

    return result.ToArray();
  }

  /// <summary>Decodes ASCIIHexDecode data.</summary>
  internal static byte[] DecodeAsciiHex(byte[] data) {
    var result = new List<byte>();
    var hi = -1;

    for (var i = 0; i < data.Length; ++i) {
      var b = data[i];

      // '>' terminates
      if (b == (byte)'>')
        break;

      // Skip whitespace
      if (b is 0 or 9 or 10 or 12 or 13 or 32)
        continue;

      var nibble = _HexVal(b);
      if (nibble < 0)
        continue;

      if (hi < 0)
        hi = nibble;
      else {
        result.Add((byte)(hi * 16 + nibble));
        hi = -1;
      }
    }

    // Odd nibble: append with implicit 0
    if (hi >= 0)
      result.Add((byte)(hi * 16));

    return result.ToArray();
  }

  /// <summary>Decodes LZWDecode data.</summary>
  internal static byte[] DecodeLzw(byte[] data, Dictionary<string, object?>? parms) {
    var result = _LzwDecompress(data);
    if (parms != null)
      result = _ApplyPredictor(result, parms);

    return result;
  }

  /// <summary>Decodes RunLengthDecode data (PackBits-style).</summary>
  internal static byte[] DecodeRunLength(byte[] data) {
    var result = new List<byte>();
    var i = 0;

    while (i < data.Length) {
      var b = data[i];
      ++i;

      if (b == 128)
        break; // EOD marker

      if (b < 128) {
        // Copy next (b + 1) bytes literally
        var count = b + 1;
        for (var j = 0; j < count && i < data.Length; ++j, ++i)
          result.Add(data[i]);
      } else {
        // Repeat next byte (257 - b) times
        var count = 257 - b;
        if (i < data.Length) {
          var val = data[i];
          ++i;
          for (var j = 0; j < count; ++j)
            result.Add(val);
        }
      }
    }

    return result.ToArray();
  }

  private static byte[] _ApplyFilter(byte[] data, string filter, Dictionary<string, object?>? parms) => filter switch {
    "FlateDecode" or "Fl" => DecodeFlateDecode(data, parms),
    "ASCII85Decode" or "A85" => DecodeAscii85(data),
    "ASCIIHexDecode" or "AHx" => DecodeAsciiHex(data),
    "LZWDecode" or "LZW" => DecodeLzw(data, parms),
    "RunLengthDecode" or "RL" => DecodeRunLength(data),
    "DCTDecode" or "DCT" => data, // JPEG: raw bytes are the image
    "JPXDecode" => data,          // JPEG2000: raw bytes are the image
    "CCITTFaxDecode" or "CCF" => data, // CCITT: not decoded here
    "Crypt" => data,              // Encryption: not handled
    _ => data,
  };

  private static List<string> _GetFilters(Dictionary<string, object?> dict) {
    var filters = new List<string>();
    if (!dict.TryGetValue("Filter", out var filterObj))
      return filters;

    if (filterObj is string name)
      filters.Add(name);
    else if (filterObj is List<object?> list)
      foreach (var item in list)
        if (item is string s)
          filters.Add(s);

    return filters;
  }

  private static List<Dictionary<string, object?>?> _GetDecodeParmsArray(Dictionary<string, object?> dict, int filterCount) {
    var result = new List<Dictionary<string, object?>?>();
    if (!dict.TryGetValue("DecodeParms", out var parmsObj))
      return result;

    if (parmsObj is Dictionary<string, object?> singleDict) {
      result.Add(singleDict);
      return result;
    }

    if (parmsObj is not List<object?> list)
      return result;

    foreach (var item in list)
      result.Add(item as Dictionary<string, object?>);

    return result;
  }

  private static byte[] _Inflate(byte[] data, int offset) {
    try {
      using var input = new MemoryStream(data, offset, data.Length - offset);
      using var deflate = new DeflateStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      deflate.CopyTo(output);
      return output.ToArray();
    } catch {
      // Try without skipping zlib header (some PDFs omit it)
      try {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
      } catch {
        return data;
      }
    }
  }

  private static byte[] _ApplyPredictor(byte[] data, Dictionary<string, object?> parms) {
    var predictor = 1;
    var columns = 1;
    var colors = 1;
    var bitsPerComponent = 8;

    if (parms.TryGetValue("Predictor", out var pObj))
      predictor = _ToInt(pObj);
    if (parms.TryGetValue("Columns", out var cObj))
      columns = _ToInt(cObj);
    if (parms.TryGetValue("Colors", out var colObj))
      colors = _ToInt(colObj);
    if (parms.TryGetValue("BitsPerComponent", out var bpcObj))
      bitsPerComponent = _ToInt(bpcObj);

    if (predictor == 1)
      return data; // No prediction

    if (predictor == 2)
      return _TiffPredictor(data, columns, colors, bitsPerComponent);

    // PNG predictors (10-15)
    if (predictor >= 10)
      return _PngPredictor(data, columns, colors, bitsPerComponent);

    return data;
  }

  private static byte[] _PngPredictor(byte[] data, int columns, int colors, int bitsPerComponent) {
    var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
    var rowBytes = columns * colors * bitsPerComponent / 8;
    var srcRowBytes = rowBytes + 1; // +1 for filter type byte

    if (srcRowBytes <= 0 || data.Length < srcRowBytes)
      return data;

    var rows = data.Length / srcRowBytes;
    var output = new byte[rows * rowBytes];
    var prevRow = new byte[rowBytes];

    for (var row = 0; row < rows; ++row) {
      var srcOffset = row * srcRowBytes;
      var dstOffset = row * rowBytes;
      var filterType = data[srcOffset];

      for (var col = 0; col < rowBytes; ++col) {
        var raw = data[srcOffset + 1 + col];
        var a = col >= bytesPerPixel ? output[dstOffset + col - bytesPerPixel] : (byte)0;
        var b = prevRow[col];
        var c = col >= bytesPerPixel ? prevRow[col - bytesPerPixel] : (byte)0;

        output[dstOffset + col] = filterType switch {
          0 => raw,                                          // None
          1 => (byte)(raw + a),                              // Sub
          2 => (byte)(raw + b),                              // Up
          3 => (byte)(raw + (a + b) / 2),                    // Average
          4 => (byte)(raw + _PaethPredictor(a, b, c)),       // Paeth
          _ => raw,
        };
      }

      Array.Copy(output, dstOffset, prevRow, 0, rowBytes);
    }

    return output;
  }

  private static byte _PaethPredictor(byte a, byte b, byte c) {
    var p = a + b - c;
    var pa = Math.Abs(p - a);
    var pb = Math.Abs(p - b);
    var pc = Math.Abs(p - c);

    if (pa <= pb && pa <= pc)
      return a;

    return pb <= pc ? b : c;
  }

  private static byte[] _TiffPredictor(byte[] data, int columns, int colors, int bitsPerComponent) {
    var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
    var rowBytes = columns * bytesPerPixel;

    if (rowBytes <= 0)
      return data;

    var rows = data.Length / rowBytes;
    var output = new byte[data.Length];
    Array.Copy(data, output, data.Length);

    for (var row = 0; row < rows; ++row) {
      var offset = row * rowBytes;
      for (var col = bytesPerPixel; col < rowBytes; ++col)
        output[offset + col] = (byte)(output[offset + col] + output[offset + col - bytesPerPixel]);
    }

    return output;
  }

  private static byte[] _LzwDecompress(byte[] data) {
    if (data.Length == 0)
      return [];

    var result = new List<byte>();
    var bitPos = 0;
    var codeSize = 9;
    const int clearCode = 256;
    const int eodCode = 257;
    var nextCode = 258;
    var table = new Dictionary<int, byte[]>();

    // Initialize table
    for (var i = 0; i < 256; ++i)
      table[i] = [(byte)i];

    table[clearCode] = [];
    table[eodCode] = [];

    byte[]? prevEntry = null;

    while (true) {
      var code = _ReadBits(data, ref bitPos, codeSize);
      if (code < 0 || code == eodCode)
        break;

      if (code == clearCode) {
        codeSize = 9;
        nextCode = 258;
        table.Clear();
        for (var i = 0; i < 256; ++i)
          table[i] = [(byte)i];
        table[clearCode] = [];
        table[eodCode] = [];
        prevEntry = null;
        continue;
      }

      byte[] entry;
      if (table.TryGetValue(code, out var existing))
        entry = existing;
      else if (code == nextCode && prevEntry != null) {
        entry = new byte[prevEntry.Length + 1];
        prevEntry.CopyTo(entry, 0);
        entry[^1] = prevEntry[0];
      } else
        break; // Invalid

      result.AddRange(entry);

      if (prevEntry != null && nextCode < 4096) {
        var newEntry = new byte[prevEntry.Length + 1];
        prevEntry.CopyTo(newEntry, 0);
        newEntry[^1] = entry[0];
        table[nextCode] = newEntry;
        ++nextCode;

        if (nextCode >= (1 << codeSize) && codeSize < 12)
          ++codeSize;
      }

      prevEntry = entry;
    }

    return result.ToArray();
  }

  private static int _ReadBits(byte[] data, ref int bitPos, int count) {
    var result = 0;
    for (var i = 0; i < count; ++i) {
      var byteIndex = bitPos / 8;
      var bitIndex = 7 - (bitPos % 8); // MSB first for LZW
      if (byteIndex >= data.Length)
        return -1;

      if ((data[byteIndex] & (1 << bitIndex)) != 0)
        result |= 1 << (count - 1 - i);

      ++bitPos;
    }

    return result;
  }

  private static int _ToInt(object? obj) => obj switch {
    int i => i,
    long l => (int)l,
    double d => (int)d,
    float f => (int)f,
    _ => 0,
  };

  private static int _HexVal(byte b) => b switch {
    >= (byte)'0' and <= (byte)'9' => b - (byte)'0',
    >= (byte)'a' and <= (byte)'f' => b - (byte)'a' + 10,
    >= (byte)'A' and <= (byte)'F' => b - (byte)'A' + 10,
    _ => -1,
  };
}
