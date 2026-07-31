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

    // The header ends with the four bytes "\f\n:\x1A" — a colon on its own line, then Ctrl-Z. What
    // was looked for instead was a *line* equal to ":", which never matches: the colon is followed
    // immediately by the 0x1A and the image's binary data, not by a newline, so the "line" holding it
    // is the colon plus whatever bytes ran to the next 0x0A. Anchoring on the colon-then-0x1A pair is
    // also what keeps the search off the colons inside values such as "date:create=...".
    var (terminator, afterTerminator) = _IndexOfTerminator(data);
    if (terminator < 0)
      throw new InvalidDataException("MIFF header terminator ':' not found.");

    dataOffset = afterTerminator;

    // Attributes are whitespace-separated, not one to a line: "columns=37 rows=23 depth=8" is three
    // of them. Reading a line as a single key=value made every attribute after the first part of the
    // one before it, so "columns" came out as "37 rows=23 depth=8".
    _ParseAttributes(text.AsSpan(0, terminator), result);
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

  /// <summary>
  /// Where the header's closing colon sits and where the pixels start after it, or (-1, -1).
  /// </summary>
  /// <remarks>
  /// The colon has to be the one that ends the header rather than one inside a value — attributes
  /// such as "date:create=..." are full of them — so it is only accepted where it stands at the start
  /// of a line and is followed by the 0x1A that closes the header. Writers differ on what sits
  /// between the two: ImageMagick puts the 0x1A straight after the colon, while this library's own
  /// writer puts a newline in first, and both are read here.
  /// </remarks>
  private static (int Colon, int Data) _IndexOfTerminator(byte[] data) {
    for (var i = 0; i < data.Length; ++i) {
      if (data[i] != (byte)':')
        continue;

      if (i > 0 && data[i - 1] is not ((byte)'\n' or (byte)'\f'))
        continue;

      var j = i + 1;
      while (j < data.Length && data[j] is (byte)'\r' or (byte)'\n')
        ++j;

      if (j < data.Length && data[j] == _TERMINATOR_BYTE)
        return (i, j + 1);
    }

    return (-1, -1);
  }

  /// <summary>
  /// Reads the header's whitespace-separated key=value attributes.
  /// </summary>
  /// <remarks>
  /// A value wrapped in braces may contain spaces and newlines — that is how a comment or a profile
  /// is carried — so those are read to their closing brace rather than to the next space.
  /// </remarks>
  private static void _ParseAttributes(ReadOnlySpan<char> header, Dictionary<string, string> into) {
    var i = 0;
    while (i < header.Length) {
      while (i < header.Length && char.IsWhiteSpace(header[i]))
        ++i;

      var keyStart = i;
      while (i < header.Length && header[i] != '=' && !char.IsWhiteSpace(header[i]))
        ++i;

      if (i >= header.Length || header[i] != '=' || i == keyStart) {
        // Not an attribute; skip to the next whitespace and try again.
        while (i < header.Length && !char.IsWhiteSpace(header[i]))
          ++i;

        continue;
      }

      var key = header[keyStart..i].ToString();
      ++i; // '='

      string value;
      if (i < header.Length && header[i] == '{') {
        var close = header[i..].IndexOf('}');
        if (close < 0) {
          value = header[(i + 1)..].ToString();
          i = header.Length;
        } else {
          value = header.Slice(i + 1, close - 1).ToString();
          i += close + 1;
        }
      } else {
        var valueStart = i;
        while (i < header.Length && !char.IsWhiteSpace(header[i]))
          ++i;

        value = header[valueStart..i].ToString();
      }

      into[key] = value;
    }
  }
}
