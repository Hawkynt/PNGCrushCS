using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FileFormat.Pdf;

/// <summary>Tokenizer and object parser for the PDF file format.</summary>
internal static class PdfParser {

  /// <summary>Skips PDF whitespace characters and comments.</summary>
  public static void SkipWhitespace(byte[] data, ref int pos) {
    while (pos < data.Length) {
      var b = data[pos];
      if (b is 0 or 9 or 10 or 12 or 13 or 32) {
        ++pos;
        continue;
      }

      // PDF comment: '%' through end of line
      if (b == (byte)'%') {
        ++pos;
        while (pos < data.Length && data[pos] != 10 && data[pos] != 13)
          ++pos;

        continue;
      }

      break;
    }
  }

  /// <summary>Parses a PDF object at the current position. Returns the object and advances <paramref name="pos"/>.</summary>
  public static object? ParseObject(byte[] data, ref int pos) {
    SkipWhitespace(data, ref pos);
    if (pos >= data.Length)
      return null;

    var b = data[pos];

    // Dictionary or hex string
    if (b == (byte)'<') {
      if (pos + 1 < data.Length && data[pos + 1] == (byte)'<')
        return _ParseDictionary(data, ref pos);

      return _ParseHexString(data, ref pos);
    }

    // Array
    if (b == (byte)'[')
      return _ParseArray(data, ref pos);

    // Literal string
    if (b == (byte)'(')
      return _ParseLiteralString(data, ref pos);

    // Name
    if (b == (byte)'/')
      return _ParseName(data, ref pos);

    // Number or indirect reference (n g R)
    if (b is >= (byte)'0' and <= (byte)'9' || b == (byte)'+' || b == (byte)'-' || b == (byte)'.')
      return _ParseNumberOrRef(data, ref pos);

    // Keywords: true, false, null, stream, endobj, etc.
    return _ParseKeyword(data, ref pos);
  }

  /// <summary>Parses a PDF object at a specific offset without modifying the caller's position.</summary>
  public static object? ParseObjectAt(byte[] data, int offset) {
    var pos = offset;
    return ParseObject(data, ref pos);
  }

  /// <summary>Resolves an indirect reference to the actual object by looking it up in the xref table.</summary>
  public static object? ResolveRef(object? obj, byte[] data, Dictionary<int, long> xref) {
    if (obj is not PdfRef r)
      return obj;

    if (!xref.TryGetValue(r.ObjectNumber, out var offset))
      return null;

    var pos = (int)offset;
    SkipWhitespace(data, ref pos);

    // Skip "N G obj" header
    _SkipInteger(data, ref pos); // object number
    SkipWhitespace(data, ref pos);
    _SkipInteger(data, ref pos); // generation
    SkipWhitespace(data, ref pos);
    _SkipKeyword(data, ref pos); // "obj"

    return ParseObject(data, ref pos);
  }

  /// <summary>Resolves a reference and returns the result as a dictionary, or null.</summary>
  public static Dictionary<string, object?>? ResolveDict(object? obj, byte[] data, Dictionary<int, long> xref) {
    var resolved = ResolveRef(obj, data, xref);
    if (resolved is PdfStream ps)
      return ps.Dictionary;

    return resolved as Dictionary<string, object?>;
  }

  /// <summary>Resolves a reference and returns the result as a PdfStream, or null.</summary>
  public static PdfStream? ResolveStream(object? obj, byte[] data, Dictionary<int, long> xref) {
    var resolved = ResolveRef(obj, data, xref);
    return resolved as PdfStream;
  }

  /// <summary>Gets an integer value from a dictionary, resolving references if needed.</summary>
  public static int GetInt(Dictionary<string, object?> dict, string key, byte[] data, Dictionary<int, long> xref, int defaultValue = 0) {
    if (!dict.TryGetValue(key, out var val))
      return defaultValue;

    val = ResolveRef(val, data, xref);
    return val switch {
      int i => i,
      long l => (int)l,
      double d => (int)d,
      float f => (int)f,
      _ => defaultValue,
    };
  }

