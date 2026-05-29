using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Sixel;

/// <summary>Encodes and decodes Sixel pixel data.</summary>
internal static class SixelCodec {

  private const char _SIXEL_BASE = '?'; // 63
  private const char _COLOR_INTRO = '#';
  private const char _CARRIAGE_RETURN = '$';
  private const char _NEW_LINE = '-';
  private const char _RLE_INTRO = '!';
  private const int _BAND_HEIGHT = 6;

  /// <summary>Decodes Sixel body text into indexed pixel data and palette.</summary>
  /// <param name="body">Sixel body (after DCS parameters 'q', before ST).</param>
  /// <param name="width">Output: image width in pixels.</param>
  /// <param name="height">Output: image height in pixels.</param>
  /// <param name="palette">Output: RGB palette (3 bytes per color).</param>
  /// <param name="paletteColorCount">Output: number of defined palette colors.</param>
  /// <returns>Indexed pixel data (1 byte per pixel).</returns>
  public static byte[] Decode(string body, out int width, out int height, out byte[]? palette, out int paletteColorCount) {
    var colors = new Dictionary<int, (byte R, byte G, byte B)>();
    var pixels = new Dictionary<(int X, int Y), byte>();
    var maxX = 0;
    var maxY = 0;
    var x = 0;
    var bandY = 0;
    byte currentColor = 0;
    var i = 0;

    while (i < body.Length) {
      var ch = body[i];

      if (ch == _COLOR_INTRO) {
        ++i;
        var colorIndex = _ReadInt(body, ref i);
        if (i < body.Length && body[i] == ';') {
          ++i;
          var mode = _ReadInt(body, ref i);
          _SkipChar(body, ref i, ';');
          var v1 = _ReadInt(body, ref i);
          _SkipChar(body, ref i, ';');
          var v2 = _ReadInt(body, ref i);
          _SkipChar(body, ref i, ';');
          var v3 = _ReadInt(body, ref i);

          if (mode == (int)SixelColorMode.Rgb)
            colors[colorIndex] = ((byte)(v1 * 255 / 100), (byte)(v2 * 255 / 100), (byte)(v3 * 255 / 100));
          else
            colors[colorIndex] = _HlsToRgb(v1, v2, v3);
        }
        currentColor = (byte)colorIndex;
      } else if (ch == _RLE_INTRO) {
        ++i;
        var count = _ReadInt(body, ref i);
        if (i < body.Length && body[i] >= _SIXEL_BASE && body[i] <= '~') {
          var sixelValue = body[i] - _SIXEL_BASE;
          ++i;
          for (var r = 0; r < count; ++r) {
            _PlotSixel(pixels, x, bandY, sixelValue, currentColor, ref maxX, ref maxY);
            ++x;
          }
        }
      } else if (ch == _CARRIAGE_RETURN) {
        x = 0;
        ++i;
      } else if (ch == _NEW_LINE) {
        x = 0;
        bandY += _BAND_HEIGHT;
        ++i;
      } else if (ch >= _SIXEL_BASE && ch <= '~') {
        var sixelValue = ch - _SIXEL_BASE;
        _PlotSixel(pixels, x, bandY, sixelValue, currentColor, ref maxX, ref maxY);
        ++x;
        ++i;
      } else {
        ++i;
      }
    }

    width = maxX + 1;
    height = maxY + 1;

    var result = new byte[width * height];
    foreach (var ((px, py), color) in pixels) {
      if (px < width && py < height)
        result[py * width + px] = color;
    }

    if (colors.Count > 0) {
      paletteColorCount = 0;
      foreach (var key in colors.Keys)
        if (key >= paletteColorCount)
          paletteColorCount = key + 1;

      palette = new byte[paletteColorCount * 3];
      foreach (var (idx, (r, g, b)) in colors) {
        palette[idx * 3] = r;
        palette[idx * 3 + 1] = g;
        palette[idx * 3 + 2] = b;
      }
    } else {
      palette = null;
      paletteColorCount = 0;
    }

    return result;
  }

