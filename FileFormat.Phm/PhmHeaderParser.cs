using System;
using System.Globalization;

namespace FileFormat.Phm;

/// <summary>Parses the text header of a PHM file.</summary>
internal static class PhmHeaderParser {

  /// <summary>Minimum header size: "PH\nW H\nS\n" requires at least 8 bytes.</summary>
  internal const int MinHeaderSize = 8;

  internal readonly record struct ParseResult(
    PhmColorMode ColorMode,
    int Width,
    int Height,
    float Scale,
    bool IsLittleEndian,
    int DataOffset
  );

  internal static ParseResult Parse(byte[] data) {
    var offset = 0;

    var magicLine = _ReadLine(data, ref offset);
    var colorMode = magicLine switch {
      "PH" => PhmColorMode.Rgb,
      "Ph" => PhmColorMode.Grayscale,
      _ => throw new System.IO.InvalidDataException($"Invalid PHM magic: '{magicLine}'. Expected 'PH' or 'Ph'.")
    };

    var dimensionLine = _ReadLine(data, ref offset);
    var parts = dimensionLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2)
      throw new System.IO.InvalidDataException($"Invalid PHM dimensions line: '{dimensionLine}'.");

    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) || width <= 0)
      throw new System.IO.InvalidDataException($"Invalid PHM width: '{parts[0]}'.");

    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) || height <= 0)
      throw new System.IO.InvalidDataException($"Invalid PHM height: '{parts[1]}'.");

    var scaleLine = _ReadLine(data, ref offset);
    if (!float.TryParse(scaleLine, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawScale) || rawScale == 0f)
      throw new System.IO.InvalidDataException($"Invalid PHM scale factor: '{scaleLine}'.");

    var isLittleEndian = rawScale < 0f;
    var scale = Math.Abs(rawScale);

    return new(colorMode, width, height, scale, isLittleEndian, offset);
  }

  private static string _ReadLine(byte[] data, ref int offset) {
    var start = offset;
    while (offset < data.Length && data[offset] != (byte)'\n')
      ++offset;

    if (offset >= data.Length)
      throw new System.IO.InvalidDataException("Unexpected end of PHM header.");

    var line = System.Text.Encoding.ASCII.GetString(data, start, offset - start);
    ++offset;
    return line;
  }
}