  /// <summary>Gets a name (string) value from a dictionary, resolving references if needed.</summary>
  public static string? GetName(Dictionary<string, object?> dict, string key, byte[] data, Dictionary<int, long> xref) {
    if (!dict.TryGetValue(key, out var val))
      return null;

    val = ResolveRef(val, data, xref);
    return val as string;
  }

  private static object _ParseDictionary(byte[] data, ref int pos) {
    // Skip "<<"
    pos += 2;
    var dict = new Dictionary<string, object?>();

    while (pos < data.Length) {
      SkipWhitespace(data, ref pos);
      if (pos >= data.Length)
        break;

      // End of dictionary
      if (pos + 1 < data.Length && data[pos] == (byte)'>' && data[pos + 1] == (byte)'>') {
        pos += 2;
        break;
      }

      // Key must be a name
      if (data[pos] != (byte)'/') {
        // Malformed; skip byte and try to continue
        ++pos;
        continue;
      }

      var key = _ParseName(data, ref pos);
      var value = ParseObject(data, ref pos);
      dict[key] = value;
    }

    // Check if followed by "stream"
    var savedPos = pos;
    SkipWhitespace(data, ref savedPos);
    if (_MatchesKeyword(data, savedPos, "stream"u8)) {
      pos = savedPos + 6;

      // Skip \r\n or \n after "stream"
      if (pos < data.Length && data[pos] == 13)
        ++pos;
      if (pos < data.Length && data[pos] == 10)
        ++pos;

      var length = 0;
      if (dict.TryGetValue("Length", out var lenObj)) {
        // Length might be an indirect reference itself
        if (lenObj is PdfRef lenRef) {
          // For stream length references, do a quick in-place resolve
          var lengthResolved = _QuickResolveInt(data, lenRef);
          if (lengthResolved.HasValue)
            length = lengthResolved.Value;
        } else
          length = _ToInt(lenObj);
      }

      // Clamp length to available data
      if (length < 0)
        length = 0;
      if (pos + length > data.Length)
        length = data.Length - pos;

      // If length seems wrong, search for "endstream" from current position
      var streamStart = pos;
      var streamEnd = streamStart + length;

      // Verify endstream is actually there, otherwise search for it
      var verifyPos = streamEnd;
      _SkipLineEnding(data, ref verifyPos);
      if (!_MatchesKeyword(data, verifyPos, "endstream"u8))
        streamEnd = _FindEndstream(data, streamStart);

      length = streamEnd - streamStart;
      if (length < 0)
        length = 0;

      var streamData = new byte[length];
      if (length > 0)
        Array.Copy(data, streamStart, streamData, 0, length);

      pos = streamEnd;

      // Skip past "endstream"
      _SkipLineEnding(data, ref pos);
      if (_MatchesKeyword(data, pos, "endstream"u8))
        pos += 9;

      return new PdfStream { Dictionary = dict, RawData = streamData };
    }

    return dict;
  }

