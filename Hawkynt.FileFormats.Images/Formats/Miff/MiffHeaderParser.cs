using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Miff;

/// <summary>Parses and formats MIFF text headers.</summary>
internal static class MiffHeaderParser {

  private const string _MAGIC = "id=ImageMagick";
  private const byte _TERMINATOR_BYTE = 0x1A;

  public static Dictionary<string, string> Parse(byte[] data, out int dataOffset) {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var text = Encoding.ASCII.GetString(data);

    // Find the terminator line ":" followed by newline and 0x1A
    var offset = 0;
    var foundTerminator = false;

    while (offset < text.Length) {
      var lineEnd = text.IndexOf('\n', offset);
      if (lineEnd < 0)
        lineEnd = text.Length;

      var line = text.Substring(offset, lineEnd - offset).TrimEnd('\r');

      // The header ends at a colon, and the colon is not alone on its line: a writer emits a form
      // feed, a newline, the colon and then the control byte, with the samples following it
      // immediately. Looking for a line equal to ":" therefore reads the colon together with the
      // whole binary payload and never matches.
      var colon = line.IndexOf(':');
      if (colon >= 0 && line[..colon].TrimEnd('\f', ' ', '\t').Length == 0) {
        offset += colon + 1;
        foundTerminator = true;
        break;
      }

      offset = lineEnd + 1;

      _ReadFields(line, result);
    }

    if (!foundTerminator)
      throw new InvalidDataException("MIFF header terminator ':' not found.");

    // What follows the colon varies: a writer may put the control byte straight after it, or end
    // the line first. Skipping either leaves the samples in both cases.
    while (offset < data.Length && data[offset] is (byte)'\r' or (byte)'\n' or _TERMINATOR_BYTE)
      ++offset;

    dataOffset = offset;
    return result;
  }

  public static byte[] Format(MiffFile file) {
    var sb = new StringBuilder();
    sb.Append("id=ImageMagick\n");
    sb.Append("class=").Append(file.ColorClass == MiffColorClass.PseudoClass ? "PseudoClass" : "DirectClass").Append('\n');
    sb.Append("columns=").Append(file.Width).Append('\n');
    sb.Append("rows=").Append(file.Height).Append('\n');
    sb.Append("depth=").Append(file.Depth).Append('\n');
    sb.Append("type=").Append(file.Type).Append('\n');
    sb.Append("colorspace=").Append(file.Colorspace).Append('\n');

    if (file.Compression != MiffCompression.None)
      sb.Append("compression=").Append(file.Compression == MiffCompression.Rle ? "RLE" : "Zip").Append('\n');
    else
      sb.Append("compression=None\n");

    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null) {
      var colorCount = file.Palette.Length / 3;
      sb.Append("colors=").Append(colorCount).Append('\n');
    }

    sb.Append(":\n");

    var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
    var result = new byte[headerBytes.Length + 1];
    Array.Copy(headerBytes, result, headerBytes.Length);
    result[headerBytes.Length] = _TERMINATOR_BYTE;

    return result;
  }

  /// <summary>Reads every key and value on one header line.</summary>
  /// <remarks>
  /// A line carries as many pairs as fit — <c>columns=13 rows=7 depth=16</c> is one line, not
  /// three — so a parser that splits on the first equals sign takes the rest of the line as one
  /// value and loses every field after the first.
  /// <para/>
  /// A value ordinarily runs to the next space, but one wrapped in braces may contain them, which
  /// is how a comment or a text chunk is carried.
  /// </remarks>
  private static void _ReadFields(string line, Dictionary<string, string> into) {
    var at = 0;
    while (at < line.Length) {
      while (at < line.Length && char.IsWhiteSpace(line[at]))
        ++at;

      var keyStart = at;
      while (at < line.Length && line[at] != '=' && !char.IsWhiteSpace(line[at]))
        ++at;

      if (at >= line.Length || line[at] != '=' || at == keyStart)
        return;

      var key = line[keyStart..at];
      ++at;

      string value;
      if (at < line.Length && line[at] == '{') {
        var close = line.IndexOf('}', at);
        if (close < 0)
          return;

        value = line[(at + 1)..close];
        at = close + 1;
      } else {
        var valueStart = at;
        while (at < line.Length && !char.IsWhiteSpace(line[at]))
          ++at;

        value = line[valueStart..at];
      }

      into[key] = value;
    }
  }
}
