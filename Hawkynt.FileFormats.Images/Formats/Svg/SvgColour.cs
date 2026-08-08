using System;
using System.Collections.Generic;
using System.Globalization;
using FileFormat.Core;

namespace FileFormat.Svg;

/// <summary>Reads the colours an SVG attribute is written with.</summary>
/// <remarks>
/// Four notations: a hash and three or six hexadecimal digits, <c>rgb()</c> with numbers or
/// percentages, one of the names the specification lists, and the keywords <c>none</c> and
/// <c>currentColor</c>. The names are the CSS list the specification adopts wholesale, so they are
/// written out here rather than approximated — a drawing that says <c>peachpuff</c> means a
/// particular colour and no other.
/// </remarks>
public static class SvgColour {

  /// <summary>Every colour name the specification defines, as a name and its packed RGB.</summary>
  private static readonly Dictionary<string, int> _Names = _Build(
    "aliceblue F0F8FF antiquewhite FAEBD7 aqua 00FFFF aquamarine 7FFFD4 azure F0FFFF beige F5F5DC " +
    "bisque FFE4C4 black 000000 blanchedalmond FFEBCD blue 0000FF blueviolet 8A2BE2 brown A52A2A " +
    "burlywood DEB887 cadetblue 5F9EA0 chartreuse 7FFF00 chocolate D2691E coral FF7F50 " +
    "cornflowerblue 6495ED cornsilk FFF8DC crimson DC143C cyan 00FFFF darkblue 00008B darkcyan 008B8B " +
    "darkgoldenrod B8860B darkgray A9A9A9 darkgreen 006400 darkgrey A9A9A9 darkkhaki BDB76B " +
    "darkmagenta 8B008B darkolivegreen 556B2F darkorange FF8C00 darkorchid 9932CC darkred 8B0000 " +
    "darksalmon E9967A darkseagreen 8FBC8F darkslateblue 483D8B darkslategray 2F4F4F darkslategrey 2F4F4F " +
    "darkturquoise 00CED1 darkviolet 9400D3 deeppink FF1493 deepskyblue 00BFFF dimgray 696969 " +
    "dimgrey 696969 dodgerblue 1E90FF firebrick B22222 floralwhite FFFAF0 forestgreen 228B22 " +
    "fuchsia FF00FF gainsboro DCDCDC ghostwhite F8F8FF gold FFD700 goldenrod DAA520 gray 808080 " +
    "grey 808080 green 008000 greenyellow ADFF2F honeydew F0FFF0 hotpink FF69B4 indianred CD5C5C " +
    "indigo 4B0082 ivory FFFFF0 khaki F0E68C lavender E6E6FA lavenderblush FFF0F5 lawngreen 7CFC00 " +
    "lemonchiffon FFFACD lightblue ADD8E6 lightcoral F08080 lightcyan E0FFFF lightgoldenrodyellow FAFAD2 " +
    "lightgray D3D3D3 lightgreen 90EE90 lightgrey D3D3D3 lightpink FFB6C1 lightsalmon FFA07A " +
    "lightseagreen 20B2AA lightskyblue 87CEFA lightslategray 778899 lightslategrey 778899 " +
    "lightsteelblue B0C4DE lightyellow FFFFE0 lime 00FF00 limegreen 32CD32 linen FAF0E6 magenta FF00FF " +
    "maroon 800000 mediumaquamarine 66CDAA mediumblue 0000CD mediumorchid BA55D3 mediumpurple 9370DB " +
    "mediumseagreen 3CB371 mediumslateblue 7B68EE mediumspringgreen 00FA9A mediumturquoise 48D1CC " +
    "mediumvioletred C71585 midnightblue 191970 mintcream F5FFFA mistyrose FFE4E1 moccasin FFE4B5 " +
    "navajowhite FFDEAD navy 000080 oldlace FDF5E6 olive 808000 olivedrab 6B8E23 orange FFA500 " +
    "orangered FF4500 orchid DA70D6 palegoldenrod EEE8AA palegreen 98FB98 paleturquoise AFEEEE " +
    "palevioletred DB7093 papayawhip FFEFD5 peachpuff FFDAB9 peru CD853F pink FFC0CB plum DDA0DD " +
    "powderblue B0E0E6 purple 800080 red FF0000 rosybrown BC8F8F royalblue 4169E1 saddlebrown 8B4513 " +
    "salmon FA8072 sandybrown F4A460 seagreen 2E8B57 seashell FFF5EE sienna A0522D silver C0C0C0 " +
    "skyblue 87CEEB slateblue 6A5ACD slategray 708090 slategrey 708090 snow FFFAFA springgreen 00FF7F " +
    "steelblue 4682B4 tan D2B48C teal 008080 thistle D8BFD8 tomato FF6347 turquoise 40E0D0 " +
    "violet EE82EE wheat F5DEB3 white FFFFFF whitesmoke F5F5F5 yellow FFFF00 yellowgreen 9ACD32");