  private static string _ParseName(byte[] data, ref int pos) {
    ++pos; // skip '/'
    var sb = new StringBuilder();

    while (pos < data.Length) {
      var b = data[pos];

      // Name terminators: whitespace, delimiters
      if (b is 0 or 9 or 10 or 12 or 13 or 32 or (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%')
        break;

      // Hex escape: #XX
      if (b == (byte)'#' && pos + 2 < data.Length) {
        var hi = _HexVal(data[pos + 1]);
        var lo = _HexVal(data[pos + 2]);
        if (hi >= 0 && lo >= 0) {
          sb.Append((char)(hi * 16 + lo));
          pos += 3;
          continue;
        }
      }

      sb.Append((char)b);
      ++pos;
    }

    return sb.ToString();
  }

  private static List<object?> _ParseArray(byte[] data, ref int pos) {
    ++pos; // skip '['
    var list = new List<object?>();

    while (pos < data.Length) {
      SkipWhitespace(data, ref pos);
      if (pos >= data.Length)
        break;

      if (data[pos] == (byte)']') {
        ++pos;
        break;
      }

      list.Add(ParseObject(data, ref pos));
    }

    return list;
  }

  private static byte[] _ParseLiteralString(byte[] data, ref int pos) {
    ++pos; // skip '('
    var result = new List<byte>();
    var depth = 1;

    while (pos < data.Length && depth > 0) {
      var b = data[pos];

      if (b == (byte)'\\' && pos + 1 < data.Length) {
        ++pos;
        var esc = data[pos];
        switch (esc) {
          case (byte)'n':
            result.Add(10);
            break;
          case (byte)'r':
            result.Add(13);
            break;
          case (byte)'t':
            result.Add(9);
            break;
          case (byte)'b':
            result.Add(8);
            break;
          case (byte)'f':
            result.Add(12);
            break;
          case (byte)'(':
            result.Add((byte)'(');
            break;
          case (byte)')':
            result.Add((byte)')');
            break;
          case (byte)'\\':
            result.Add((byte)'\\');
            break;
          default:
            // Octal escape: \ddd
            if (esc is >= (byte)'0' and <= (byte)'7') {
              var octal = esc - (byte)'0';
              if (pos + 1 < data.Length && data[pos + 1] is >= (byte)'0' and <= (byte)'7') {
                ++pos;
                octal = octal * 8 + (data[pos] - (byte)'0');
                if (pos + 1 < data.Length && data[pos + 1] is >= (byte)'0' and <= (byte)'7') {
                  ++pos;
                  octal = octal * 8 + (data[pos] - (byte)'0');
                }
              }
              result.Add((byte)octal);
            } else
              result.Add(esc);

            break;
        }

        ++pos;
        continue;
      }

      if (b == (byte)'(')
        ++depth;
      else if (b == (byte)')') {
        --depth;
        if (depth == 0) {
          ++pos;
          break;
        }
      }

      result.Add(b);
      ++pos;
    }

    return result.ToArray();
  }

  private static byte[] _ParseHexString(byte[] data, ref int pos) {
    ++pos; // skip '<'
    var result = new List<byte>();

    while (pos < data.Length) {
      var b = data[pos];
      if (b == (byte)'>') {
        ++pos;
        break;
      }

      // Skip whitespace inside hex strings
      if (b is 0 or 9 or 10 or 12 or 13 or 32) {
        ++pos;
        continue;
      }

      var hi = _HexVal(b);
      ++pos;

      int lo;
      if (pos < data.Length && data[pos] != (byte)'>') {
        // Skip whitespace before second nibble
        while (pos < data.Length && data[pos] is 0 or 9 or 10 or 12 or 13 or 32)
          ++pos;

        if (pos < data.Length && data[pos] != (byte)'>') {
          lo = _HexVal(data[pos]);
          ++pos;
        } else
          lo = 0; // Odd number of hex digits: last nibble is 0
      } else
        lo = 0;

      if (hi >= 0 && lo >= 0)
        result.Add((byte)(hi * 16 + lo));
    }

    return result.ToArray();
  }

  private static object _ParseNumberOrRef(byte[] data, ref int pos) {
    var start = pos;
    var hasDecimal = false;

    if (pos < data.Length && data[pos] is (byte)'+' or (byte)'-')
      ++pos;

    while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;

    if (pos < data.Length && data[pos] == (byte)'.') {
      hasDecimal = true;
      ++pos;
      while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
        ++pos;
    }

    var numStr = Encoding.ASCII.GetString(data, start, pos - start);

    if (hasDecimal)
      return double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;

    if (!int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
      return 0;

    // Check if this is an indirect reference: number generation R
    var savedPos = pos;
    SkipWhitespace(data, ref savedPos);

    if (savedPos < data.Length && data[savedPos] is >= (byte)'0' and <= (byte)'9') {
      var genStart = savedPos;
      while (savedPos < data.Length && data[savedPos] is >= (byte)'0' and <= (byte)'9')
        ++savedPos;

      if (int.TryParse(Encoding.ASCII.GetString(data, genStart, savedPos - genStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out var gen)) {
        SkipWhitespace(data, ref savedPos);
        if (savedPos < data.Length && data[savedPos] == (byte)'R') {
          // Confirm 'R' is followed by a delimiter or end
          if (savedPos + 1 >= data.Length || _IsDelimiter(data[savedPos + 1])) {
            pos = savedPos + 1;
            return new PdfRef(num, gen);
          }
        }
      }
    }

    return num;
  }

  private static object? _ParseKeyword(byte[] data, ref int pos) {
    var start = pos;
    while (pos < data.Length && data[pos] is >= (byte)'a' and <= (byte)'z')
      ++pos;

    if (pos == start) {
      // Unknown byte, skip it
      ++pos;
      return null;
    }

    var kw = Encoding.ASCII.GetString(data, start, pos - start);
    return kw switch {
      "true" => true,
      "false" => false,
      "null" => null,
      _ => kw,
    };
  }

  private static void _SkipInteger(byte[] data, ref int pos) {
    if (pos < data.Length && data[pos] is (byte)'+' or (byte)'-')
      ++pos;

    while (pos < data.Length && data[pos] is >= (byte)'0' and <= (byte)'9')
      ++pos;
  }

  private static void _SkipKeyword(byte[] data, ref int pos) {
    while (pos < data.Length && data[pos] is >= (byte)'a' and <= (byte)'z')
      ++pos;
  }

  private static void _SkipLineEnding(byte[] data, ref int pos) {
    if (pos < data.Length && data[pos] == 13)
      ++pos;
    if (pos < data.Length && data[pos] == 10)
      ++pos;
  }

  private static bool _MatchesKeyword(byte[] data, int pos, ReadOnlySpan<byte> keyword) {
    if (pos + keyword.Length > data.Length)
      return false;

    for (var i = 0; i < keyword.Length; ++i)
      if (data[pos + i] != keyword[i])
        return false;

    return true;
  }

  private static int _FindEndstream(byte[] data, int from) {
    // Search for "endstream" from the given position
    var needle = "endstream"u8;
    for (var i = from; i + needle.Length <= data.Length; ++i) {
      var match = true;
      for (var j = 0; j < needle.Length; ++j) {
        if (data[i + j] != needle[j]) {
          match = false;
          break;
        }
      }

      if (!match)
        continue;

      // Trim trailing line ending before endstream
      var end = i;
      if (end > from && data[end - 1] == 10)
        --end;
      if (end > from && data[end - 1] == 13)
        --end;

      return end;
    }

    // Not found, return end of data
    return data.Length;
  }

  private static int? _QuickResolveInt(byte[] data, PdfRef r) {
    // Brute search for "N G obj" in the file for length references
    var pattern = $"{r.ObjectNumber} {r.Generation} obj";
    var patternBytes = Encoding.ASCII.GetBytes(pattern);

    for (var i = 0; i + patternBytes.Length < data.Length; ++i) {
      var match = true;
      for (var j = 0; j < patternBytes.Length; ++j) {
        if (data[i + j] != patternBytes[j]) {
          match = false;
          break;
        }
      }

      if (!match)
        continue;

      var pos = i + patternBytes.Length;
      SkipWhitespace(data, ref pos);
      var obj = ParseObject(data, ref pos);
      return _ToInt(obj);
    }

    return null;
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

  private static bool _IsDelimiter(byte b) =>
    b is 0 or 9 or 10 or 12 or 13 or 32
      or (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
      or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%';
}
