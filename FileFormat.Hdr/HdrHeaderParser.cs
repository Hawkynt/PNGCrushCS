using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Hdr;

/// <summary>Parses the text header of a Radiance HDR file.</summary>
internal static class HdrHeaderParser {

  private const string _MAGIC_FULL = "#?RADIANCE";
  private const string _MAGIC_SHORT = "#?";
  private const string _FORMAT_PREFIX = "FORMAT=";
  private const string _EXPOSURE_PREFIX = "EXPOSURE=";

  public static (int Width, int Height, float Exposure, int DataOffset) Parse(byte[] data) {
    var text = Encoding.ASCII.GetString(data);

    if (!text.StartsWith(_MAGIC_FULL, StringComparison.Ordinal) && !text.StartsWith(_MAGIC_SHORT, StringComparison.Ordinal))
      throw new System.IO.InvalidDataException("Invalid HDR magic: expected '#?RADIANCE' or '#?'.");

    var exposure = 1.0f;
    var offset = 0;

    // Find end of header (empty line)
    while (offset < text.Length) {
      var lineEnd = text.IndexOf('\n', offset);
      if (lineEnd < 0)
        throw new System.IO.InvalidDataException("Unterminated HDR header: no empty line found.");

      var line = text.Substring(offset, lineEnd - offset).TrimEnd('\r');
      offset = lineEnd + 1;

      if (line.Length == 0)
        break;

      if (line.StartsWith(_EXPOSURE_PREFIX, StringComparison.OrdinalIgnoreCase))
        if (float.TryParse(line.Substring(_EXPOSURE_PREFIX.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var exp))
          exposure *= exp;
    }

    // Parse resolution string
    if (offset >= text.Length)
      throw new System.IO.InvalidDataException("Missing resolution string in HDR file.");

    var resLineEnd = text.IndexOf('\n', offset);
    if (resLineEnd < 0)
      resLineEnd = text.Length;

    var resLine = text.Substring(offset, resLineEnd - offset).TrimEnd('\r');
    var dataOffset = resLineEnd + 1;

    var (width, height) = _ParseResolution(resLine);

    // Convert character offset to byte offset
    var byteOffset = Encoding.ASCII.GetByteCount(text.AsSpan(0, dataOffset));

    return (width, height, exposure, byteOffset);
  }

  private static (int Width, int Height) _ParseResolution(string resLine) {
    var parts = resLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 4)
      throw new System.IO.InvalidDataException($"Invalid resolution string: '{resLine}'.");

    // Expected: -Y height +X width
    if (parts[0] == "-Y" && parts[2] == "+X") {
      if (int.TryParse(parts[1], out var height) && int.TryParse(parts[3], out var width))
        return (width, height);
    }

    throw new System.IO.InvalidDataException($"Unsupported resolution format: '{resLine}'. Only '-Y height +X width' is supported.");
  }
}
