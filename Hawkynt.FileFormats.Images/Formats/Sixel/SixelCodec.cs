using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Encodes and decodes SIXEL pixel data.</summary>
internal static class SixelCodec {

  private const char _SIXEL_BASE = '?'; // 63
  private const char _COLOR_INTRO = '#';
  private const char _RASTER_ATTRIBUTES = '"';
  private const char _CARRIAGE_RETURN = '$';
  private const char _NEW_LINE = '-';
  private const char _RLE_INTRO = '!';
  private const int _BAND_HEIGHT = 6;
  private const int _MAX_REPEAT = 1_000_000;
  private const int _MAX_DIMENSION = 65_535;
  private const int _MAX_PIXELS = 100_000_000;

  /// <summary>Decodes SIXEL body text into indexed pixel data and palette.</summary>
  public static byte[] Decode(
    string body,
    out int width,
    out int height,
    out byte[]? palette,
    out int paletteColorCount,
    int backgroundMode = 1
  ) {
    ArgumentNullException.ThrowIfNull(body);
    if (backgroundMode is not (0 or 1 or 2))
      throw new InvalidDataException($"Invalid SIXEL background mode P2={backgroundMode}.");

    var colors = _CreateDefaultColors();
    var pixels = new Dictionary<(int X, int Y), byte>();
    var maxX = -1;
    var maxY = -1;
    var rasterWidth = 0;
    var rasterHeight = 0;
    var x = 0;
    var bandY = 0;
    byte currentColor = 0;
    var i = 0;

    while (i < body.Length) {
      var ch = body[i++];

      if (ch == _COLOR_INTRO) {
        var parameters = _ReadParameters(body, ref i);
        if (parameters.Length == 0)
          throw new InvalidDataException("SIXEL color introducer requires a color-map index.");

        var colorIndex = parameters[0];
        if (colorIndex is < 0 or > 255)
          throw new InvalidDataException($"SIXEL color-map index {colorIndex} is outside 0..255.");

        if (parameters.Length > 1) {
          if (parameters.Length < 5)
            throw new InvalidDataException("SIXEL color definition requires mode and three coordinates.");

          colors[colorIndex] = parameters[1] switch {
            (int)SixelColorMode.Hls => _HlsToRgb(parameters[2], parameters[3], parameters[4]),
            (int)SixelColorMode.Rgb => _RgbPercentToRgb(parameters[2], parameters[3], parameters[4]),
            var mode => throw new InvalidDataException($"Unsupported SIXEL color coordinate mode {mode}.")
          };
        }

        currentColor = (byte)colorIndex;
        continue;
      }

      if (ch == _RASTER_ATTRIBUTES) {
        var parameters = _ReadParameters(body, ref i);
        if (parameters.Length < 2)
          throw new InvalidDataException("SIXEL raster attributes require Pan and Pad.");
        if (parameters[0] <= 0 || parameters[1] <= 0)
          throw new InvalidDataException("SIXEL raster aspect ratio Pan and Pad must be positive.");

        if (parameters.Length >= 4) {
          if (parameters[2] < 0 || parameters[3] < 0)
            throw new InvalidDataException("SIXEL raster dimensions cannot be negative.");
          rasterWidth = Math.Max(rasterWidth, parameters[2]);
          rasterHeight = Math.Max(rasterHeight, parameters[3]);
          _ValidateExtent(rasterWidth, rasterHeight, allowZero: true);
        }
        continue;
      }

      if (ch == _RLE_INTRO) {
        var count = _ReadRequiredInt(body, ref i, "SIXEL repeat count");
        if (count > _MAX_REPEAT)
          throw new InvalidDataException($"SIXEL repeat count {count} exceeds the implementation safety limit {_MAX_REPEAT}.");
        if (i >= body.Length || body[i] is < _SIXEL_BASE or > '~')
          throw new InvalidDataException("SIXEL repeat introducer is not followed by a SIXEL data character.");

        var sixelValue = body[i++] - _SIXEL_BASE;
        for (var repeat = 0; repeat < count; ++repeat) {
          _PlotSixel(pixels, x, bandY, sixelValue, currentColor, backgroundMode, ref maxX, ref maxY);
          ++x;
          if (x > _MAX_DIMENSION)
            throw new InvalidDataException($"SIXEL width exceeds {_MAX_DIMENSION} pixels.");
        }
        continue;
      }

      if (ch == _CARRIAGE_RETURN) {
        x = 0;
        continue;
      }

      if (ch == _NEW_LINE) {
        x = 0;
        bandY = checked(bandY + _BAND_HEIGHT);
        if (bandY > _MAX_DIMENSION)
          throw new InvalidDataException($"SIXEL height exceeds {_MAX_DIMENSION} pixels.");
        continue;
      }

      if (ch is >= _SIXEL_BASE and <= '~') {
        var sixelValue = ch - _SIXEL_BASE;
        _PlotSixel(pixels, x, bandY, sixelValue, currentColor, backgroundMode, ref maxX, ref maxY);
        ++x;
        if (x > _MAX_DIMENSION)
          throw new InvalidDataException($"SIXEL width exceeds {_MAX_DIMENSION} pixels.");
        continue;
      }

      // C0 whitespace can occur while a DCS is transported through text-oriented tooling. It has no
      // graphics meaning and is harmless to ignore; printable unknown syntax is not.
      if (ch is '\0' or '\t' or '\r' or '\n' or ' ')
        continue;

      throw new InvalidDataException($"Unsupported character 0x{(int)ch:X2} in SIXEL data.");
    }

    width = Math.Max(rasterWidth, maxX + 1);
    height = Math.Max(rasterHeight, maxY + 1);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException("SIXEL stream does not define a positive raster extent.");
    _ValidateExtent(width, height, allowZero: false);

    var result = new byte[checked(width * height)];
    foreach (var ((px, py), color) in pixels)
      if ((uint)px < (uint)width && (uint)py < (uint)height)
        result[py * width + px] = color;

    paletteColorCount = 16;
    foreach (var key in colors.Keys)
      paletteColorCount = Math.Max(paletteColorCount, key + 1);

    palette = new byte[paletteColorCount * 3];
    foreach (var (index, (r, g, b)) in colors) {
      if (index >= paletteColorCount)
        continue;
      palette[index * 3] = r;
      palette[index * 3 + 1] = g;
      palette[index * 3 + 2] = b;
    }

    return result;
  }

