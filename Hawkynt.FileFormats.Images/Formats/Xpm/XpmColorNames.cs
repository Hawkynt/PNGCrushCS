using System;
using System.Collections.Generic;
using System.Globalization;

namespace FileFormat.Xpm;

/// <summary>Resolving the colour spellings an XPM may use.</summary>
/// <remarks>
/// XPM inherited X11's colour syntax, which is three things at once: a hash followed by hex digits
/// in any of several widths, a name from the X server's table, or the word for transparent. A
/// reader that understands only six hex digits rejects most files written by hand or by a tool that
/// prefers names — which is nearly all of them.
/// <para/>
/// The grey ramp is generated rather than tabulated: X11 defines <c>gray0</c> through
/// <c>gray100</c> as percentages, so a hundred and one entries each for two spellings would be six
/// hundred lines of table saying one multiplication.
/// </remarks>
public static class XpmColorNames {

  /// <summary>Turns a colour value into a triplet, or returns false if it names nothing.</summary>
  public static bool TryResolve(string value, out byte red, out byte green, out byte blue) {
    red = green = blue = 0;
    if (string.IsNullOrWhiteSpace(value))
      return false;

    value = value.Trim();

    return value[0] == '#'
      ? _TryHex(value[1..], out red, out green, out blue)
      : _TryName(value, out red, out green, out blue);
  }

