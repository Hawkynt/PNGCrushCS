using System;
using System.Collections.Generic;
using System.Globalization;

namespace FileFormat.Xpm;

/// <summary>Parses XPM3 C source text into structured data.</summary>
internal static class XpmTextParser {

  private const string _MAGIC = "/* XPM */";
  private const string _TRANSPARENT = "None";

  /// <summary>Parses XPM3 text and returns an <see cref="XpmFile"/>.</summary>
  internal static XpmFile Parse(string text) {
    ArgumentNullException.ThrowIfNull(text);

    var trimmed = text.AsSpan().Trim();
    if (!trimmed.StartsWith(_MAGIC.AsSpan()))
      throw new InvalidOperationException("Invalid XPM magic comment.");

    var strings = _ExtractQuotedStrings(text);
    if (strings.Count < 1)
      throw new InvalidOperationException("No quoted strings found in XPM data.");

    // Parse values line: "width height numColors charsPerPixel"
    var valuesParts = strings[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (valuesParts.Length < 4)
      throw new InvalidOperationException("Invalid XPM values line.");

    var width = int.Parse(valuesParts[0], CultureInfo.InvariantCulture);
    var height = int.Parse(valuesParts[1], CultureInfo.InvariantCulture);
    var numColors = int.Parse(valuesParts[2], CultureInfo.InvariantCulture);
    var charsPerPixel = int.Parse(valuesParts[3], CultureInfo.InvariantCulture);

    if (width <= 0 || height <= 0)
      throw new InvalidOperationException($"Invalid XPM dimensions: {width}x{height}.");

    if (numColors <= 0)
      throw new InvalidOperationException($"Invalid XPM color count: {numColors}.");

    if (charsPerPixel <= 0)
      throw new InvalidOperationException($"Invalid XPM chars per pixel: {charsPerPixel}.");

    var expectedStrings = 1 + numColors + height;
    if (strings.Count < expectedStrings)
      throw new InvalidOperationException($"Expected {expectedStrings} quoted strings but found {strings.Count}.");

    // Parse color definitions
    var palette = new byte[numColors * 3];
    int? transparentIndex = null;
    var charToIndex = new Dictionary<string, int>(numColors);

    for (var i = 0; i < numColors; ++i) {
      var colorLine = strings[1 + i];
      if (colorLine.Length < charsPerPixel)
        throw new InvalidOperationException($"Color line {i} too short.");

      var chars = colorLine[..charsPerPixel];
      var rest = colorLine[charsPerPixel..].Trim();

      // Find "c" key (color visual)
      var colorValue = _ExtractColorValue(rest);

      if (string.Equals(colorValue, _TRANSPARENT, StringComparison.OrdinalIgnoreCase)) {
        transparentIndex = i;
        // Leave palette entry as 0,0,0 for transparent
      } else if (colorValue.StartsWith('#')) {
        // XPM allows a hex triple at several widths — 4, 8, 12 and 16 bits a channel — and only the
        // 8-bit one was read. The wider ones are taken from their most significant byte.
        if (!_TryParseHex(colorValue[1..], out var r, out var g, out var b))
          throw new InvalidOperationException($"Unsupported hex color format: {colorValue}");

        palette[i * 3] = r;
        palette[(i * 3) + 1] = g;
        palette[(i * 3) + 2] = b;
      } else if (XpmColorNames.TryGet(colorValue, out var named, out var namedG, out var namedB)) {
        // A name from X11's colour database, which is what an XPM writer reaches for first.
        palette[i * 3] = named;
        palette[(i * 3) + 1] = namedG;
        palette[(i * 3) + 2] = namedB;
      } else
        throw new InvalidOperationException($"Unsupported color value: {colorValue}");

      charToIndex[chars] = i;
    }

    // Parse pixel rows
    var pixelData = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var row = strings[1 + numColors + y];
      if (row.Length < width * charsPerPixel)
        throw new InvalidOperationException($"Pixel row {y} too short: expected {width * charsPerPixel} chars but got {row.Length}.");

      for (var x = 0; x < width; ++x) {
        var chars = row.Substring(x * charsPerPixel, charsPerPixel);
        if (!charToIndex.TryGetValue(chars, out var index))
          throw new InvalidOperationException($"Unknown pixel character '{chars}' at row {y}, column {x}.");

        pixelData[y * width + x] = (byte)index;
      }
    }

    // Extract variable name
    var name = _ExtractVariableName(text);

    return new XpmFile {
      Width = width,
      Height = height,
      CharsPerPixel = charsPerPixel,
      Name = name,
      Palette = palette,
      PaletteColorCount = numColors,
      TransparentIndex = transparentIndex,
      PixelData = pixelData
    };
  }

  private static string _ExtractColorValue(string rest) {
    // Format: "c #RRGGBB" or "c None" or "c colorname"
    // May also have other visual keys like "m" (mono), "g" (gray), "s" (symbolic)
    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (var i = 0; i < parts.Length - 1; ++i)
      if (string.Equals(parts[i], "c", StringComparison.OrdinalIgnoreCase))
        return parts[i + 1];

    throw new InvalidOperationException($"No 'c' color key found in: {rest}");
  }

  private static List<string> _ExtractQuotedStrings(string text) {
    var result = new List<string>();
    var i = 0;
    while (i < text.Length) {
      var start = text.IndexOf('"', i);
      if (start < 0)
        break;

      var end = text.IndexOf('"', start + 1);
      if (end < 0)
        break;

      result.Add(text[(start + 1)..end]);
      i = end + 1;
    }

    return result;
  }

  private static string _ExtractVariableName(string text) {
    // Look for: static char *name[] = {
    // Must skip the "/* XPM */" comment, so search for "static"
    var staticIdx = text.IndexOf("static", StringComparison.Ordinal);
    if (staticIdx < 0)
      return "image";

    var starIdx = text.IndexOf('*', staticIdx);
    if (starIdx < 0)
      return "image";

    var endIdx = text.IndexOf('[', starIdx);
    if (endIdx < 0)
      return "image";

    var name = text[(starIdx + 1)..endIdx].Trim();
    return name.Length > 0 ? name : "image";
  }

  /// <summary>A hex triple at any of the widths XPM permits: 4, 8, 12 or 16 bits a channel.</summary>
  private static bool _TryParseHex(string hex, out byte r, out byte g, out byte b) {
    r = g = b = 0;
    if (hex.Length % 3 != 0)
      return false;

    var perChannel = hex.Length / 3;
    if (perChannel is < 1 or > 4)
      return false;

    if (!_TryChannel(hex.AsSpan(0, perChannel), out r)
        || !_TryChannel(hex.AsSpan(perChannel, perChannel), out g)
        || !_TryChannel(hex.AsSpan(perChannel * 2, perChannel), out b))
      return false;

    return true;
  }

  /// <summary>One channel, scaled to eight bits from whatever width it was written at.</summary>
  private static bool _TryChannel(ReadOnlySpan<char> text, out byte value) {
    value = 0;
    if (!ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
      return false;

    value = text.Length switch {
      1 => (byte)((raw << 4) | raw),   // #RGB doubles each nibble
      2 => (byte)raw,
      3 => (byte)(raw >> 4),
      _ => (byte)(raw >> 8),
    };

    return true;
  }
}
