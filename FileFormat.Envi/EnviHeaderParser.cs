using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Envi;

/// <summary>Parses and formats ENVI text header keyword=value lines.</summary>
internal static class EnviHeaderParser {

  /// <summary>Parses ENVI header text from raw data, returning all key-value pairs and the byte offset where pixel data begins.</summary>
  public static (Dictionary<string, string> fields, int dataOffset) Parse(byte[] data) {
    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var lastHeaderEnd = 0;
    var pos = 0;
    var firstLine = true;
    string? pendingKey = null;
    var pendingValue = new StringBuilder();
    var inBraces = false;

    while (pos < data.Length) {
      // find end of current line
      var lineStart = pos;
      while (pos < data.Length && data[pos] != (byte)'\n' && data[pos] != (byte)'\r')
        ++pos;

      var lineBytes = pos - lineStart;
      var rawLine = Encoding.ASCII.GetString(data, lineStart, lineBytes);

      // consume line terminator
      if (pos < data.Length && data[pos] == (byte)'\r')
        ++pos;
      if (pos < data.Length && data[pos] == (byte)'\n')
        ++pos;

      // skip the "ENVI" magic line
      if (firstLine) {
        firstLine = false;
        lastHeaderEnd = pos;
        continue;
      }

      var line = rawLine.Trim();

      // handle multiline brace continuation
      if (inBraces) {
        pendingValue.Append(' ').Append(line);
        if (line.Contains('}')) {
          inBraces = false;
          if (pendingKey != null)
            fields[pendingKey] = pendingValue.ToString().Trim();
          pendingKey = null;
        }

        lastHeaderEnd = pos;
        continue;
      }

      // empty lines between header fields are skipped but do NOT advance the header end
      // because binary pixel data might start with 0x0A (newline) and look like an empty line
      if (line.Length == 0)
        continue;

      // a line without '=' and not in braces means we've hit non-header data
      var eqIndex = line.IndexOf('=');
      if (eqIndex < 0)
        break;

      var key = line.Substring(0, eqIndex).Trim();
      var value = line.Substring(eqIndex + 1).Trim();

      // check for multiline brace value
      if (value.Contains('{') && !value.Contains('}')) {
        pendingKey = key;
        pendingValue.Clear().Append(value);
        inBraces = true;
        lastHeaderEnd = pos;
        continue;
      }

      // strip braces from single-line brace values
      if (value.StartsWith('{') && value.EndsWith('}'))
        value = value[1..^1].Trim();

      fields[key] = value;
      lastHeaderEnd = pos;
    }

    // if still in braces, store what we have
    if (inBraces && pendingKey != null)
      fields[pendingKey] = pendingValue.ToString().Trim();

    return (fields, lastHeaderEnd);
  }

  /// <summary>Extracts an integer value from parsed header fields.</summary>
  public static int GetInt(Dictionary<string, string> fields, string key, int defaultValue = 0)
    => fields.TryGetValue(key, out var val) && int.TryParse(val.Trim(), out var result) ? result : defaultValue;

  /// <summary>Extracts a string value from parsed header fields.</summary>
  public static string GetString(Dictionary<string, string> fields, string key, string defaultValue = "")
    => fields.TryGetValue(key, out var val) ? val.Trim() : defaultValue;

  /// <summary>Parses the interleave string to an enum value.</summary>
  public static EnviInterleave ParseInterleave(string value) => value.Trim().ToLowerInvariant() switch {
    "bsq" => EnviInterleave.Bsq,
    "bip" => EnviInterleave.Bip,
    "bil" => EnviInterleave.Bil,
    _ => EnviInterleave.Bsq
  };

  /// <summary>Formats an ENVI header as bytes.</summary>
  public static byte[] Format(int width, int height, int bands, int dataType, EnviInterleave interleave, int byteOrder) {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append($"samples = {width}\n");
    sb.Append($"lines = {height}\n");
    sb.Append($"bands = {bands}\n");
    sb.Append($"data type = {dataType}\n");
    sb.Append($"interleave = {_InterleaveToString(interleave)}\n");
    sb.Append($"byte order = {byteOrder}\n");
    sb.Append($"header offset = 0\n");
    return Encoding.ASCII.GetBytes(sb.ToString());
  }

  private static string _InterleaveToString(EnviInterleave interleave) => interleave switch {
    EnviInterleave.Bsq => "bsq",
    EnviInterleave.Bip => "bip",
    EnviInterleave.Bil => "bil",
    _ => "bsq"
  };
}