  /// <summary>Reads a colour, or nothing when the text names no colour or names none at all.</summary>
  public static bool TryParse(string? text, out Rgba32 colour) {
    colour = Rgba32.Black;
    if (string.IsNullOrWhiteSpace(text))
      return false;

    var value = text.Trim();
    if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
      return false;

    if (value[0] == '#')
      return _TryHex(value.AsSpan(1), out colour);

    if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) {
      var open = value.IndexOf('(');
      var close = value.LastIndexOf(')');
      if (open < 0 || close <= open)
        return false;

      var body = value[(open + 1)..close];
      var percent = body.Contains('%');
      var parts = SvgLength.Numbers(body);
      if (parts.Length < 3)
        return false;

      var scale = percent ? 2.55 : 1;
      colour = new(_Byte(parts[0] * scale), _Byte(parts[1] * scale), _Byte(parts[2] * scale), parts.Length > 3 ? _Byte(parts[3] * 255) : (byte)255);
      return true;
    }

    if (!_Names.TryGetValue(value.ToLowerInvariant(), out var packed))
      return false;

    colour = new((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    return true;
  }

  private static bool _TryHex(ReadOnlySpan<char> digits, out Rgba32 colour) {
    colour = Rgba32.Black;

    switch (digits.Length) {
      case 3:
      case 4: {
        Span<byte> channels = stackalloc byte[4] { 255, 255, 255, 255 };
        for (var i = 0; i < digits.Length; ++i) {
          if (!_TryDigit(digits[i], out var value))
            return false;

          channels[i] = (byte)(value * 17);
        }

        colour = new(channels[0], channels[1], channels[2], digits.Length == 4 ? channels[3] : (byte)255);
        return true;
      }

      case 6:
      case 8: {
        Span<byte> channels = stackalloc byte[4] { 255, 255, 255, 255 };
        for (var i = 0; i * 2 + 1 < digits.Length; ++i) {
          if (!byte.TryParse(digits.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out channels[i]))
            return false;
        }

        colour = new(channels[0], channels[1], channels[2], digits.Length == 8 ? channels[3] : (byte)255);
        return true;
      }

      default:
        return false;
    }
  }

  private static bool _TryDigit(char c, out int value) {
    value = c switch {
      >= '0' and <= '9' => c - '0',
      >= 'a' and <= 'f' => c - 'a' + 10,
      >= 'A' and <= 'F' => c - 'A' + 10,
      _ => -1
    };

    return value >= 0;
  }

  private static byte _Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

  private static Dictionary<string, int> _Build(string table) {
    var parts = table.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var names = new Dictionary<string, int>(parts.Length / 2, StringComparer.Ordinal);
    for (var i = 0; i + 1 < parts.Length; i += 2)
      names[parts[i]] = int.Parse(parts[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    return names;
  }
}
