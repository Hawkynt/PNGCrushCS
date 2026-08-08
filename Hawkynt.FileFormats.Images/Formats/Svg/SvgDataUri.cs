using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.Svg;

/// <summary>Reads and writes the <c>data:</c> URIs an SVG carries a raster in.</summary>
/// <remarks>
/// <c>data:image/png;base64,</c> and the rest of the payload. That is how a drawing holds a picture
/// without holding a reference to a file, and it is the only form read here: an <c>image</c> naming
/// a path or a URL points at something the document does not contain.
/// </remarks>
public static class SvgDataUri {

  /// <summary>What a PNG payload is introduced by.</summary>
  public const string PngPrefix = "data:image/png;base64,";

  /// <summary>The most bytes a payload may decode to, so a long one cannot exhaust memory.</summary>
  private const int _MaxPayload = 1 << 26;

  /// <summary>The picture a data URI carries, or null where it carries nothing this decodes.</summary>
  public static RawImage? Decode(string? href) {
    if (href == null)
      return null;

    var uri = href.Trim();
    if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
      return null;

    var comma = uri.IndexOf(',');
    if (comma < 0)
      return null;

    var header = uri[5..comma];
    if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
      return null;

    byte[] payload;
    try {
      payload = Convert.FromBase64String(_WithoutWhitespace(uri[(comma + 1)..]));
    } catch (FormatException) {
      return null;
    }

    if (payload.Length is 0 or > _MaxPayload)
      return null;

    // The bytes say what they are; the media type in the header is what the writer claimed and is
    // not worth trusting over the file itself.
    try {
      if (payload.Length > 8 && payload[0] == 0x89 && payload[1] == 'P' && payload[2] == 'N' && payload[3] == 'G')
        return PngFile.ToRawImage(PngReader.FromBytes(payload));

      if (payload.Length > 3 && payload[0] == 0xFF && payload[1] == 0xD8)
        return JpegFile.ToRawImage(JpegReader.FromBytes(payload));
    } catch (InvalidDataException) {
      return null;
    } catch (NotSupportedException) {
      return null;
    }

    return null;
  }

  /// <summary>The picture as the data URI an <c>image</c> element carries it in.</summary>
  public static string EncodePng(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return PngPrefix + Convert.ToBase64String(PngWriter.ToBytes(PngFile.FromRawImage(image)));
  }

  /// <summary>Base64 in an attribute may be wrapped over lines, and the decoder wants it in one.</summary>
  private static string _WithoutWhitespace(string value) {
    var buffer = new char[value.Length];
    var length = 0;
    foreach (var c in value)
      if (!char.IsWhiteSpace(c))
        buffer[length++] = c;

    return new(buffer, 0, length);
  }
}