  /// <summary>Encodes indexed pixel data into a SIXEL body, including a precise raster extent.</summary>
  public static string Encode(byte[] pixelData, int width, int height, byte[]? palette, int paletteColorCount) {
    ArgumentNullException.ThrowIfNull(pixelData);
    _ValidateExtent(width, height, allowZero: false);
    if (pixelData.Length != checked(width * height))
      throw new ArgumentException("SIXEL pixel data length does not match width and height.", nameof(pixelData));
    if (paletteColorCount is < 0 or > 256)
      throw new ArgumentOutOfRangeException(nameof(paletteColorCount));
    if (paletteColorCount > 0 && (palette == null || palette.Length < paletteColorCount * 3))
      throw new ArgumentException("SIXEL palette is shorter than PaletteColorCount.", nameof(palette));

    var sb = new StringBuilder();
    // DECGRA: square pixels and exact raster dimensions. Ph/Pv preserve blank right/bottom edges;
    // inferring dimensions from set bits alone cannot do that.
    sb.Append('"').Append("1;1;").Append(width).Append(';').Append(height);

    var usedColors = new SortedSet<byte>();
    for (var pixel = 0; pixel < pixelData.Length; ++pixel)
      usedColors.Add(pixelData[pixel]);

    if (palette != null)
      foreach (var color in usedColors) {
        if (color >= paletteColorCount)
          throw new ArgumentException($"Pixel data uses color index {color}, but the palette has only {paletteColorCount} entries.", nameof(pixelData));

        var offset = color * 3;
        sb.Append('#').Append(color).Append(';').Append((int)SixelColorMode.Rgb).Append(';')
          .Append(_ByteToPercent(palette[offset])).Append(';')
          .Append(_ByteToPercent(palette[offset + 1])).Append(';')
          .Append(_ByteToPercent(palette[offset + 2]));
      }

    var bandCount = (height + _BAND_HEIGHT - 1) / _BAND_HEIGHT;
    for (var band = 0; band < bandCount; ++band) {
      var bandTop = band * _BAND_HEIGHT;
      var isFirstColorInBand = true;

      foreach (var color in usedColors) {
        var lastNonZeroColumn = -1;
        for (var column = width - 1; column >= 0 && lastNonZeroColumn < 0; --column)
          for (var row = 0; row < _BAND_HEIGHT; ++row) {
            var y = bandTop + row;
            if (y < height && pixelData[y * width + column] == color) {
              lastNonZeroColumn = column;
              break;
            }
          }

        if (lastNonZeroColumn < 0)
          continue;

        if (!isFirstColorInBand)
          sb.Append(_CARRIAGE_RETURN);
        isFirstColorInBand = false;
        sb.Append(_COLOR_INTRO).Append(color);

        var columnIndex = 0;
        while (columnIndex <= lastNonZeroColumn) {
          var sixelValue = _GetSixelValue(pixelData, width, height, bandTop, columnIndex, color);
          var runStart = columnIndex++;
          while (columnIndex <= lastNonZeroColumn &&
                 _GetSixelValue(pixelData, width, height, bandTop, columnIndex, color) == sixelValue)
            ++columnIndex;

          _AppendRun(sb, (char)(_SIXEL_BASE + sixelValue), columnIndex - runStart);
        }
      }

      if (band < bandCount - 1)
        sb.Append(_NEW_LINE);
    }

    return sb.ToString();
  }

