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

  /// <summary>The encodings a Radiance <c>FORMAT=</c> line may name.</summary>
  private static readonly string[] _RADIANCE_FORMATS = ["32-bit_rle_rgbe", "32-bit_rle_xyze"];

  /// <summary>Whether the header opens the way a Radiance file does.</summary>
  /// <remarks>
  /// Ordinarily that is the <c>#?</c> line. XnView's nconvert omits it and opens with
  /// <c>FORMAT=32-bit_rle_rgbe</c> instead, which no other format writes and which nothing but
  /// Radiance can follow — the resolution line and RGBE quads behind it are exactly what the
  /// format calls for, and putting the missing line back makes nconvert and ImageMagick read the
  /// same bytes. That line therefore stands as a signature of its own; it does not stand in for
  /// dropping the test, and a file opening with neither is still refused.
  /// </remarks>
  public static bool HasRadianceHeader(ReadOnlySpan<byte> data) {
    if (data.Length >= _MAGIC_SHORT.Length && data[0] == (byte)'#' && data[1] == (byte)'?')
      return true;

    var probe = Encoding.ASCII.GetString(data[..Math.Min(data.Length, 64)]);
    if (!probe.StartsWith(_FORMAT_PREFIX, StringComparison.OrdinalIgnoreCase))
      return false;

    var lineEnd = probe.IndexOf('\n');
    var named = (lineEnd < 0 ? probe : probe[..lineEnd])[_FORMAT_PREFIX.Length..].Trim();

    return Array.Exists(_RADIANCE_FORMATS, f => named.Equals(f, StringComparison.OrdinalIgnoreCase));
  }

  public static (int Width, int Height, float Exposure, int DataOffset) Parse(byte[] data) {
    var text = Encoding.ASCII.GetString(data);

    if (!HasRadianceHeader(data))
      throw new System.IO.InvalidDataException("Invalid HDR magic: expected '#?RADIANCE', '#?' or a Radiance 'FORMAT=' line.");

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
