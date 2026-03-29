using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Interfile;

/// <summary>Parsed Interfile header containing image dimensions and format information.</summary>
internal sealed record InterfileHeader {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int BytesPerPixel { get; init; }
  public required string NumberFormat { get; init; }
  public required int DataOffset { get; init; }
}

/// <summary>Parses and formats Interfile text headers.</summary>
internal static class InterfileHeaderParser {

  private const string _KEY_MATRIX_SIZE_1 = "matrix size [1]";
  private const string _KEY_MATRIX_SIZE_2 = "matrix size [2]";
  private const string _KEY_NUMBER_FORMAT = "number format";
  private const string _KEY_BYTES_PER_PIXEL = "number of bytes per pixel";
  private const string _KEY_END = "end of interfile";

  public static InterfileHeader Parse(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    var text = Encoding.ASCII.GetString(data);
    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var offset = 0;
    var foundEnd = false;

    while (offset < text.Length) {
      var lineEnd = text.IndexOf('\n', offset);
      if (lineEnd < 0)
        lineEnd = text.Length;

      var line = text.Substring(offset, lineEnd - offset).TrimEnd('\r');
      offset = lineEnd + 1;

      if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
        continue;

      // Parse "key := value" or "!key := value"
      var sepIndex = line.IndexOf(":=", StringComparison.Ordinal);
      if (sepIndex < 0)
        continue;

      var key = line.Substring(0, sepIndex).TrimStart('!').Trim();
      var value = line.Substring(sepIndex + 2).Trim();

      // Check for end marker (key is "END OF INTERFILE")
      if (key.Equals(_KEY_END, StringComparison.OrdinalIgnoreCase)) {
        foundEnd = true;
        break;
      }

      fields[key] = value;
    }

    if (!foundEnd)
      // If no explicit end marker, treat the entire text as header and pixel data follows
      offset = text.Length;

    var width = 0;
    var height = 0;
    var bytesPerPixel = 1;
    var numberFormat = "unsigned integer";

    if (fields.TryGetValue(_KEY_MATRIX_SIZE_1, out var widthStr) && int.TryParse(widthStr, out var w))
      width = w;

    if (fields.TryGetValue(_KEY_MATRIX_SIZE_2, out var heightStr) && int.TryParse(heightStr, out var h))
      height = h;

    if (fields.TryGetValue(_KEY_BYTES_PER_PIXEL, out var bppStr) && int.TryParse(bppStr, out var bpp))
      bytesPerPixel = bpp;

    if (fields.TryGetValue(_KEY_NUMBER_FORMAT, out var numFmt))
      numberFormat = numFmt;

    return new InterfileHeader {
      Width = width,
      Height = height,
      BytesPerPixel = bytesPerPixel,
      NumberFormat = numberFormat,
      DataOffset = Math.Min(offset, data.Length),
    };
  }

  public static byte[] Format(InterfileFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();
    sb.Append("!INTERFILE :=\n");
    sb.Append("!imaging modality := nucmed\n");
    sb.Append("!number format := ").Append(file.NumberFormat).Append('\n');
    sb.Append("!number of bytes per pixel := ").Append(file.BytesPerPixel).Append('\n');
    sb.Append("!matrix size [1] := ").Append(file.Width).Append('\n');
    sb.Append("!matrix size [2] := ").Append(file.Height).Append('\n');
    sb.Append("!END OF INTERFILE :=\n");

    return Encoding.ASCII.GetBytes(sb.ToString());
  }

  /// <summary>Extracts width from header fields.</summary>
  internal static int ExtractWidth(byte[] data) => Parse(data).Width;

  /// <summary>Extracts height from header fields.</summary>
  internal static int ExtractHeight(byte[] data) => Parse(data).Height;

  /// <summary>Extracts bytes per pixel from header fields.</summary>
  internal static int ExtractBytesPerPixel(byte[] data) => Parse(data).BytesPerPixel;

  /// <summary>Extracts number format from header fields.</summary>
  internal static string ExtractNumberFormat(byte[] data) => Parse(data).NumberFormat;

  /// <summary>Extracts data offset from header fields.</summary>
  internal static int ExtractDataOffset(byte[] data) => Parse(data).DataOffset;
}