  private static int _GetSixelValue(byte[] pixels, int width, int height, int bandTop, int column, byte color) {
    var value = 0;
    for (var row = 0; row < _BAND_HEIGHT; ++row) {
      var y = bandTop + row;
      if (y < height && pixels[y * width + column] == color)
        value |= 1 << row;
    }
    return value;
  }

  private static void _AppendRun(StringBuilder sb, char sixel, int count) {
    while (count > 0) {
      var chunk = Math.Min(count, 32_766); // DEC printer interoperability limit for DECGRI.
      if (chunk >= 4)
        sb.Append(_RLE_INTRO).Append(chunk).Append(sixel);
      else
        sb.Append(sixel, chunk);
      count -= chunk;
    }
  }

  private static void _PlotSixel(
    Dictionary<(int X, int Y), byte> pixels,
    int x,
    int bandY,
    int sixelValue,
    byte color,
    int backgroundMode,
    ref int maxX,
    ref int maxY
  ) {
    maxX = Math.Max(maxX, x);
    maxY = Math.Max(maxY, bandY + _BAND_HEIGHT - 1);

    for (var bit = 0; bit < _BAND_HEIGHT; ++bit) {
      var position = (x, bandY + bit);
      if ((sixelValue & (1 << bit)) != 0)
        pixels[position] = color;
      else if (backgroundMode != 1)
        pixels[position] = 0;
    }
  }

  private static int[] _ReadParameters(string text, ref int index) {
    var values = new List<int>();
    var value = 0;
    var hasDigits = false;
    var sawParameterSyntax = false;

    while (index < text.Length) {
      var ch = text[index];
      if (ch is >= '0' and <= '9') {
        sawParameterSyntax = true;
        hasDigits = true;
        try {
          value = checked(value * 10 + ch - '0');
        } catch (OverflowException exception) {
          throw new InvalidDataException("SIXEL parameter is too large.", exception);
        }
        ++index;
        continue;
      }

      if (ch == ';') {
        sawParameterSyntax = true;
        values.Add(hasDigits ? value : 0);
        value = 0;
        hasDigits = false;
        ++index;
        continue;
      }
      break;
    }

    if (hasDigits || sawParameterSyntax)
      values.Add(hasDigits ? value : 0);
    return values.ToArray();
  }

