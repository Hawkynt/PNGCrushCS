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
      offset = lineEnd + 1;

      if (line == ":") {
        foundTerminator = true;
        break;
      }

      var eqIdx = line.IndexOf('=');
      if (eqIdx > 0) {
        var key = line.Substring(0, eqIdx).Trim();
        var value = line.Substring(eqIdx + 1).Trim();
        result[key] = value;
      }
    }

    if (!foundTerminator)
      throw new InvalidDataException("MIFF header terminator ':' not found.");

    // The 0x1A byte follows the terminator line
    if (offset < data.Length && data[offset] == _TERMINATOR_BYTE)
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
}
