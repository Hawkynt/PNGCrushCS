using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Illustrator;

/// <summary>Parses the native Illustrator 6/7 <c>XI</c> embedded raster object.</summary>
internal static class AiNativeRaster {

  private const string _Begin = "%AI5_BeginRaster";
  private const string _End = "%AI5_EndRaster";

  public static bool TryDecode(ReadOnlySpan<byte> data, out RawImage image) {
    image = null!;
    var text = Encoding.Latin1.GetString(data);
    var begin = text.IndexOf(_Begin, StringComparison.Ordinal);
    if (begin < 0)
      return false;

    var xi = text.IndexOf(" XI", begin, StringComparison.Ordinal);
    if (xi < 0)
      throw new InvalidDataException("An Illustrator raster begins but has no XI image operator.");

    var lineStart = text.LastIndexOfAny(['\r', '\n'], xi);
    lineStart = lineStart < begin ? begin + _Begin.Length : lineStart + 1;
    var declaration = text[lineStart..(xi + 3)].Trim();
    var closeMatrix = declaration.IndexOf(']');
    if (closeMatrix < 0)
      throw new InvalidDataException("An Illustrator XI raster has no image matrix.");

    var operands = declaration[(closeMatrix + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (operands.Length < 13 || !operands[^1].Equals("XI", StringComparison.Ordinal))
      throw new InvalidDataException($"An Illustrator XI raster has {operands.Length} operands where the version-6 form needs twelve plus XI.");

    var width = _Int(operands[4], "width");
    var height = _Int(operands[5], "height");
    var bits = _Int(operands[6], "sample depth");
    var imageType = _Int(operands[7], "image type");
    var alphaChannels = _Int(operands[8], "alpha channel count");
    var encoding = _Int(operands[10], "encoding");
    var imageMask = _Int(operands[11], "image mask flag");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An Illustrator raster states {width}x{height} pixels.");
    if (bits != 8 || imageType != 3 || alphaChannels != 0 || imageMask != 0)
      throw new InvalidDataException(
        $"This Illustrator raster is {bits}-bit type {imageType} with {alphaChannels} alpha channel(s) and mask {imageMask}; " +
        "the native reader currently accepts opaque 8-bit RGB XI objects.");
    if (encoding != 0)
      throw new InvalidDataException("This Illustrator XI raster is binary; the native reader currently accepts the specification's ASCII-hex form.");

    var dataStart = _AfterLine(text, xi + 3);
    var end = text.IndexOf(_End, dataStart, StringComparison.Ordinal);
    if (end < 0)
      throw new InvalidDataException("An Illustrator XI raster has no %AI5_EndRaster marker.");

    var expected = checked(width * height * 3);
    var bytes = _HexPayload(text.AsSpan(dataStart, end - dataStart), expected);
    image = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = bytes };
    return true;
  }

  private static int _Int(string text, string name) {
    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
      throw new InvalidDataException($"An Illustrator XI raster writes its {name} as {text}.");
    return value;
  }

  private static int _AfterLine(string text, int at) {
    while (at < text.Length && text[at] is not ('\r' or '\n'))
      ++at;
    if (at < text.Length && text[at] == '\r') ++at;
    if (at < text.Length && text[at] == '\n') ++at;
    return at;
  }

  private static byte[] _HexPayload(ReadOnlySpan<char> text, int expected) {
    var result = new byte[expected];
    var filled = 0;
    var high = -1;
    var atLineStart = true;

    foreach (var ch in text) {
      if (ch is '\r' or '\n') {
        atLineStart = true;
        continue;
      }

      if (atLineStart && char.IsWhiteSpace(ch))
        continue;
      if (atLineStart && ch == '%') {
        atLineStart = false;
        continue;
      }
      atLineStart = false;

      if (char.IsWhiteSpace(ch))
        continue;
      var nibble = ch switch {
        >= '0' and <= '9' => ch - '0',
        >= 'A' and <= 'F' => ch - 'A' + 10,
        >= 'a' and <= 'f' => ch - 'a' + 10,
        _ => -1,
      };
      if (nibble < 0)
        continue;

      if (high < 0) {
        high = nibble;
        continue;
      }

      if (filled >= result.Length)
        throw new InvalidDataException("An Illustrator XI raster contains more hexadecimal sample data than its dimensions declare.");
      result[filled++] = (byte)((high << 4) | nibble);
      high = -1;
    }

    if (high >= 0 || filled != expected)
      throw new InvalidDataException($"An Illustrator XI raster needs {expected} sample bytes and carries {filled} complete byte(s).");
    return result;
  }
}