  /// <summary>Encodes indexed pixel data into Sixel body text.</summary>
  /// <param name="pixelData">Indexed pixel data (1 byte per pixel).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="palette">RGB palette (3 bytes per color), or null.</param>
  /// <param name="paletteColorCount">Number of palette colors.</param>
  /// <returns>Sixel body text (color defs + sixel data).</returns>
  public static string Encode(byte[] pixelData, int width, int height, byte[]? palette, int paletteColorCount) {
    var sb = new StringBuilder();

    if (palette != null)
      for (var c = 0; c < paletteColorCount; ++c) {
        var r = palette[c * 3] * 100 / 255;
        var g = palette[c * 3 + 1] * 100 / 255;
        var b = palette[c * 3 + 2] * 100 / 255;
        sb.Append($"#{c};{(int)SixelColorMode.Rgb};{r};{g};{b}");
      }

    var bandCount = (height + _BAND_HEIGHT - 1) / _BAND_HEIGHT;

    var usedColors = new HashSet<byte>();
    for (var i = 0; i < pixelData.Length; ++i)
      usedColors.Add(pixelData[i]);

    for (var band = 0; band < bandCount; ++band) {
      var bandTop = band * _BAND_HEIGHT;
      var isFirstColorInBand = true;

      foreach (var color in usedColors) {
        var hasPixel = false;
        for (var col = 0; col < width && !hasPixel; ++col)
          for (var row = 0; row < _BAND_HEIGHT; ++row) {
            var y = bandTop + row;
            if (y >= height)
              continue;
            if (pixelData[y * width + col] == color) {
              hasPixel = true;
              break;
            }
          }

        if (!hasPixel)
          continue;

        if (!isFirstColorInBand)
          sb.Append(_CARRIAGE_RETURN);
        isFirstColorInBand = false;

        sb.Append(_COLOR_INTRO);
        sb.Append(color);

        var col2 = 0;
        while (col2 < width) {
          var sixelValue = 0;
          for (var row = 0; row < _BAND_HEIGHT; ++row) {
            var y = bandTop + row;
            if (y < height && pixelData[y * width + col2] == color)
              sixelValue |= 1 << row;
          }

          var runStart = col2;
          ++col2;
          while (col2 < width) {
            var nextSixel = 0;
            for (var row = 0; row < _BAND_HEIGHT; ++row) {
              var y = bandTop + row;
              if (y < height && pixelData[y * width + col2] == color)
                nextSixel |= 1 << row;
            }
            if (nextSixel != sixelValue)
              break;
            ++col2;
          }

          var runLength = col2 - runStart;
          var sixelChar = (char)(_SIXEL_BASE + sixelValue);

          if (runLength >= 4) {
            sb.Append(_RLE_INTRO);
            sb.Append(runLength);
            sb.Append(sixelChar);
          } else
            for (var r = 0; r < runLength; ++r)
              sb.Append(sixelChar);
        }
      }

      if (band < bandCount - 1)
        sb.Append(_NEW_LINE);
    }

    return sb.ToString();
  }

  private static void _PlotSixel(Dictionary<(int X, int Y), byte> pixels, int x, int bandY, int sixelValue, byte color, ref int maxX, ref int maxY) {
    for (var bit = 0; bit < _BAND_HEIGHT; ++bit)
      if ((sixelValue & (1 << bit)) != 0) {
        var y = bandY + bit;
        pixels[(x, y)] = color;
        if (x > maxX)
          maxX = x;
        if (y > maxY)
          maxY = y;
      }
  }

  private static int _ReadInt(string s, ref int i) {
    var result = 0;
    while (i < s.Length && s[i] >= '0' && s[i] <= '9') {
      result = result * 10 + (s[i] - '0');
      ++i;
    }
    return result;
  }

  private static void _SkipChar(string s, ref int i, char expected) {
    if (i < s.Length && s[i] == expected)
      ++i;
  }

  private static (byte R, byte G, byte B) _HlsToRgb(int h, int l, int s) {
    if (s == 0) {
      var gray = (byte)(l * 255 / 100);
      return (gray, gray, gray);
    }

    var hue = h / 360.0;
    var lum = l / 100.0;
    var sat = s / 100.0;

    var q = lum < 0.5 ? lum * (1.0 + sat) : lum + sat - lum * sat;
    var p = 2.0 * lum - q;

    return (
      (byte)(Math.Clamp(_HueToRgb(p, q, hue + 1.0 / 3.0), 0.0, 1.0) * 255),
      (byte)(Math.Clamp(_HueToRgb(p, q, hue), 0.0, 1.0) * 255),
      (byte)(Math.Clamp(_HueToRgb(p, q, hue - 1.0 / 3.0), 0.0, 1.0) * 255)
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
}
