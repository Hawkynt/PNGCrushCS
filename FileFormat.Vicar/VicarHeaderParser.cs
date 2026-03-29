using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Vicar;

/// <summary>Parses and formats VICAR keyword=value header text.</summary>
internal static class VicarHeaderParser {

  /// <summary>Parses space-separated KEY=VALUE or KEY='quoted value' pairs from header text.</summary>
  public static Dictionary<string, string> Parse(string headerText) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var i = 0;
    var length = headerText.Length;

    while (i < length) {
      while (i < length && headerText[i] == ' ')
        ++i;

      if (i >= length)
        break;

      var eqIndex = headerText.IndexOf('=', i);
      if (eqIndex < 0)
        break;

      var key = headerText.Substring(i, eqIndex - i).Trim();
      i = eqIndex + 1;

      if (i >= length)
        break;

      string value;
      if (i < length && headerText[i] == '\'') {
        ++i;
        var closeQuote = headerText.IndexOf('\'', i);
        if (closeQuote < 0) {
          value = headerText.Substring(i);
          i = length;
        } else {
          value = headerText.Substring(i, closeQuote - i);
          i = closeQuote + 1;
        }
      } else {
        var spaceIndex = headerText.IndexOf(' ', i);
        if (spaceIndex < 0) {
          value = headerText.Substring(i).TrimEnd('\0').TrimEnd();
          i = length;
        } else {
          value = headerText.Substring(i, spaceIndex - i);
          i = spaceIndex;
        }
      }

      result[key] = value;
    }

    return result;
  }

  /// <summary>Builds a header string from labels, padded with spaces. When lblSize &lt;= 0 returns the unpadded content.</summary>
  public static string Format(Dictionary<string, string> labels, int lblSize) {
    var sb = new StringBuilder();

    foreach (var kvp in labels) {
      if (sb.Length > 0)
        sb.Append(' ');

      var needsQuote = kvp.Value.Contains(' ');
      if (needsQuote)
        sb.Append($"{kvp.Key}='{kvp.Value}'");
      else
        sb.Append($"{kvp.Key}={kvp.Value}");
    }

    if (lblSize <= 0)
      return sb.ToString();

    while (sb.Length < lblSize)
      sb.Append(' ');

    return sb.ToString(0, lblSize);
  }
}