  private static int _ReadRequiredInt(string text, ref int index, string fieldName) {
    if (index >= text.Length || text[index] is < '0' or > '9')
      throw new InvalidDataException($"{fieldName} is missing.");

    var value = 0;
    while (index < text.Length && text[index] is >= '0' and <= '9') {
      try {
        value = checked(value * 10 + text[index] - '0');
      } catch (OverflowException exception) {
        throw new InvalidDataException($"{fieldName} is too large.", exception);
      }
      ++index;
    }
    return value;
  }

  private static Dictionary<int, (byte R, byte G, byte B)> _CreateDefaultColors() {
    // VT340 default colour map, expressed in the RGB percentages published by DEC.
    int[][] percentages = [
      [0, 0, 0], [20, 20, 80], [80, 13, 13], [20, 80, 20],
      [80, 20, 80], [20, 80, 80], [80, 80, 20], [53, 53, 53],
      [26, 26, 26], [33, 33, 60], [60, 26, 26], [33, 60, 33],
      [60, 33, 60], [33, 60, 60], [60, 60, 33], [80, 80, 80],
    ];

    var result = new Dictionary<int, (byte R, byte G, byte B)>(16);
    for (var i = 0; i < percentages.Length; ++i)
      result[i] = _RgbPercentToRgb(percentages[i][0], percentages[i][1], percentages[i][2]);
    return result;
  }

  private static (byte R, byte G, byte B) _RgbPercentToRgb(int r, int g, int b) {
    _ValidatePercent(r, nameof(r));
    _ValidatePercent(g, nameof(g));
    _ValidatePercent(b, nameof(b));
    return (_PercentToByte(r), _PercentToByte(g), _PercentToByte(b));
  }

  private static (byte R, byte G, byte B) _HlsToRgb(int h, int l, int s) {
    if (h is < 0 or > 360)
      throw new InvalidDataException($"SIXEL HLS hue {h} is outside 0..360 degrees.");
    _ValidatePercent(l, nameof(l));
    _ValidatePercent(s, nameof(s));

    if (s == 0) {
      var gray = _PercentToByte(l);
      return (gray, gray, gray);
    }

    // DEC's HLS wheel is deliberately rotated relative to the conventional HSL wheel:
    // blue=0°, red=120°, green=240°. Converting to the conventional wheel is +240°.
    var standardHue = (h + 240) % 360 / 360.0;
    var lum = l / 100.0;
    var sat = s / 100.0;
    var q = lum < 0.5 ? lum * (1.0 + sat) : lum + sat - lum * sat;
    var p = 2.0 * lum - q;

    return (
      _UnitToByte(_HueToRgb(p, q, standardHue + 1.0 / 3.0)),
      _UnitToByte(_HueToRgb(p, q, standardHue)),
      _UnitToByte(_HueToRgb(p, q, standardHue - 1.0 / 3.0))
    );
  }

  private static double _HueToRgb(double p, double q, double t) {
    if (t < 0.0)
      t += 1.0;
    if (t > 1.0)
      t -= 1.0;
    if (t < 1.0 / 6.0)
      return p + (q - p) * 6.0 * t;
    if (t < 1.0 / 2.0)
      return q;
    if (t < 2.0 / 3.0)
      return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
    return p;
  }

  private static byte _PercentToByte(int value) => (byte)((value * 255 + 50) / 100);
  private static int _ByteToPercent(byte value) => (value * 100 + 127) / 255;
  private static byte _UnitToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);

  private static void _ValidatePercent(int value, string name) {
    if (value is < 0 or > 100)
      throw new InvalidDataException($"SIXEL {name} value {value} is outside 0..100 percent.");
  }

  private static void _ValidateExtent(int width, int height, bool allowZero) {
    var minimum = allowZero ? 0 : 1;
    if (width < minimum || height < minimum || width > _MAX_DIMENSION || height > _MAX_DIMENSION)
      throw new InvalidDataException($"SIXEL raster extent {width}x{height} is outside the supported range.");
    if ((long)width * height > _MAX_PIXELS)
      throw new InvalidDataException($"SIXEL raster contains more than {_MAX_PIXELS:N0} pixels.");
  }
}