  /// <summary>
  /// Reads the hash form, whose digits may be three, six, nine or twelve — one, two, three or four
  /// per channel. The wider ones are narrowed by their leading digits, which is where the magnitude
  /// is; taking the trailing ones would turn white into black.
  /// </summary>
  private static bool _TryHex(string hex, out byte red, out byte green, out byte blue) {
    red = green = blue = 0;
    if (hex.Length % 3 != 0)
      return false;

    var perChannel = hex.Length / 3;
    if (perChannel is < 1 or > 4)
      return false;

    Span<byte> channels = stackalloc byte[3];
    for (var i = 0; i < 3; ++i) {
      var part = hex.Substring(i * perChannel, perChannel);
      if (!int.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
        return false;

      channels[i] = perChannel switch {
        1 => (byte)(raw * 17),
        2 => (byte)raw,
        _ => (byte)(raw >> ((perChannel - 2) * 4)),
      };
    }

    (red, green, blue) = (channels[0], channels[1], channels[2]);

    return true;
  }

  private static bool _TryName(string name, out byte red, out byte green, out byte blue) {
    red = green = blue = 0;

    if (_Named.TryGetValue(name, out var packed)) {
      red = (byte)(packed >> 16);
      green = (byte)(packed >> 8);
      blue = (byte)packed;

      return true;
    }

    // gray0 through gray100, in either spelling, are percentages rather than table entries.
    foreach (var prefix in new[] { "gray", "grey" }) {
      if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        continue;

      var rest = name[prefix.Length..];
      if (rest.Length == 0 || !int.TryParse(rest, out var percent) || percent is < 0 or > 100)
        continue;

      red = green = blue = (byte)((percent * 255 + 50) / 100);

      return true;
    }

    return false;
  }

  /// <summary>The X11 names a picture is realistically written with, as 0xRRGGBB.</summary>
  private static readonly Dictionary<string, int> _Named = new(StringComparer.OrdinalIgnoreCase) {
    // The rest of the X11 table, added because a picture naming DarkSlateGray was refused
    // outright — a reader that knows sixty of the names knows none of the files that use the
    // other eighty.
    ["aliceblue"] = 0xF0F8FF, ["antiquewhite"] = 0xFAEBD7, ["blanchedalmond"] = 0xFFEBCD,
    ["blueviolet"] = 0x8A2BE2, ["burlywood"] = 0xDEB887, ["cornflowerblue"] = 0x6495ED,
    ["darkcyan"] = 0x008B8B, ["darkgoldenrod"] = 0xB8860B, ["darkmagenta"] = 0x8B008B,
    ["darkolivegreen"] = 0x556B2F, ["darkorchid"] = 0x9932CC, ["darksalmon"] = 0xE9967A,
    ["darkseagreen"] = 0x8FBC8F, ["darkslateblue"] = 0x483D8B, ["darkslategray"] = 0x2F4F4F,
    ["darkslategrey"] = 0x2F4F4F, ["darkturquoise"] = 0x00CED1, ["darkviolet"] = 0x9400D3,
    ["deeppink"] = 0xFF1493, ["deepskyblue"] = 0x00BFFF, ["dodgerblue"] = 0x1E90FF,
    ["floralwhite"] = 0xFFFAF0, ["ghostwhite"] = 0xF8F8FF, ["indianred"] = 0xCD5C5C,
    ["khaki1"] = 0xFFF68F, ["lavenderblush"] = 0xFFF0F5, ["lawngreen"] = 0x7CFC00,
    ["lemonchiffon"] = 0xFFFACD, ["lightcoral"] = 0xF08080, ["lightcyan"] = 0xE0FFFF,
    ["lightgoldenrod"] = 0xEEDD82, ["lightgoldenrodyellow"] = 0xFAFAD2, ["lightgreen"] = 0x90EE90,
    ["lightpink"] = 0xFFB6C1, ["lightsalmon"] = 0xFFA07A, ["lightseagreen"] = 0x20B2AA,
    ["lightskyblue"] = 0x87CEFA, ["lightslateblue"] = 0x8470FF, ["lightslategray"] = 0x778899,
    ["lightslategrey"] = 0x778899, ["lightsteelblue"] = 0xB0C4DE, ["lightyellow"] = 0xFFFFE0,
    ["mediumaquamarine"] = 0x66CDAA, ["mediumorchid"] = 0xBA55D3, ["mediumpurple"] = 0x9370DB,
    ["mediumseagreen"] = 0x3CB371, ["mediumslateblue"] = 0x7B68EE, ["mediumspringgreen"] = 0x00FA9A,
    ["mediumturquoise"] = 0x48D1CC, ["mediumvioletred"] = 0xC71585, ["mintcream"] = 0xF5FFFA,
    ["moccasin"] = 0xFFE4B5, ["navajowhite"] = 0xFFDEAD, ["oldlace"] = 0xFDF5E6,
    ["orangered"] = 0xFF4500, ["palegoldenrod"] = 0xEEE8AA, ["palegreen"] = 0x98FB98,
    ["paleturquoise"] = 0xAFEEEE, ["palevioletred"] = 0xDB7093, ["papayawhip"] = 0xFFEFD5,
    ["powderblue"] = 0xB0E0E6, ["saddlebrown"] = 0x8B4513, ["sandybrown"] = 0xF4A460,
    ["whitesmoke"] = 0xF5F5F5,
    ["black"] = 0x000000, ["white"] = 0xFFFFFF, ["red"] = 0xFF0000, ["green"] = 0x00FF00,
    ["blue"] = 0x0000FF, ["cyan"] = 0x00FFFF, ["magenta"] = 0xFF00FF, ["yellow"] = 0xFFFF00,
    ["gray"] = 0xBEBEBE, ["grey"] = 0xBEBEBE, ["darkgray"] = 0xA9A9A9, ["darkgrey"] = 0xA9A9A9,
    ["lightgray"] = 0xD3D3D3, ["lightgrey"] = 0xD3D3D3, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969,
    ["navy"] = 0x000080, ["navyblue"] = 0x000080, ["darkblue"] = 0x00008B, ["mediumblue"] = 0x0000CD,
    ["royalblue"] = 0x4169E1, ["steelblue"] = 0x4682B4, ["skyblue"] = 0x87CEEB, ["lightblue"] = 0xADD8E6,
    ["darkgreen"] = 0x006400, ["forestgreen"] = 0x228B22, ["limegreen"] = 0x32CD32,
    ["seagreen"] = 0x2E8B57, ["olive"] = 0x808000, ["olivedrab"] = 0x6B8E23, ["lime"] = 0x00FF00,
    ["maroon"] = 0xB03060, ["darkred"] = 0x8B0000, ["firebrick"] = 0xB22222, ["crimson"] = 0xDC143C,
    ["orange"] = 0xFFA500, ["darkorange"] = 0xFF8C00, ["gold"] = 0xFFD700, ["coral"] = 0xFF7F50,
    ["pink"] = 0xFFC0CB, ["hotpink"] = 0xFF69B4, ["violet"] = 0xEE82EE, ["purple"] = 0xA020F0,
    ["indigo"] = 0x4B0082, ["orchid"] = 0xDA70D6, ["plum"] = 0xDDA0DD, ["lavender"] = 0xE6E6FA,
    ["brown"] = 0xA52A2A, ["sienna"] = 0xA0522D, ["chocolate"] = 0xD2691E, ["tan"] = 0xD2B48C,
    ["beige"] = 0xF5F5DC, ["ivory"] = 0xFFFFF0, ["khaki"] = 0xF0E68C, ["wheat"] = 0xF5DEB3,
    ["salmon"] = 0xFA8072, ["tomato"] = 0xFF6347, ["turquoise"] = 0x40E0D0, ["teal"] = 0x008080,
    ["aqua"] = 0x00FFFF, ["fuchsia"] = 0xFF00FF, ["silver"] = 0xC0C0C0, ["gainsboro"] = 0xDCDCDC,
    ["snow"] = 0xFFFAFA, ["linen"] = 0xFAF0E6, ["azure"] = 0xF0FFFF, ["honeydew"] = 0xF0FFF0,
    ["midnightblue"] = 0x191970, ["slateblue"] = 0x6A5ACD, ["slategray"] = 0x708090,
    ["slategrey"] = 0x708090, ["cadetblue"] = 0x5F9EA0, ["aquamarine"] = 0x7FFFD4,
    ["springgreen"] = 0x00FF7F, ["chartreuse"] = 0x7FFF00, ["greenyellow"] = 0xADFF2F,
    ["yellowgreen"] = 0x9ACD32, ["darkkhaki"] = 0xBDB76B, ["goldenrod"] = 0xDAA520,
    ["peru"] = 0xCD853F, ["rosybrown"] = 0xBC8F8F, ["thistle"] = 0xD8BFD8, ["peachpuff"] = 0xFFDAB9,
    ["mistyrose"] = 0xFFE4E1, ["seashell"] = 0xFFF5EE, ["cornsilk"] = 0xFFF8DC, ["bisque"] = 0xFFE4C4,
  };
}
